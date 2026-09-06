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

        // Couleur claire par défaut pour tout texte créé par l'agent : alignée
        // sur les notes weeks (#EEEEEE, cf. AgentWeeksService) et sur les notes
        // existantes du build. Le fond du RichTextBox d'étude étant sombre
        // (#0A0A12), un texte noir serait illisible. Le pinceau est créé
        // paresseusement sur le thread STA dédié (jamais dans le static
        // constructor, pour ne pas initialiser WPF sur le mauvais thread).
        private static SolidColorBrush _defaultTextBrush;

        private static SolidColorBrush GetDefaultTextBrush()
        {
            if (_defaultTextBrush == null)
                _defaultTextBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
            return _defaultTextBrush;
        }

        // Mise en forme par défaut du contenu créé par l'agent : identique aux
        // notes weeks et aux notes du build (Segoe UI, corps 14, texte clair).
        private const string DefaultFontFamily = "Segoe UI";
        private const double DefaultFontSize = 14;

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
                // Document aux couleurs du module : Segoe UI 14, texte clair
                // #EEEEEE (identique aux notes weeks et aux notes du build).
                var document = new FlowDocument
                {
                    FontFamily = new FontFamily(DefaultFontFamily),
                    FontSize = DefaultFontSize,
                    Foreground = GetDefaultTextBrush()
                };
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

        // internal : partagé avec AgentWeeksService (notes hebdo).
        internal sealed class ExtractionResult
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
        // internal : partagé avec AgentWeeksService (notes hebdo).
        internal static ExtractionResult ExtractText(string path)
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

        internal static void InvalidateCache(string path)
        {
            lock (CacheLock) TextCache.Remove(path);
        }

        /// <summary>
        /// Charge un .etude (XamlPackage) dans un FlowDocument.
        /// </summary>
        internal static FlowDocument LoadDocument(string path)
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
        internal static void SaveDocument(FlowDocument document, string filePath)
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

        private static void AppendInlines(InlineCollection inlines, ExtractionState state, bool insideSpan = false)
        {
            foreach (var inline in inlines)
            {
                // Ordre des cas important : LineBreak hérite de Span.
                if (inline is Run run)
                {
                    // Le XamlPackage normalise parfois Bold/Italic/Underline en
                    // Run générique portant la propriété : on restitue alors le
                    // marqueur markdown (sauf Run imbriqué dans un Span, qui
                    // est déjà couvert par le marqueur du Span parent).
                    string runMarker = insideSpan ? null : GetFormatMarker(run);
                    if (runMarker == null) state.Text.Append(run.Text);
                    else state.Text.Append(runMarker).Append(run.Text).Append(runMarker);
                }
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
                    AppendInlines(span.Inlines, state, insideSpan: true);
                    string spanText = state.Text.ToString(spanStart, state.Text.Length - spanStart).Trim();
                    if (spanText.Length == 0) continue;

                    string marker = GetFormatMarker(span);
                    if (marker == null) continue;
                    state.Text.Length = spanStart;
                    state.Text.Append(marker).Append(spanText).Append(marker);
                }
            }
        }

        /// <summary>
        /// Marqueur markdown d'un inline mis en forme : l'élément typé WPF
        /// (Bold/Italic/Underline) ou, après aller-retour XamlPackage, un
        /// Span/Run générique portant la propriété localement (l'héritage est
        /// exclu via ReadLocalValue pour ne pas marquer deux fois un Run déjà
        /// couvert par son Span parent).
        /// </summary>
        private static string GetFormatMarker(Inline inline)
        {
            if (inline is Bold) return "**";
            if (inline is Italic) return "*";
            if (inline is Underline) return "__";
            if (IsLocallySet(inline, TextElement.FontWeightProperty) && inline.FontWeight == FontWeights.Bold) return "**";
            if (IsLocallySet(inline, TextElement.FontStyleProperty) && inline.FontStyle == FontStyles.Italic) return "*";
            if (IsLocallySet(inline, Inline.TextDecorationsProperty) && inline.TextDecorations != null && inline.TextDecorations.Count > 0) return "__";
            return null;
        }

        private static bool IsLocallySet(DependencyObject element, DependencyProperty property)
        {
            return element.ReadLocalValue(property) != DependencyProperty.UnsetValue;
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
        internal static List<string> FindSnippets(string text, string query, int max)
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
                    var itemParagraph = NewStudyParagraph();
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
                    var paragraph = NewStudyParagraph();
                    AppendFormattedInlines(paragraph.Inlines, trimmed);
                    InsertBlock(document, paragraph, anchor);
                }
            }
        }

        /// <summary>
        /// Paragraphe de base du contenu créé par l'agent : Segoe UI, corps 14,
        /// texte clair #EEEEEE — la même mise en forme que les notes weeks
        /// (AgentWeeksService) et les notes du build, au lieu des valeurs par
        /// défaut WPF (12 pt) qui rendaient le texte généré illisible/petit.
        /// </summary>
        private static Paragraph NewStudyParagraph()
        {
            return new Paragraph
            {
                FontFamily = new FontFamily(DefaultFontFamily),
                FontSize = DefaultFontSize,
                Foreground = GetDefaultTextBrush(),
                Margin = new Thickness(0, 0, 0, 6)
            };
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
            var paragraph = NewStudyParagraph();
            // Titres en blanc (comme les notes weeks) pour se détacher du corps.
            paragraph.Foreground = new SolidColorBrush(Colors.White);
            AppendFormattedInlines(paragraph.Inlines, text);
            paragraph.FontSize = size;
            paragraph.FontWeight = System.Windows.FontWeights.Bold;
            paragraph.Margin = new Thickness(0, 8, 0, 5);
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
        internal static void AppendFormattedInlines(InlineCollection inlines, string text, Brush defaultBrush = null)
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
        /// Dictionnaire manuel des noms de couleurs nommées (HTML / CSS / WPF).
        /// Utilisé en PRIORITÉ devant ColorConverter pour garantir que des noms
        /// comme "gold", "coral", "indianred" fonctionnent toujours, même si le
        /// convertisseur système échoue pour une raison quelconque.
        /// </summary>
        private static readonly Dictionary<string, Color> _namedColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            // Gris / Neutres
            ["black"] = Color.FromRgb(0, 0, 0),
            ["silver"] = Color.FromRgb(192, 192, 192),
            ["gray"] = Color.FromRgb(128, 128, 128),
            ["grey"] = Color.FromRgb(128, 128, 128),
            ["white"] = Color.FromRgb(255, 255, 255),
            ["dimgray"] = Color.FromRgb(105, 105, 105),
            ["dimgrey"] = Color.FromRgb(105, 105, 105),
            ["darkgray"] = Color.FromRgb(169, 169, 169),
            ["darkgrey"] = Color.FromRgb(169, 169, 169),
            ["lightgray"] = Color.FromRgb(211, 211, 211),
            ["lightgrey"] = Color.FromRgb(211, 211, 211),
            ["gainsboro"] = Color.FromRgb(220, 220, 220),
            ["slategray"] = Color.FromRgb(112, 128, 144),
            ["slategrey"] = Color.FromRgb(112, 128, 144),
            ["darkslategray"] = Color.FromRgb(47, 79, 79),
            ["darkslategrey"] = Color.FromRgb(47, 79, 79),
            ["lightslategray"] = Color.FromRgb(119, 136, 153),
            ["lightslategrey"] = Color.FromRgb(119, 136, 153),

            // Rouges / Roses
            ["red"] = Color.FromRgb(255, 0, 0),
            ["darkred"] = Color.FromRgb(139, 0, 0),
            ["firebrick"] = Color.FromRgb(178, 34, 34),
            ["crimson"] = Color.FromRgb(220, 20, 60),
            ["indianred"] = Color.FromRgb(205, 92, 92),
            ["lightcoral"] = Color.FromRgb(240, 128, 128),
            ["salmon"] = Color.FromRgb(250, 128, 114),
            ["darksalmon"] = Color.FromRgb(233, 150, 122),
            ["lightsalmon"] = Color.FromRgb(255, 160, 122),
            ["pink"] = Color.FromRgb(255, 192, 203),
            ["lightpink"] = Color.FromRgb(255, 182, 193),
            ["hotpink"] = Color.FromRgb(255, 105, 180),
            ["deeppink"] = Color.FromRgb(255, 20, 147),
            ["palevioletred"] = Color.FromRgb(219, 112, 147),
            ["mediumvioletred"] = Color.FromRgb(199, 21, 133),

            // Oranges / Jaunes / Beiges
            ["gold"] = Color.FromRgb(255, 215, 0),
            ["orange"] = Color.FromRgb(255, 165, 0),
            ["darkorange"] = Color.FromRgb(255, 140, 0),
            ["orangered"] = Color.FromRgb(255, 69, 0),
            ["coral"] = Color.FromRgb(255, 127, 80),
            ["tomato"] = Color.FromRgb(255, 99, 71),
            ["yellow"] = Color.FromRgb(255, 255, 0),
            ["lightyellow"] = Color.FromRgb(255, 255, 224),
            ["lemonchiffon"] = Color.FromRgb(255, 250, 205),
            ["lightgoldenrodyellow"] = Color.FromRgb(250, 250, 210),
            ["papayawhip"] = Color.FromRgb(255, 239, 213),
            ["moccasin"] = Color.FromRgb(255, 228, 181),
            ["peachpuff"] = Color.FromRgb(255, 218, 185),
            ["palegoldenrod"] = Color.FromRgb(238, 232, 170),
            ["khaki"] = Color.FromRgb(240, 230, 140),
            ["darkkhaki"] = Color.FromRgb(189, 183, 107),
            ["goldenrod"] = Color.FromRgb(218, 165, 32),
            ["darkgoldenrod"] = Color.FromRgb(184, 134, 11),
            ["peru"] = Color.FromRgb(205, 133, 63),
            ["burlywood"] = Color.FromRgb(222, 184, 135),
            ["tan"] = Color.FromRgb(210, 180, 140),
            ["wheat"] = Color.FromRgb(245, 222, 179),
            ["sandybrown"] = Color.FromRgb(244, 164, 96),
            ["bisque"] = Color.FromRgb(255, 228, 196),
            ["blanchedalmond"] = Color.FromRgb(255, 235, 205),
            ["cornsilk"] = Color.FromRgb(255, 248, 220),

            // Bruns
            ["brown"] = Color.FromRgb(165, 42, 42),
            ["saddlebrown"] = Color.FromRgb(139, 69, 19),
            ["sienna"] = Color.FromRgb(160, 82, 45),
            ["chocolate"] = Color.FromRgb(210, 105, 30),
            ["maroon"] = Color.FromRgb(128, 0, 0),
            ["rosybrown"] = Color.FromRgb(188, 143, 143),

            // Verts
            ["green"] = Color.FromRgb(0, 128, 0),
            ["darkgreen"] = Color.FromRgb(0, 100, 0),
            ["forestgreen"] = Color.FromRgb(34, 139, 34),
            ["seagreen"] = Color.FromRgb(46, 139, 87),
            ["mediumseagreen"] = Color.FromRgb(60, 179, 113),
            ["lime"] = Color.FromRgb(0, 255, 0),
            ["limegreen"] = Color.FromRgb(50, 205, 50),
            ["lightgreen"] = Color.FromRgb(144, 238, 144),
            ["palegreen"] = Color.FromRgb(152, 251, 152),
            ["springgreen"] = Color.FromRgb(0, 255, 127),
            ["mediumspringgreen"] = Color.FromRgb(0, 250, 154),
            ["lawngreen"] = Color.FromRgb(124, 252, 0),
            ["chartreuse"] = Color.FromRgb(127, 255, 0),
            ["greenyellow"] = Color.FromRgb(173, 255, 47),
            ["yellowgreen"] = Color.FromRgb(154, 205, 50),
            ["olive"] = Color.FromRgb(128, 128, 0),
            ["olivedrab"] = Color.FromRgb(107, 142, 35),
            ["darkolivegreen"] = Color.FromRgb(85, 107, 47),
            ["teal"] = Color.FromRgb(0, 128, 128),
            ["darkcyan"] = Color.FromRgb(0, 139, 139),
            ["aquamarine"] = Color.FromRgb(127, 255, 212),
            ["mediumaquamarine"] = Color.FromRgb(102, 205, 170),
            ["lightseagreen"] = Color.FromRgb(32, 178, 170),
            ["darkseagreen"] = Color.FromRgb(143, 188, 143),
            ["honeydew"] = Color.FromRgb(240, 255, 240),

            // Bleus / Cyans
            ["blue"] = Color.FromRgb(0, 0, 255),
            ["darkblue"] = Color.FromRgb(0, 0, 139),
            ["mediumblue"] = Color.FromRgb(0, 0, 205),
            ["royalblue"] = Color.FromRgb(65, 105, 225),
            ["navy"] = Color.FromRgb(0, 0, 128),
            ["midnightblue"] = Color.FromRgb(25, 25, 112),
            ["dodgerblue"] = Color.FromRgb(30, 144, 255),
            ["deepskyblue"] = Color.FromRgb(0, 191, 255),
            ["lightskyblue"] = Color.FromRgb(135, 206, 250),
            ["skyblue"] = Color.FromRgb(135, 206, 235),
            ["lightblue"] = Color.FromRgb(173, 216, 230),
            ["powderblue"] = Color.FromRgb(176, 224, 230),
            ["paleturquoise"] = Color.FromRgb(175, 238, 238),
            ["lightcyan"] = Color.FromRgb(224, 255, 255),
            ["cyan"] = Color.FromRgb(0, 255, 255),
            ["aqua"] = Color.FromRgb(0, 255, 255),
            ["turquoise"] = Color.FromRgb(64, 224, 208),
            ["mediumturquoise"] = Color.FromRgb(72, 209, 204),
            ["darkturquoise"] = Color.FromRgb(0, 206, 209),
            ["cadetblue"] = Color.FromRgb(95, 158, 160),
            ["steelblue"] = Color.FromRgb(70, 130, 180),
            ["lightsteelblue"] = Color.FromRgb(176, 196, 222),
            ["slateblue"] = Color.FromRgb(106, 90, 205),
            ["mediumslateblue"] = Color.FromRgb(123, 104, 238),
            ["darkslateblue"] = Color.FromRgb(72, 61, 139),

            // Violets / Magentas / Pourpres
            ["purple"] = Color.FromRgb(128, 0, 128),
            ["darkmagenta"] = Color.FromRgb(139, 0, 139),
            ["magenta"] = Color.FromRgb(255, 0, 255),
            ["fuchsia"] = Color.FromRgb(255, 0, 255),
            ["mediumpurple"] = Color.FromRgb(147, 112, 219),
            ["blueviolet"] = Color.FromRgb(138, 43, 226),
            ["indigo"] = Color.FromRgb(75, 0, 130),
            ["darkviolet"] = Color.FromRgb(148, 0, 211),
            ["darkorchid"] = Color.FromRgb(153, 50, 204),
            ["mediumorchid"] = Color.FromRgb(186, 85, 211),
            ["orchid"] = Color.FromRgb(218, 112, 214),
            ["violet"] = Color.FromRgb(238, 130, 238),
            ["plum"] = Color.FromRgb(221, 160, 221),
            ["thistle"] = Color.FromRgb(216, 191, 216),
            ["lavender"] = Color.FromRgb(230, 230, 250),

            // Couleurs trading / spé
            ["win"] = Color.FromRgb(0, 230, 118),
            ["loss"] = Color.FromRgb(255, 82, 82),
            ["profit"] = Color.FromRgb(0, 230, 118),
            ["neutral"] = Color.FromRgb(255, 193, 7),
            ["warning"] = Color.FromRgb(255, 193, 7),
            ["info"] = Color.FromRgb(0, 191, 255),
            ["bullish"] = Color.FromRgb(38, 166, 154),
            ["bearish"] = Color.FromRgb(239, 83, 80),
        };

        /// <summary>
        /// Convertit un nom de couleur ou un code hex (#RRGGBB) en pinceau.
        /// Utilise d'abord le dictionnaire manuel des noms nommés (robuste,
        /// fonctionne toujours), puis tente ColorConverter pour les formats
        /// hex (#RRGGBB, #AARRGGBB) et les noms non listés. Retombe sur le
        /// pinceau clair par défaut (#EEEEEE) si tout échoue.
        /// </summary>
        private static SolidColorBrush ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return GetDefaultTextBrush();
            try
            {
                value = value.Trim().ToLowerInvariant();

                // 1. Dictionnaire manuel (couvre gold, silver, coral, etc.)
                if (_namedColors.TryGetValue(value, out Color named))
                    return new SolidColorBrush(named);

                // 2. Hex ou format WPF (#RRGGBB, #AARRGGBB, sc#, ContextColor)
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

        internal static string StripImageMarkers(string line)
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
        internal static T RunSta<T>(Func<T> action)
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

        internal static string GetString(JsonElement arguments, string property, string fallback = "")
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

        internal static double GetNumber(JsonElement arguments, string property, double fallback = 0)
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
