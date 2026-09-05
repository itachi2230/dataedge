using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace backtest.Services
{
    /// <summary>
    /// Outils « études » de l'agent IA : lecture, recherche, création, remplissage
    /// et suppression des études (.etude = XamlPackage WPF).
    ///
    /// Le fichier .etude (lourd : il embarque les images en binaire) ne quitte
    /// JAMAIS le disque : seul le contenu textuel extrait est renvoyé au modèle,
    /// les images étant remplacées par des marqueurs [image]. L'écriture se fait
    /// depuis un markdown léger (# titres, **gras**, *italique*, __souligné__,
    /// [color=...] couleur, [size=...] taille, listes « - » et « 1. ») converti
    /// en FlowDocument puis sauvegardé en XamlPackage — l'agent peut donc créer
    /// et remplir une étude sans générer un payload lourd.
    ///
    /// Tous les objets WPF (RichTextBox / FlowDocument / TextRange) exigent un
    /// thread STA : chaque opération passe par RunSta (thread dédié, cf. la
    /// migration RTF d'EtudesView qui applique la même technique).
    /// </summary>
    public static class AgentStudiesService
    {
        // Racines explorées : le module Études + les notes hebdomadaires du dashboard.
        private static readonly string[] RootFolders = { "etudes", "Notes" };

        // L'agent ne crée des études que dans le module Études (les Notes sont
        // gérées automatiquement par semaine par le MainWindow).
        private const string CreationRoot = "etudes";
        private const string Extension = ".etude";

        private const int DefaultReadChars = 8000;
        private const int MinReadChars = 500;
        private const int MaxReadChars = 24000;
        private const int DefaultSearchResults = 8;
        private const int MaxSearchResults = 25;
        private const int SnippetPadding = 90;
        private const int MaxSnippetsPerStudy = 3;

        // Seuils de taille de police interprétés comme titres à la lecture.
        private const double Heading1FontSize = 22;
        private const double Heading2FontSize = 17;

        // Couleur claire par défaut pour tout texte créé par l'agent : le fond
        // du RichTextBox d'étude étant sombre (#0A0A12), un texte noir serait
        // illisible. Le pinceau est créé paresseusement sur le thread STA dédié
        // (jamais dans le static constructor, pour ne pas initialiser WPF sur
        // le mauvais thread).
        private static SolidColorBrush _defaultTextBrush;

        private static SolidColorBrush GetDefaultTextBrush()
        {
            if (_defaultTextBrush == null)
                _defaultTextBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF5));
            return _defaultTextBrush;
        }

        // =====================================================================
        // CATALOGUE
        // =====================================================================

        /// <summary>
        /// Catalogue compact des études : nom, chemin relatif, dossier, taille,
        /// dernière modification. Aucun fichier n'est lu (payload minimal) —
        /// c'est le point d'entrée recommandé avant read/write/delete.
        /// </summary>
        public static string GetCatalog()
        {
            var entries = new List<object>();
            foreach (var path in EnumerateStudyFiles())
            {
                var info = new FileInfo(path);
                entries.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    path = NormalizePath(path),
                    folder = NormalizeFolder(path),
                    size_kb = Math.Round(info.Length / 1024.0, 1),
                    modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                });
            }
            return JsonSerializer.Serialize(new { count = entries.Count, studies = entries });
        }

        /// <summary>
        /// Chemins relatifs normalisés (ex: « etudes/SMC/ICT.etude ») — utilisés
        /// par le snapshot du workspace pour rester compact.
        /// </summary>
        public static List<string> GetRelativePaths()
        {
            return EnumerateStudyFiles().Select(NormalizePath).ToList();
        }

        // =====================================================================
        // LECTURE / RECHERCHE
        // =====================================================================

        /// <summary>
        /// Lit le contenu textuel d'une étude, images exclues (marqueurs [image]).
        /// Le texte est tronqué à max_chars pour ne pas saturer le contexte du modèle.
        /// </summary>
        public static AiToolResult Read(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name))
                return AiToolResult.Error("Le nom (ou chemin) de l'étude est obligatoire.");

            string path = ResolvePath(name);
            if (path == null)
                return AiToolResult.Error($"Étude introuvable : « {name} ». Appelle get_study_catalog pour connaître les études disponibles.");

            int maxChars = (int)GetNumber(arguments, "max_chars", DefaultReadChars);
            maxChars = Math.Max(MinReadChars, Math.Min(MaxReadChars, maxChars));

            ExtractionResult extraction = ExtractText(path);
            bool truncated = extraction.Text.Length > maxChars;
            string content = truncated ? extraction.Text.Substring(0, maxChars) + "\n\n[... contenu tronqué ...]" : extraction.Text;

            var payload = new
            {
                path = NormalizePath(path),
                name = Path.GetFileNameWithoutExtension(path),
                char_count = extraction.Text.Length,
                image_count = extraction.ImageCount,
                truncated,
                content
            };
            return AiToolResult.Success(JsonSerializer.Serialize(payload));
        }

        /// <summary>
        /// Recherche plein texte dans toutes les études (texte extrait, images ignorées)
        /// et renvoie jusqu'à max_results études avec des extraits contextuels.
        /// </summary>
        public static AiToolResult Search(JsonElement arguments)
        {
            string query = GetString(arguments, "query");
            if (string.IsNullOrWhiteSpace(query))
                return AiToolResult.Error("Le texte à rechercher est obligatoire.");

            int maxResults = (int)GetNumber(arguments, "max_results", DefaultSearchResults);
            maxResults = Math.Max(1, Math.Min(MaxSearchResults, maxResults));

            var results = new List<object>();
            foreach (var path in EnumerateStudyFiles())
            {
                ExtractionResult extraction = ExtractText(path);
                var snippets = FindSnippets(extraction.Text, query, MaxSnippetsPerStudy);
                if (snippets.Count == 0) continue;
                results.Add(new
                {
                    path = NormalizePath(path),
                    name = Path.GetFileNameWithoutExtension(path),
                    matches = snippets
                });
                if (results.Count >= maxResults) break;
            }

            var payload = new { query, count = results.Count, results };
            return AiToolResult.Success(JsonSerializer.Serialize(payload));
        }

        // =====================================================================
        // ÉCRITURE
        // =====================================================================

        /// <summary>
        /// Crée une nouvelle étude dans le module Études, éventuellement dans un
        /// sous-dossier créé au passage, avec un contenu initial markdown optionnel.
        /// </summary>
        public static AiToolResult Create(JsonElement arguments)
        {
            string name = GetString(arguments, "name").Trim();
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return AiToolResult.Error("Nom d'étude invalide (caractères interdits).");

            string folder = GetString(arguments, "folder").Trim().Trim('/');
            if (!string.IsNullOrEmpty(folder) &&
                folder.Split('/').Any(part => part == ".." || part.Length == 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                return AiToolResult.Error("Dossier invalide (uniquement des sous-dossiers du module Études).");

            string directory = string.IsNullOrEmpty(folder) ? CreationRoot : Path.Combine(CreationRoot, folder);
            string path = Path.Combine(directory, name + Extension);
            if (File.Exists(path))
                return AiToolResult.Error($"Une étude nommée « {name} » existe déjà à cet emplacement.");

            string content = GetString(arguments, "content");
            RunSta<object>(() =>
            {
                Directory.CreateDirectory(directory);
                var document = new FlowDocument();
                if (!string.IsNullOrWhiteSpace(content))
                    AppendMarkdownToDocument(document, content, insertAtStart: false);
                SaveDocument(document, path);
                return null;
            });

            return AiToolResult.Success($"Étude créée : {NormalizePath(path)}" +
                (string.IsNullOrWhiteSpace(content) ? " (vierge)." : " et remplie."));
        }

        /// <summary>
        /// Écrit du contenu markdown dans une étude existante : replace (défaut,
        /// remplace tout le contenu), append (ajoute à la fin) ou prepend (insère
        /// au début). Les images déjà présentes dans l'étude sont préservées.
        /// </summary>
        public static AiToolResult Write(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            string content = GetString(arguments, "content");
            string mode = GetString(arguments, "mode", "replace").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(content))
                return AiToolResult.Error("Le contenu à écrire est obligatoire (vide = aucune modification).");
            if (mode != "replace" && mode != "append" && mode != "prepend")
                return AiToolResult.Error("Mode invalide. Valeurs attendues : replace, append ou prepend.");

            string path = ResolvePath(name);
            if (path == null)
                return AiToolResult.Error($"Étude introuvable : « {name} ». Appelle get_study_catalog pour connaître les études disponibles.");

            RunSta<object>(() =>
            {
                var document = LoadDocument(path);

                // replace : on vide d'abord (l'ancre prepend vaudra null et les blocs
                // s'ajoutent simplement à la fin d'un document vide).
                if (mode == "replace") document.Blocks.Clear();
                AppendMarkdownToDocument(document, content, insertAtStart: mode == "prepend");
                SaveDocument(document, path);
                return null;
            });

            InvalidateCache(path);
            return AiToolResult.Success($"Étude mise à jour ({mode}) : {NormalizePath(path)}");
        }

        /// <summary>
        /// Supprime définitivement le fichier d'une étude (module Études ou Notes).
        /// </summary>
        public static AiToolResult Delete(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name))
                return AiToolResult.Error("Le nom (ou chemin) de l'étude est obligatoire.");

            string path = ResolvePath(name);
            if (path == null)
                return AiToolResult.Error($"Étude introuvable : « {name} ». Appelle get_study_catalog pour connaître les études disponibles.");

            File.Delete(path);
            InvalidateCache(path);
            return AiToolResult.Success($"Étude supprimée : {NormalizePath(path)}");
        }

        // =====================================================================
        // EXTRACTION DE TEXTE (XamlPackage → texte sans images)
        // =====================================================================

        private sealed class ExtractionResult
        {
            public string Text { get; set; }
            public int ImageCount { get; set; }
        }

        private sealed class CacheEntry
        {
            public DateTime ModifiedUtc;
            public ExtractionResult Result;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> TextCache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Extraction mise en cache : le parse XamlPackage (thread STA coûteux) n'est
        /// refait que si la date de modification du fichier a changé.
        /// </summary>
        private static ExtractionResult ExtractText(string path)
        {
            DateTime modifiedUtc = File.GetLastWriteTimeUtc(path);
            lock (CacheLock)
            {
                if (TextCache.TryGetValue(path, out var cached) && cached.ModifiedUtc == modifiedUtc)
                    return cached.Result;
            }
            ExtractionResult result = RunSta(() => ExtractTextCore(path));
            lock (CacheLock)
            {
                TextCache[path] = new CacheEntry { ModifiedUtc = File.GetLastWriteTimeUtc(path), Result = result };
            }
            return result;
        }

        private static void InvalidateCache(string path)
        {
            lock (CacheLock) TextCache.Remove(path);
        }

        /// <summary>
        /// Charge un .etude (XamlPackage) dans un FlowDocument.
        /// </summary>
        private static FlowDocument LoadDocument(string path)
        {
            var document = new FlowDocument();
            if (!File.Exists(path)) return document;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length > 0)
                {
                    TextRange range = new TextRange(document.ContentStart, document.ContentEnd);
                    range.Load(fs, DataFormats.XamlPackage);
                }
            }
            return document;
        }

        /// <summary>
        /// Sauvegarde un FlowDocument en .etude (XamlPackage).
        /// </summary>
        private static void SaveDocument(FlowDocument document, string filePath)
        {
            TextRange range = new TextRange(document.ContentStart, document.ContentEnd);
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                range.Save(fs, DataFormats.XamlPackage);
            }
        }

        /// <summary>
        /// Parcourt l'arbre d'un FlowDocument : texte des Run, marqueurs [image],
        /// gras/italique/souligné, titres (taille de police), listes et tableaux.
        /// </summary>
        private static ExtractionResult ExtractTextCore(string path)
        {
            FlowDocument document = LoadDocument(path);
            var state = new ExtractionState();
            foreach (var block in document.Blocks) AppendBlock(block, state);
            return new ExtractionResult { Text = state.Text.ToString().Trim(), ImageCount = state.Images };
        }

        private sealed class ExtractionState
        {
            public readonly StringBuilder Text = new StringBuilder();
            public int Images;
        }

        private static void AppendBlock(Block block, ExtractionState state)
        {
            if (block is Paragraph paragraph) AppendParagraph(paragraph, state);
            else if (block is List list) AppendList(list, state);
            else if (block is Table table) AppendTable(table, state);
            else if (block is Section section)
            {
                foreach (var child in section.Blocks) AppendBlock(child, state);
            }
        }

        private static void AppendParagraph(Paragraph paragraph, ExtractionState state)
        {
            int start = state.Text.Length;
            AppendInlines(paragraph.Inlines, state);
            string content = state.Text.ToString(start, state.Text.Length - start).Trim();
            state.Text.Length = start;
            if (content.Length == 0) return;

            double fontSize = paragraph.FontSize;
            if (fontSize >= Heading1FontSize) state.Text.Append("# ").Append(content);
            else if (fontSize >= Heading2FontSize) state.Text.Append("## ").Append(content);
            else state.Text.Append(content);
            state.Text.Append("\n\n");
        }

        private static void AppendInlines(InlineCollection inlines, ExtractionState state)
        {
            foreach (var inline in inlines)
            {
                // Ordre des cas important : LineBreak hérite de Span.
                if (inline is Run run) state.Text.Append(run.Text);
                else if (inline is LineBreak) state.Text.Append('\n');
                else if (inline is InlineUIContainer container)
                {
                    if (container.Child is Image)
                    {
                        state.Text.Append(" [image] ");
                        state.Images++;
                    }
                }
                else if (inline is Span span)
                {
                    int spanStart = state.Text.Length;
                    AppendInlines(span.Inlines, state);
                    string spanText = state.Text.ToString(spanStart, state.Text.Length - spanStart).Trim();
                    if (spanText.Length == 0) continue;

                    string marker = span is Bold ? "**" : span is Italic ? "*" : span is Underline ? "__" : null;
                    if (marker == null) continue;
                    state.Text.Length = spanStart;
                    state.Text.Append(marker).Append(spanText).Append(marker);
                }
            }
        }

        private static void AppendList(List list, ExtractionState state)
        {
            foreach (var item in list.ListItems)
            {
                var itemState = new ExtractionState();
                foreach (var block in item.Blocks) AppendBlock(block, itemState);
                string text = itemState.Text.ToString().Trim();
                if (text.Length == 0) continue;
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                state.Text.Append("- ").Append(string.Join("\n  ", lines)).Append('\n');
            }
            state.Text.Append('\n');
        }

        private static void AppendTable(Table table, ExtractionState state)
        {
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                var cells = row.Cells.Select(cell =>
                {
                    var cellState = new ExtractionState();
                    foreach (var block in cell.Blocks) AppendBlock(block, cellState);
                    return cellState.Text.ToString().Trim().Replace('\n', ' ');
                });
                state.Text.Append(string.Join(" | ", cells)).Append('\n');
            }
            state.Text.Append('\n');
        }

        /// <summary>
        /// Extraits contextuels autour de chaque occurrence de la requête
        /// (repli en une seule ligne pour rester compact dans le chat).
        /// </summary>
        private static List<string> FindSnippets(string text, string query, int max)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            string flattened = Regex.Replace(text, @"\s+", " ");
            string flatQuery = Regex.Replace(query, @"\s+", " ");

            var snippets = new List<string>();
            int index = 0;
            while (snippets.Count < max)
            {
                int found = flattened.IndexOf(flatQuery, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                int start = Math.Max(0, found - SnippetPadding);
                int length = Math.Min(flattened.Length - start, flatQuery.Length + SnippetPadding * 2);
                snippets.Add((start > 0 ? "…" : "") + flattened.Substring(start, length).Trim() + (start + length < flattened.Length ? "…" : ""));
                index = found + flatQuery.Length;
            }
            return snippets;
        }

        // =====================================================================
        // MARKDOWN LÉGER → FLOWDOCUMENT (écriture)
        // =====================================================================

        /// <summary>
        /// Convertit un markdown léger et l'insère dans un FlowDocument : « # »,
        /// « ## », « ### » en titres (tailles alignées sur les seuils de lecture),
        /// « - » / « 1. » en listes WPF, **gras**, *italique*, __souligné__. Les
        /// marqueurs [image] éventuels sont ignorés (l'agent ne produit pas d'images).
        /// Les blocs sont créés directement dans le document cible (jamais de
        /// déplacement d'un FlowDocument temporaire : WPF refuse de reparenter des
        /// blocs issus d'un autre arbre texte).
        /// </summary>
        private static void AppendMarkdownToDocument(FlowDocument document, string content, bool insertAtStart)
        {
            // Ancre figée au premier bloc ORIGINAL : en mode prepend, insérer avant
            // cette ancre conserve l'ordre du markdown (si l'on ré-évaluait FirstBlock
            // à chaque insertion, les blocs arriveraient en ordre inverse).
            Block anchor = insertAtStart ? document.Blocks.FirstBlock : null;

            List pendingList = null;
            string[] lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (string rawLine in lines)
            {
                string trimmed = StripImageMarkers(rawLine).Trim();

                bool isBullet = trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ");
                bool isNumbered = Regex.IsMatch(trimmed, @"^\d+[.)]\s");
                if (isBullet || isNumbered)
                {
                    if (pendingList == null)
                    {
                        pendingList = new List
                        {
                            MarkerStyle = isNumbered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(20, 0, 0, 0)
                        };
                        InsertBlock(document, pendingList, anchor);
                    }
                    string itemText = isBullet ? trimmed.Substring(2).Trim() : Regex.Replace(trimmed, @"^\d+[.)]\s", "").Trim();
                    var itemParagraph = new Paragraph();
                    AppendFormattedInlines(itemParagraph.Inlines, itemText);
                    pendingList.ListItems.Add(new ListItem(itemParagraph));
                    continue;
                }

                pendingList = null; // une ligne hors liste termine la liste en cours

                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("### ")) InsertBlock(document, Heading(trimmed.Substring(4).Trim(), 15), anchor);
                else if (trimmed.StartsWith("## ")) InsertBlock(document, Heading(trimmed.Substring(3).Trim(), 18), anchor);
                else if (trimmed.StartsWith("# ")) InsertBlock(document, Heading(trimmed.Substring(2).Trim(), 22), anchor);
                else
                {
                    var paragraph = new Paragraph();
                    AppendFormattedInlines(paragraph.Inlines, trimmed);
                    InsertBlock(document, paragraph, anchor);
                }
            }
        }

        /// <summary>
        /// Insère un bloc avant l'ancre (mode prepend) ou à la fin du document.
        /// </summary>
        private static void InsertBlock(FlowDocument document, Block block, Block anchor)
        {
            if (anchor != null) document.Blocks.InsertBefore(anchor, block);
            else document.Blocks.Add(block);
        }

        private static Paragraph Heading(string text, double size)
        {
            var paragraph = new Paragraph();
            AppendFormattedInlines(paragraph.Inlines, text);
            paragraph.FontSize = size;
            paragraph.FontWeight = System.Windows.FontWeights.Bold;
            paragraph.Margin = new Thickness(0, 6, 0, 4);
            return paragraph;
        }

        /// <summary>
        /// Tokenisation inline simple : **gras**, __souligné__, *italique*.
        /// Les segments hors marqueurs deviennent des Run simples.
        /// </summary>
        /// <summary>
        /// Convertit un texte en Inlines : gère les conteneurs [color=...]...[/color]
        /// et [size=...]...[/size] (Span), ainsi que la mise en forme simple
        /// **gras**, *italique*, __souligné__ à l'intérieur. Chaque Run reçoit un
        /// pinceau clair par défaut (fond sombre), sauf si un conteneur coloré
        /// impose sa propre couleur.
        /// </summary>
        private static void AppendFormattedInlines(InlineCollection inlines, string text, Brush defaultBrush = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            Brush fallback = defaultBrush ?? GetDefaultTextBrush();

            // Conteneurs couleur/taille (non imbriqués entre eux, mais la mise en
            // forme simple ** * __ peut apparaître à l'intérieur).
            var containerPattern = new Regex(
                @"\[color=(?<c>[^\]]+)\](?<ct>.*?)\[/color\]" +
                @"|\[size=(?<s>[^\]]+)\](?<st>.*?)\[/size\]",
                RegexOptions.Singleline);

            int index = 0;
            foreach (Match match in containerPattern.Matches(text))
            {
                if (match.Index > index)
                    ApplySimpleFormatting(inlines, text.Substring(index, match.Index - index), fallback);
                if (match.Groups["c"].Success)
                {
                    var span = new Span { Foreground = ParseColor(match.Groups["c"].Value) };
                    ApplySimpleFormatting(span.Inlines, match.Groups["ct"].Value, span.Foreground);
                    inlines.Add(span);
                }
                else if (match.Groups["s"].Success)
                {
                    var span = new Span { FontSize = ParseSize(match.Groups["s"].Value) };
                    ApplySimpleFormatting(span.Inlines, match.Groups["st"].Value, span.Foreground ?? fallback);
                    inlines.Add(span);
                }
                index = match.Index + match.Length;
            }
            if (index < text.Length)
                ApplySimpleFormatting(inlines, text.Substring(index), fallback);
        }

        /// <summary>
        /// Applique **gras**, *italique* et __souligné__ sur un segment. Chaque
        /// Run est créé avec le pinceau par défaut fourni (couleur claire ou
        /// couleur imposée par un conteneur parent).
        /// </summary>
        private static void ApplySimpleFormatting(InlineCollection inlines, string text, Brush defaultBrush)
        {
            if (string.IsNullOrEmpty(text)) return;
            var regex = new Regex(@"(\*\*(?<bold>.+?)\*\*)|(\*(?<italic>.+?)\*)|__(?<underline>.+?)__", RegexOptions.Compiled);
            int i = 0;
            foreach (Match match in regex.Matches(text))
            {
                if (match.Index > i) inlines.Add(MakeRun(text.Substring(i, match.Index - i), defaultBrush));
                if (match.Groups["bold"].Success) inlines.Add(new Bold(MakeRun(match.Groups["bold"].Value, defaultBrush)));
                else if (match.Groups["italic"].Success) inlines.Add(new Italic(MakeRun(match.Groups["italic"].Value, defaultBrush)));
                else if (match.Groups["underline"].Success) inlines.Add(new Underline(MakeRun(match.Groups["underline"].Value, defaultBrush)));
                i = match.Index + match.Length;
            }
            if (i < text.Length) inlines.Add(MakeRun(text.Substring(i), defaultBrush));
        }

        private static Run MakeRun(string text, Brush defaultBrush)
        {
            return new Run(text) { Foreground = defaultBrush };
        }

        /// <summary>
        /// Convertit un nom de couleur ou un code hex (#RRGGBB) en pinceau.
        /// Reconnait les noms courants ; retombe sur le pinceau clair par défaut
        /// si la valeur est invalide.
        /// </summary>
        private static SolidColorBrush ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return GetDefaultTextBrush();
            try
            {
                value = value.Trim();
                var color = (Color)ColorConverter.ConvertFromString(value);
                return new SolidColorBrush(color);
            }
            catch
            {
                return GetDefaultTextBrush();
            }
        }

        private static double ParseSize(string value)
        {
            if (double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return Math.Max(8, Math.Min(72, result));
            return 14;
        }

        private static string StripImageMarkers(string line)
        {
            return line.Replace("[image]", string.Empty).Replace("[image ]", string.Empty).Replace("[ image ]", string.Empty);
        }

        // =====================================================================
        // HELPERS (chemins, threads STA, arguments JSON)
        // =====================================================================

        private static IEnumerable<string> EnumerateStudyFiles()
        {
            var files = new List<string>();
            foreach (var root in RootFolders)
            {
                if (Directory.Exists(root)) files.AddRange(Directory.GetFiles(root, "*" + Extension, SearchOption.AllDirectories));
            }
            return files;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string NormalizeFolder(string path)
        {
            string directory = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(directory) ? string.Empty : directory.Replace('\\', '/');
        }

        /// <summary>
        /// Résout un nom d'étude (« ICT Basics » ou « etudes/SMC/ICT Basics ») vers
        /// un fichier .etude existant, avec garde-fous anti path traversal.
        /// </summary>
        private static string ResolvePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string cleaned = name.Trim().Replace('\\', '/').TrimStart('/');
            if (cleaned.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - Extension.Length);
            if (cleaned.Length == 0) return null;

            string[] parts = cleaned.Split('/');
            if (parts.Any(part => part == ".." || part.Length == 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                return null;

            // 1. Correspondance exacte du chemin relatif dans chaque racine.
            foreach (var root in RootFolders)
            {
                if (parts[0].Equals(root, StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
                {
                    string candidate = Path.Combine(root, string.Join("/", parts.Skip(1)) + Extension);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                else
                {
                    string candidate = Path.Combine(root, cleaned + Extension);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }

            // 2. Repli : recherche par titre partout (l'agent ne connaît pas toujours le sous-dossier).
            foreach (var root in RootFolders)
            {
                if (!Directory.Exists(root)) continue;
                string match = Directory.GetFiles(root, "*" + Extension, SearchOption.AllDirectories)
                    .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), parts.Last(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return Path.GetFullPath(match);
            }
            return null;
        }

        /// <summary>
        /// Thread STA dédié unique avec Dispatcher pour toutes les opérations sur
        /// les FlowDocument. WPF initialise ses caches globaux (polices, etc.) sur
        /// le PREMIER thread qui crée un objet WPF ; utiliser un thread STA unique
        /// et persistant pour l'ensemble des opérations document évite les erreurs
        /// d'affinité ("another thread owns this object") qui surviennent quand
        /// RunSta créerait un nouveau thread STA à chaque appel.
        /// </summary>
        private static readonly object StaInitLock = new object();
        private static Dispatcher _docDispatcher;
        private static bool _docDispatcherReady;

        private static void EnsureDocDispatcher()
        {
            if (_docDispatcherReady) return;
            lock (StaInitLock)
            {
                if (_docDispatcherReady) return;
                var ready = new ManualResetEventSlim(false);
                var thread = new Thread(() =>
                {
                    _docDispatcher = Dispatcher.CurrentDispatcher;
                    _docDispatcherReady = true;
                    ready.Set();
                    Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.Wait();
            }
        }

        /// <summary>
        /// Exécute une opération sur un FlowDocument sur le thread STA dédié et
        /// persistant. Le FlowDocument est créé, manipulé et sauvegardé sur ce
        /// même thread — jamais d'accès depuis un autre thread. Utilise
        /// Dispatcher.Invoke (synchrone) : fiable car appelé depuis un thread
        /// d'arrière-plan (agent IA) sans pompe de messages propre.
        /// </summary>
        private static T RunSta<T>(Func<T> action)
        {
            EnsureDocDispatcher();
            T result = default;
            Exception error = null;
            _docDispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
            }));
            if (error != null)
                throw new InvalidOperationException("Accès au document d'étude impossible : " + error.Message, error);
            return result;
        }

        private static string GetString(JsonElement arguments, string property, string fallback = "")
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString() ?? fallback;
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return fallback;
            }
        }

        private static double GetNumber(JsonElement arguments, string property, double fallback = 0)
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number: return value.GetDouble();
                case JsonValueKind.String:
                    string text = (value.GetString() ?? string.Empty).Trim().Replace(',', '.');
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
                default: return fallback;
            }
        }







    }
}
