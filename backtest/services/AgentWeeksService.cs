using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace backtest.Services
{
    /// <summary>
    /// Outils « Weeks » de l'agent IA : catalogue, lecture, recherche, calendrier
    /// mensuel, création, écriture et suppression des notes hebdomadaires du
    /// dashboard (fichiers Notes/Notes_yyyyMMdd.etude = XamlPackage WPF, la
    /// même arborescence que la zone « NOTES &amp; PLANNING » de MainWindow).
    ///
    /// Mise en forme par défaut alignée sur les notes existantes du build
    /// (bin/Debug/Notes) : police Segoe UI, corps 14 pt, texte clair #EEEEEE —
    /// identique au RichTextBox du dashboard (fond #0D141D). L'agent écrit un
    /// markdown léger (# titres, **gras**, *italique*, __souligné__, listes,
    /// [color=...] et [size=...]) converti en FlowDocument puis sauvegardé en
    /// XamlPackage ; les images déjà présentes dans une note sont préservées.
    ///
    /// Toutes les opérations document passent par le thread STA dédié partagé
    /// d'AgentStudiesService (cf. RunSta) : aucun objet WPF n'est créé sur le
    /// thread d'arrière-plan de l'agent.
    /// </summary>
    public static class AgentWeeksService
    {
        // Les notes weeks vivent dans le dossier Notes (cf. MainWindow.notesFolderPath),
        // un fichier par semaine : Notes_yyyyMMdd.etude où yyyyMMdd = le LUNDI de la semaine.
        private const string NotesFolder = "Notes";
        private const string Extension = ".etude";
        private const string FilePrefix = "Notes_";

        private const int DefaultReadChars = 8000;
        private const int MinReadChars = 500;
        private const int MaxReadChars = 24000;
        private const int DefaultSearchResults = 8;
        private const int MaxSearchResults = 25;

        // Mise en forme par défaut des notes (relevée dans le build : paragraphes
        // Segoe UI 14 pt #EEEEEE). Le RichTextBox du dashboard affiche exactement
        // ces valeurs, la note créée par l'agent est donc visuellement identique.
        private const string DefaultFontFamily = "Segoe UI";
        private const double DefaultFontSize = 14;
        private const double Heading1FontSize = 22;
        private const double Heading2FontSize = 18;
        private const double Heading3FontSize = 15;

        private static readonly CultureInfo FrenchCulture = new CultureInfo("fr-FR");

        // Pinceau clair par défaut (#EEEEEE) : créé paresseusement SUR le thread
        // STA dédié (jamais dans le static constructor, pour ne pas initialiser
        // WPF sur le mauvais thread) — même technique qu'AgentStudiesService.
        private static Brush _defaultTextBrush;
        private static Brush DefaultTextBrush
        {
            get
            {
                if (_defaultTextBrush == null)
                    _defaultTextBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
                return _defaultTextBrush;
            }
        }

        // =====================================================================
        // CATALOGUE
        // =====================================================================

        /// <summary>
        /// Catalogue compact des notes hebdomadaires : semaine, fichier, taille,
        /// dernière modification, semaine courante. Aucun fichier n'est lu —
        /// point d'entrée recommandé avant read/write/delete_week.
        /// </summary>
        public static string GetCatalog()
        {
            DateTime currentWeek = GetStartOfWeek(DateTime.Now);
            var entries = new List<object>();
            foreach (var path in EnumerateWeekFiles())
            {
                var info = new FileInfo(path);
                DateTime weekStart = WeekStartFromFileName(path);
                entries.Add(new
                {
                    week_start = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    name = Path.GetFileNameWithoutExtension(path),
                    path = NormalizePath(path),
                    size_kb = Math.Round(info.Length / 1024.0, 1),
                    modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    is_current_week = weekStart == currentWeek
                });
            }
            var payload = new
            {
                current_week = currentWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                current_week_file = Path.GetFileName(WeekFilePath(currentWeek)),
                count = entries.Count,
                weeks = entries
            };
            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// Résumé compact injecté dans le snapshot du workspace : semaine courante,
        /// présence d'une note pour cette semaine, nombre total de notes.
        /// </summary>
        public static object GetSnapshotSummary()
        {
            DateTime currentWeek = GetStartOfWeek(DateTime.Now);
            string currentFile = WeekFilePath(currentWeek);
            return new
            {
                current_week = currentWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                current_week_note_exists = File.Exists(currentFile),
                notes_count = EnumerateWeekFiles().Count()
            };
        }

        // =====================================================================
        // LECTURE / RECHERCHE / CALENDRIER
        // =====================================================================

        /// <summary>
        /// Lit le contenu textuel d'une note hebdomadaire, images exclues
        /// (marqueurs [image]), tronqué à max_chars pour le contexte du modèle.
        /// </summary>
        public static AiToolResult Read(JsonElement arguments)
        {
            if (!TryResolveWeek(AgentStudiesService.GetString(arguments, "week"), out DateTime weekStart, out string error))
                return AiToolResult.Error(error);

            string path = WeekFilePath(weekStart);
            if (!File.Exists(path))
                return AiToolResult.Error($"Aucune note pour la semaine du {weekStart:yyyy-MM-dd} " +
                    $"(fichier attendu : {NormalizePath(path)}). Appelle get_weeks_catalog, ou crée-la avec create_week.");

            int maxChars = (int)AgentStudiesService.GetNumber(arguments, "max_chars", DefaultReadChars);
            maxChars = Math.Max(MinReadChars, Math.Min(MaxReadChars, maxChars));

            AgentStudiesService.ExtractionResult extraction = AgentStudiesService.ExtractText(path);
            bool truncated = extraction.Text.Length > maxChars;
            string content = truncated ? extraction.Text.Substring(0, maxChars) + "\n\n[... contenu tronqué ...]" : extraction.Text;

            var payload = new
            {
                path = NormalizePath(path),
                week_start = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                is_current_week = weekStart == GetStartOfWeek(DateTime.Now),
                char_count = extraction.Text.Length,
                image_count = extraction.ImageCount,
                truncated,
                content
            };
            return AiToolResult.Success(JsonSerializer.Serialize(payload));
        }

        /// <summary>
        /// Recherche plein texte dans toutes les notes hebdomadaires (texte
        /// extrait, images ignorées) et renvoie jusqu'à max_results semaines.
        /// </summary>
        public static AiToolResult Search(JsonElement arguments)
        {
            string query = AgentStudiesService.GetString(arguments, "query");
            if (string.IsNullOrWhiteSpace(query))
                return AiToolResult.Error("Le texte à rechercher est obligatoire.");

            int maxResults = (int)AgentStudiesService.GetNumber(arguments, "max_results", DefaultSearchResults);
            maxResults = Math.Max(1, Math.Min(MaxSearchResults, maxResults));

            var results = new List<object>();
            foreach (var path in EnumerateWeekFiles())
            {
                AgentStudiesService.ExtractionResult extraction = AgentStudiesService.ExtractText(path);
                var snippets = AgentStudiesService.FindSnippets(extraction.Text, query, 3);
                if (snippets.Count == 0) continue;
                results.Add(new
                {
                    path = NormalizePath(path),
                    week_start = WeekStartFromFileName(path).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    name = Path.GetFileNameWithoutExtension(path),
                    matches = snippets
                });
                if (results.Count >= maxResults) break;
            }

            var payload = new { query, count = results.Count, results };
            return AiToolResult.Success(JsonSerializer.Serialize(payload));
        }

        /// <summary>
        /// Calendrier d'un mois : grille des semaines (lundi → dimanche), numéros
        /// de semaine ISO, jours (fr), semaines avec/sans note, semaine courante
        /// et jour courant. Point de départ de toute planification : l'agent y
        /// lit les bornes de chaque semaine et l'état des notes, puis complète
        /// avec la recherche web (news, calendrier économique) avant d'écrire.
        /// </summary>
        public static AiToolResult GetMonthCalendar(JsonElement arguments)
        {
            DateTime today = DateTime.Now;
            int year = (int)AgentStudiesService.GetNumber(arguments, "year", today.Year);
            int month = (int)AgentStudiesService.GetNumber(arguments, "month", today.Month);
            if (year < 1 || year > 9999) year = today.Year;
            month = Math.Max(1, Math.Min(12, month));

            DateTime firstDay = new DateTime(year, month, 1);
            DateTime lastDay = firstDay.AddMonths(1).AddDays(-1);
            DateTime cursor = GetStartOfWeek(firstDay);

            var weeks = new List<object>();
            int notesInMonth = 0;
            for (; cursor <= lastDay; cursor = cursor.AddDays(7))
            {
                bool hasNote = File.Exists(WeekFilePath(cursor));
                if (hasNote) notesInMonth++;
                var days = new List<object>();
                for (int i = 0; i < 7; i++)
                {
                    DateTime day = cursor.AddDays(i);
                    days.Add(new
                    {
                        date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        weekday = day.ToString("dddd", FrenchCulture),
                        in_month = day.Month == month,
                        is_today = day.Date == today.Date
                    });
                }
                weeks.Add(new
                {
                    week_start = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    week_end = cursor.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    week_number = GetIsoWeekNumber(cursor),
                    note_file = Path.GetFileName(WeekFilePath(cursor)),
                    has_note = hasNote,
                    is_current_week = cursor == GetStartOfWeek(today),
                    days
                });
            }

            var payload = new
            {
                today = new
                {
                    date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    weekday = today.ToString("dddd", FrenchCulture),
                    week_start = GetStartOfWeek(today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                month,
                month_name = firstDay.ToString("MMMM", FrenchCulture),
                year,
                weeks_count = weeks.Count,
                notes_in_month = notesInMonth,
                weeks
            };
            return AiToolResult.Success(JsonSerializer.Serialize(payload));
        }

        // =====================================================================
        // ÉCRITURE
        // =====================================================================

        /// <summary>
        /// Crée la note hebdomadaire d'une semaine (alignée sur son lundi) avec
        /// un contenu initial markdown optionnel. Refuse d'écraser une note
        /// existante (write_week pour la compléter). Sans confirmation, comme
        /// create_study.
        /// </summary>
        public static AiToolResult Create(JsonElement arguments)
        {
            if (!TryResolveWeek(AgentStudiesService.GetString(arguments, "week"), out DateTime weekStart, out string error))
                return AiToolResult.Error(error);

            string path = WeekFilePath(weekStart);
            if (File.Exists(path))
                return AiToolResult.Error($"Une note existe déjà pour la semaine du {weekStart:yyyy-MM-dd}. " +
                    "Utilise write_week pour la modifier (replace / append / prepend).");

            string content = AgentStudiesService.GetString(arguments, "content");
            string fullPath = Path.GetFullPath(path);
            AgentStudiesService.RunSta<object>(() =>
            {
                Directory.CreateDirectory(NotesFolder);
                var document = NewWeekDocument();
                if (!string.IsNullOrWhiteSpace(content))
                    AppendWeekMarkdownToDocument(document, content, insertAtStart: false);
                AgentStudiesService.SaveDocument(document, path);
                return null;
            });

            RefreshUiIfCurrent(fullPath);
            return AiToolResult.Success($"Note de la semaine du {weekStart:yyyy-MM-dd} créée : {NormalizePath(path)}" +
                (string.IsNullOrWhiteSpace(content) ? " (vierge)." : " et remplie."));
        }

        /// <summary>
        /// Écrit du contenu markdown dans une note hebdomadaire : replace (défaut),
        /// append (à la fin) ou prepend (au début). Les images déjà présentes
        /// dans la note sont préservées (le document existant est chargé et
        /// complété, jamais reconstruit).
        /// </summary>
        public static AiToolResult Write(JsonElement arguments)
        {
            if (!TryResolveWeek(AgentStudiesService.GetString(arguments, "week"), out DateTime weekStart, out string error))
                return AiToolResult.Error(error);

            string content = AgentStudiesService.GetString(arguments, "content");
            string mode = AgentStudiesService.GetString(arguments, "mode", "replace").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(content))
                return AiToolResult.Error("Le contenu à écrire est obligatoire (vide = aucune modification).");
            if (mode != "replace" && mode != "append" && mode != "prepend")
                return AiToolResult.Error("Mode invalide. Valeurs attendues : replace, append ou prepend.");

            string path = WeekFilePath(weekStart);
            if (!File.Exists(path))
                return AiToolResult.Error($"Aucune note pour la semaine du {weekStart:yyyy-MM-dd}. Utilise create_week pour la créer.");

            AgentStudiesService.RunSta<object>(() =>
            {
                var document = AgentStudiesService.LoadDocument(path);

                // replace : on vide d'abord (l'ancre prepend vaudra null et les
                // blocs s'ajoutent simplement à la fin d'un document vide).
                if (mode == "replace") document.Blocks.Clear();
                AppendWeekMarkdownToDocument(document, content, insertAtStart: mode == "prepend");
                AgentStudiesService.SaveDocument(document, path);
                return null;
            });

            AgentStudiesService.InvalidateCache(Path.GetFullPath(path));
            RefreshUiIfCurrent(Path.GetFullPath(path));
            return AiToolResult.Success($"Note de la semaine du {weekStart:yyyy-MM-dd} mise à jour ({mode}).");
        }

        /// <summary>
        /// Supprime définitivement le fichier d'une note hebdomadaire.
        /// </summary>
        public static AiToolResult Delete(JsonElement arguments)
        {
            if (!TryResolveWeek(AgentStudiesService.GetString(arguments, "week"), out DateTime weekStart, out string error))
                return AiToolResult.Error(error);

            string path = WeekFilePath(weekStart);
            if (!File.Exists(path))
                return AiToolResult.Error($"Aucune note pour la semaine du {weekStart:yyyy-MM-dd}.");

            string fullPath = Path.GetFullPath(path);
            File.Delete(fullPath);
            AgentStudiesService.InvalidateCache(fullPath);
            RefreshUiIfCurrent(fullPath);
            return AiToolResult.Success($"Note de la semaine du {weekStart:yyyy-MM-dd} supprimée.");
        }

        // =====================================================================
        // MARKDOWN LÉGER → FLOWDOCUMENT (format weeks)
        // =====================================================================

        /// <summary>
        /// Document vierge aux couleurs des notes weeks : Segoe UI, corps 14,
        /// texte clair #EEEEEE (fond sombre du dashboard).
        /// </summary>
        private static FlowDocument NewWeekDocument()
        {
            return new FlowDocument
            {
                FontFamily = new FontFamily(DefaultFontFamily),
                FontSize = DefaultFontSize,
                Foreground = DefaultTextBrush
            };
        }

        /// <summary>
        /// Convertit un markdown léger et l'insère dans le FlowDocument d'une
        /// note : « # », « ## », « ### » en titres blancs en gras, « - » / « 1. »
        /// en listes WPF, **gras**, *italique*, __souligné__, conteneurs
        /// [color=...] / [size=...]. Les marqueurs [image] éventuels sont ignorés.
        /// Les blocs sont créés directement dans le document cible (jamais de
        /// reparentage entre arbres texte, refusé par WPF).
        /// </summary>
        private static void AppendWeekMarkdownToDocument(FlowDocument document, string content, bool insertAtStart)
        {
            // Ancre figée au premier bloc ORIGINAL : en mode prepend, insérer
            // avant cette ancre conserve l'ordre du markdown.
            Block anchor = insertAtStart ? document.Blocks.FirstBlock : null;

            List pendingList = null;
            string[] lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (string rawLine in lines)
            {
                string trimmed = AgentStudiesService.StripImageMarkers(rawLine).Trim();

                bool isBullet = trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• ");
                bool isNumbered = System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+[.)]\s");
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
                    string itemText = isBullet ? trimmed.Substring(2).Trim() : System.Text.RegularExpressions.Regex.Replace(trimmed, @"^\d+[.)]\s", "").Trim();
                    var itemParagraph = NewWeekParagraph();
                    AgentStudiesService.AppendFormattedInlines(itemParagraph.Inlines, itemText, DefaultTextBrush);
                    pendingList.ListItems.Add(new ListItem(itemParagraph));
                    continue;
                }

                pendingList = null; // une ligne hors liste termine la liste en cours

                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("### ")) InsertBlock(document, WeekHeading(trimmed.Substring(4).Trim(), Heading3FontSize), anchor);
                else if (trimmed.StartsWith("## ")) InsertBlock(document, WeekHeading(trimmed.Substring(3).Trim(), Heading2FontSize), anchor);
                else if (trimmed.StartsWith("# ")) InsertBlock(document, WeekHeading(trimmed.Substring(2).Trim(), Heading1FontSize), anchor);
                else
                {
                    var paragraph = NewWeekParagraph();
                    AgentStudiesService.AppendFormattedInlines(paragraph.Inlines, trimmed, DefaultTextBrush);
                    InsertBlock(document, paragraph, anchor);
                }
            }
        }

        private static Paragraph NewWeekParagraph()
        {
            return new Paragraph
            {
                FontFamily = new FontFamily(DefaultFontFamily),
                FontSize = DefaultFontSize,
                Foreground = DefaultTextBrush,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static Paragraph WeekHeading(string text, double size)
        {
            var paragraph = NewWeekParagraph();
            paragraph.Foreground = new SolidColorBrush(Colors.White);
            AgentStudiesService.AppendFormattedInlines(paragraph.Inlines, text, paragraph.Foreground);
            paragraph.FontSize = size;
            paragraph.FontWeight = FontWeights.Bold;
            paragraph.Margin = new Thickness(0, 8, 0, 5);
            return paragraph;
        }

        /// <summary>
        /// Insère un bloc avant l'ancre (mode prepend) ou à la fin du document.
        /// </summary>
        private static void InsertBlock(FlowDocument document, Block block, Block anchor)
        {
            if (anchor != null) document.Blocks.InsertBefore(anchor, block);
            else document.Blocks.Add(block);
        }

        // =====================================================================
        // HELPERS (semaines, chemins, rafraîchissement UI)
        // =====================================================================

        /// <summary>
        /// Début de semaine (lundi) — même convention que MainWindow :
        /// semaine basée sur le lundi, un dimanche appartient à la semaine
        /// qui vient de s'écouler.
        /// </summary>
        private static DateTime GetStartOfWeek(DateTime date)
        {
            int daysToSubtract = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
            if (date.DayOfWeek == DayOfWeek.Sunday) daysToSubtract = 6;
            return date.AddDays(-daysToSubtract).Date;
        }

        private static int GetIsoWeekNumber(DateTime date)
        {
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        /// <summary>
        /// Résout la référence d'une semaine donnée par le modèle ('current',
        /// '2026-09-07', '20260907', 'Notes_20260907'...) vers le LUNDI de la
        /// semaine, avec message d'erreur explicite en cas d'échec.
        /// </summary>
        private static bool TryResolveWeek(string input, out DateTime weekStart, out string error)
        {
            weekStart = DateTime.MinValue;
            error = null;

            string cleaned = (input ?? string.Empty).Trim();
            if (cleaned.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - Extension.Length);
            if (cleaned.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(FilePrefix.Length);

            if (cleaned.Length == 0 ||
                cleaned.Equals("current", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("now", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("today", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("this week", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("semaine courante", StringComparison.OrdinalIgnoreCase))
            {
                weekStart = GetStartOfWeek(DateTime.Now);
                return true;
            }

            string[] formats = { "yyyy-MM-dd", "yyyyMMdd", "dd/MM/yyyy", "dd-MM-yyyy", "yyyy/MM/dd" };
            if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                weekStart = GetStartOfWeek(parsed);
                return true;
            }

            error = $"Semaine invalide : « {input} ». Formats acceptés : 'current', '2026-09-07', '20260907', '07/09/2026' " +
                "(toute date est alignée sur le lundi de sa semaine).";
            return false;
        }

        private static string WeekFilePath(DateTime weekStart)
        {
            return Path.Combine(NotesFolder, FilePrefix + weekStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);
        }

        private static DateTime WeekStartFromFileName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name != null && name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) && name.Length == FilePrefix.Length + 8)
            {
                if (DateTime.TryParseExact(name.Substring(FilePrefix.Length), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                    return parsed;
            }
            return DateTime.MinValue;
        }

        private static IEnumerable<string> EnumerateWeekFiles()
        {
            if (!Directory.Exists(NotesFolder)) return Enumerable.Empty<string>();
            return Directory.GetFiles(NotesFolder, "*" + Extension, SearchOption.TopDirectoryOnly)
                .Where(file => Path.GetFileName(file).StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// Si la note créée/écrite/supprimée correspond à la semaine affichée
        /// dans le dashboard, recharge le RichTextBox : l'utilisateur voit la
        /// mise à jour de l'agent sans changer de semaine. Appelé sur le thread
        /// UI via Dispatcher (l'agent tourne en arrière-plan).
        /// </summary>
        private static void RefreshUiIfCurrent(string absolutePath)
        {
            try
            {
                var application = Application.Current;
                var window = application?.MainWindow as MainWindow;
                if (window == null) return;
                application.Dispatcher.Invoke(new Action(() => window.RefreshWeekNotesIfCurrent(absolutePath)));
            }
            catch
            {
                // Le rafraîchissement est un confort : jamais bloquant pour l'agent.
            }
        }
    }
}
