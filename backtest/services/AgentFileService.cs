using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using UglyToad.PdfPig;

namespace backtest.Services
{
    /// <summary>
    /// Tools IA « fichiers locaux » : permet à l'agent de lire le contenu de
    /// fichiers de la machine (txt, md, json, csv, xml, pdf, xlsx, docx) et de
    /// créer des documents (txt, json, md, pdf, docx) dans le dossier DataEdge
    /// de l'utilisateur (Documents\DataEdge).
    ///
    /// Lecture : TOUS les dossiers et fichiers du PC de l'utilisateur courant
    /// sont accessibles (Bureau, Documents, Téléchargements, Images, Vidéos,
    /// autres lecteurs C:\... et leurs sous-dossiers) dans la limite des
    /// permissions Windows de la session — un refus OS réel (dossier protégé,
    /// fichier verrouillé…) est remonté comme erreur au lieu d'un refus
    /// arbitraire du logiciel. Les écritures sont confinées à Documents\DataEdge.
    /// Les tools de mutation demandent la confirmation de l'utilisateur
    /// (RequiresConfirmation côté AgentWorkspaceService).
    ///
    /// Licences : PdfPig (Apache 2.0), ExcelDataReader (MIT), DocumentFormat.OpenXml (MIT),
    /// QuestPDF (Community — déjà utilisé pour les rapports de statistiques).
    /// </summary>
    public static class AgentFileService
    {
        // ------------------------------------------------------------------
        // Dossiers de référence
        // ------------------------------------------------------------------

        /// <summary>Dossier maître de l'agent : Documents\DataEdge</summary>
        public static string AgentRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataEdge");

        /// <summary>Fichiers texte/markdown/json créés par l'agent.</summary>
        public static string DocumentsRoot => Path.Combine(AgentRoot, "Documents");

        /// <summary>Rapports PDF (export statistiques + PDF créés par l'agent).</summary>
        public static string RapportsRoot => Path.Combine(AgentRoot, "Rapports");

        /// <summary>Imports : copies des fichiers joints depuis le chat.</summary>
        public static string ImportsRoot => Path.Combine(AgentRoot, "Imports");

        private const int DefaultReadChars = 8000;
        private const int MaxReadChars = 30000;
        private const int MaxListEntries = 250;
        private const long MaxSearchFileBytes = 2 * 1024 * 1024;

        private static readonly string[] TextExtensions =
            { ".txt", ".md", ".json", ".csv", ".xml", ".yaml", ".yml", ".log", ".ini", ".tsv" };

        // ------------------------------------------------------------------
        // Résolution / sécurisation des chemins
        // ------------------------------------------------------------------

        /// <summary>
        /// Résout un chemin passé par le modèle :
        /// - vide ou relatif → interprété depuis Documents\DataEdge ;
        /// - "~"/"~/…" → profil utilisateur ;
        /// - chemin absolu Windows → accepté tel quel : l'agent peut lire TOUS
        ///   les dossiers du PC (Bureau, Documents, Téléchargements, C:\...),
        ///   seules les permissions Windows de la session peuvent refuser.
        /// Retourne null si le chemin n'existe pas (mustExist) ou est invalide.
        /// </summary>
        private static string ResolvePath(string rawPath, bool mustExist = false)
        {
            try
            {
                string trimmed = (rawPath ?? string.Empty).Trim().Trim('"');
                if (trimmed.Length == 0)
                    return AgentRoot;
                if (trimmed.IndexOf('\0') >= 0)
                    return null;

                if (trimmed == "~")
                    trimmed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                else if (trimmed.StartsWith("~/", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("~\\", StringComparison.OrdinalIgnoreCase))
                    trimmed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), trimmed.Substring(2));
                else
                    trimmed = Environment.ExpandEnvironmentVariables(trimmed);

                string full;
                if (Path.IsPathRooted(trimmed))
                    full = Path.GetFullPath(trimmed);
                else if (trimmed.StartsWith("Documents\\", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Documents/", StringComparison.OrdinalIgnoreCase))
                    full = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), trimmed.Substring(10));
                else
                    full = Path.GetFullPath(Path.Combine(AgentRoot, trimmed));

                if (mustExist && !File.Exists(full) && !Directory.Exists(full))
                    return null;
                return full;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Résolution stricte : renvoie un AiToolResult.Error si le chemin est refusé.</summary>
        private static AiToolResult ResolveOrError(string rawPath, bool mustExist, out string resolved)
        {
            resolved = ResolvePath(rawPath, mustExist);
            if (resolved == null)
            {
                if (mustExist)
                    return AiToolResult.Error("Fichier ou dossier introuvable, ou accès réellement refusé par Windows (permissions/protection). Vérifie le chemin exact (list_local_folder sur le dossier parent) ou demande confirmation à l'utilisateur.");
                return AiToolResult.Error("Chemin invalide. Tu peux lire les dossiers du PC : indique un chemin absolu Windows (ex: C:\\Users\\<nom>\\Documents, ~\\Bureau) ou relatif à Documents\\DataEdge.");
            }
            return null;
        }

        /// <summary>
        /// Résout le sous-dossier cible des outils d'écriture : toujours relatif au
        /// dossier DataEdge (Documents\DataEdge). Vide → Documents\DataEdge\Documents.
        /// Retourne null si le chemin sort du dossier DataEdge.
        /// </summary>
        private static string ResolveWriteSubfolder(string rawSubfolder)
        {
            try
            {
                string trimmed = (rawSubfolder ?? string.Empty).Trim().Trim('"');
                if (trimmed.Length == 0)
                    return DocumentsRoot;

                string full;
                if (Path.IsPathRooted(trimmed))
                    full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(trimmed));
                else
                    full = Path.GetFullPath(Path.Combine(AgentRoot, trimmed));

                var normalizedRoot = Path.GetFullPath(AgentRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return null;
                return full;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var chars = input.Trim();
            foreach (var ch in Path.GetInvalidFileNameChars())
                chars = chars.Replace(ch.ToString(), "_");
            chars = chars.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
            return string.IsNullOrWhiteSpace(chars) ? null : chars;
        }

        private static string DescribeSize(long bytes)
        {
            if (bytes < 1024) return bytes + " o";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " Ko";
            return (bytes / 1024.0 / 1024.0).ToString("0.##") + " Mo";
        }

        /// <summary>Trouve un nom de fichier libre dans le dossier cible (suffixe _2, _3…).</summary>
        internal static string GetAvailablePath(string directory, string fileName, string extension)
        {
            Directory.CreateDirectory(directory);
            string candidate = Path.Combine(directory, fileName + extension);
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, fileName + "_" + index + extension);
                index++;
            }
            return candidate;
        }

        // ------------------------------------------------------------------
        // Helpers d'arguments (mêmes conventions que AgentWorkspaceService)
        // ------------------------------------------------------------------

        private static string GetString(JsonElement arguments, string property)
        {
            if (!arguments.TryGetProperty(property, out var value)) return string.Empty;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString() ?? string.Empty;
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return string.Empty;
            }
        }

        private static bool GetBool(JsonElement arguments, string property, bool fallback = false)
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return value.GetDouble() != 0;
                case JsonValueKind.String:
                    string text = value.GetString();
                    if (bool.TryParse(text, out bool parsed)) return parsed;
                    if (text == "1" || text.Equals("oui", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
                    if (text == "0" || text.Equals("non", StringComparison.OrdinalIgnoreCase) || text.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
                    return fallback;
                default: return fallback;
            }
        }

        private static int GetInt(JsonElement arguments, string property, int fallback, int min, int max)
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            double number;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number: number = value.GetDouble(); break;
                case JsonValueKind.String:
                    if (!double.TryParse((value.GetString() ?? string.Empty).Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out number)) return fallback;
                    break;
                default: return fallback;
            }
            var clamped = (int)Math.Round(number);
            return Math.Max(min, Math.Min(max, clamped));
        }

        // ==================================================================
        // TOOL : list_local_folder
        // ==================================================================

        public static AiToolResult ListFolder(JsonElement arguments)
        {
            var guard = ResolveOrError(GetString(arguments, "path"), false, out string directory);
            if (guard != null) return guard;

            if (!Directory.Exists(directory))
                return AiToolResult.Error("Ce chemin est un fichier, pas un dossier. Utilise read_local_file pour lire son contenu.");

            bool recursive = GetBool(arguments, "recursive", false);
            var folders = new List<object>();
            var files = new List<object>();
            int totalFiles = 0, totalFolders = 0, skipped = 0;

            try
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var di = new DirectoryInfo(directory);

                foreach (var dir in di.EnumerateDirectories("*", option))
                {
                    totalFolders++;
                    if (folders.Count < MaxListEntries)
                        folders.Add(new { name = dir.FullName, modified = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
                    else skipped++;
                }

                foreach (var file in di.EnumerateFiles("*", option))
                {
                    totalFiles++;
                    if (files.Count < MaxListEntries)
                        files.Add(new
                        {
                            path = file.FullName,
                            name = file.Name,
                            type = (file.Extension ?? string.Empty).TrimStart('.').ToLowerInvariant(),
                            size = DescribeSize(file.Length),
                            modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        });
                    else skipped++;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                return AiToolResult.Error("Accès refusé à ce dossier : " + ex.Message);
            }

            var result = new
            {
                folder = directory,
                subfolders = folders,
                files = files,
                counts = new { files = totalFiles, subfolders = totalFolders, shown = files.Count + folders.Count, omitted = skipped },
                hint = "Utilise read_local_file avec le chemin complet d'un fichier pour lire son contenu."
            };
            return AiToolResult.Success(JsonSerializer.Serialize(result));
        }

        // ==================================================================
        // TOOL : read_local_file
        // ==================================================================

        public static AiToolResult ReadFile(JsonElement arguments)
        {
            var guard = ResolveOrError(GetString(arguments, "path"), true, out string path);
            if (guard != null) return guard;

            int maxChars = GetInt(arguments, "max_chars", DefaultReadChars, 200, MaxReadChars);
            int offset = GetInt(arguments, "offset_chars", 0, 0, int.MaxValue);

            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                    return AiToolResult.Error("Ceci est un dossier. Utilise list_local_folder pour lister son contenu.");
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Impossible d'accéder au fichier : " + ex.Message);
            }

            string extension = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();

            if (TextExtensions.Contains(extension))
                return ReadTextFile(path, maxChars, offset);
            if (extension == ".pdf")
                return ReadPdfFile(path, maxChars, offset);
            if (extension == ".xlsx" || extension == ".xls")
                return ReadExcelFile(path, maxChars);
            if (extension == ".docx")
                return ReadDocxFile(path, maxChars, offset);
            if (extension == ".doc")
                return AiToolResult.Error("Les anciens fichiers Word (.doc) ne sont pas pris en charge. Demande à l'utilisateur de l'enregistrer au format .docx, .pdf ou .txt.");
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".gif" || extension == ".bmp")
                return AiToolResult.Error("Les images ne peuvent pas être lues sous forme de texte (outil conçu pour les documents).");

            return AiToolResult.Error("Type de fichier non pris en charge (" + extension + "). Formats lus : txt, md, json, csv, xml, yaml, log, pdf, xlsx, xls, docx.");
        }

        private static AiToolResult ReadTextFile(string path, int maxChars, int offset)
        {
            try
            {
                string content;
                using (var reader = new StreamReader(path, System.Text.Encoding.UTF8, true))
                {
                    content = reader.ReadToEnd();
                }
                return BuildReadResult(path, "texte", content, maxChars, offset);
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Lecture impossible : " + ex.Message);
            }
        }

        private static AiToolResult BuildReadResult(string path, string kind, string content, int maxChars, int offset)
        {
            int total = content.Length;
            if (total > 0 && offset >= total)
                return AiToolResult.Error("offset_chars (" + offset + ") dépasse la taille du contenu (" + total + " caractères).");

            int available = Math.Max(0, total - offset);
            int take = Math.Min(maxChars, available);
            string slice = content.Substring(offset, take);

            var result = new
            {
                path = path,
                type = kind,
                total_chars = total,
                offset_chars = offset,
                returned_chars = take,
                truncated = offset + take < total,
                content = slice
            };
            return AiToolResult.Success(JsonSerializer.Serialize(result));
        }

        private static AiToolResult ReadPdfFile(string path, int maxChars, int offset)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                using (var document = PdfDocument.Open(path))
                {
                    int pageCount = document.NumberOfPages;
                    sb.AppendLine("[PDF : " + Path.GetFileName(path) + " — " + pageCount + " page(s)]");
                    foreach (var page in document.GetPages())
                    {
                        sb.AppendLine("--- Page " + page.Number + " ---");
                        sb.AppendLine((page.Text ?? string.Empty).Trim());
                        if (sb.Length > offset + maxChars + 400000) break; // garde-fou mémoire sur les très gros PDF
                    }
                }
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Extraction du texte PDF impossible (PDF protégé, corrompu ou scanné sans couche texte ?) : " + ex.Message);
            }

            if (sb.Length < 30)
                return AiToolResult.Error("Aucun texte extractible dans ce PDF (probablement un scan d'images sans couche texte).");

            return BuildReadResult(path, "pdf", sb.ToString().Trim(), maxChars, offset);
        }

        private static AiToolResult ReadExcelFile(string path, int maxChars)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
                {
                    int sheetIndex = 0;
                    int budget = maxChars + 4000;
                    do
                    {
                        sheetIndex++;
                        sb.AppendLine("=== Feuille " + sheetIndex + " ===");
                        int rowCount = 0;
                        while (reader.Read() && sb.Length < budget)
                        {
                            rowCount++;
                            var cells = new string[reader.FieldCount];
                            for (int i = 0; i < reader.FieldCount; i++)
                                cells[i] = reader.GetValue(i)?.ToString() ?? string.Empty;
                            sb.AppendLine(string.Join(" | ", cells).TrimEnd());
                            if (rowCount >= 500)
                            {
                                sb.AppendLine("[… lignes suivantes omises (limite 500 par feuille) — redemande si besoin]");
                                break;
                            }
                        }
                    }
                    while (reader.NextResult() && sb.Length < budget);
                }
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Lecture Excel impossible : " + ex.Message);
            }

            return BuildReadResult(path, "excel", sb.ToString().Trim(), maxChars, 0);
        }

        private static AiToolResult ReadDocxFile(string path, int maxChars, int offset)
        {
            try
            {
                string content;
                using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false))
                {
                    var body = document.MainDocumentPart?.Document?.Body;
                    if (body == null)
                        return AiToolResult.Error("Document Word vide ou illisible.");

                    var sb = new System.Text.StringBuilder();
                    foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        string text = string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
                        if (!string.IsNullOrWhiteSpace(text))
                            sb.AppendLine(text.Trim());
                    }
                    content = sb.ToString().Trim();
                }
                return BuildReadResult(path, "word", content, maxChars, offset);
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Lecture Word (.docx) impossible : " + ex.Message);
            }
        }

        // ==================================================================
        // TOOL : search_in_files
        // ==================================================================

        public static AiToolResult SearchInFiles(JsonElement arguments)
        {
            string query = GetString(arguments, "query");
            if (string.IsNullOrWhiteSpace(query))
                return AiToolResult.Error("Paramètre 'query' obligatoire : texte à rechercher.");

            var guard = ResolveOrError(GetString(arguments, "folder"), false, out string folder);
            if (guard != null) return guard;
            if (!Directory.Exists(folder))
                return AiToolResult.Error("Dossier de recherche introuvable : " + folder);

            int maxResults = GetInt(arguments, "max_results", 10, 1, 30);
            var pattern = new System.Text.RegularExpressions.Regex(
                System.Text.RegularExpressions.Regex.Escape(query.Trim()),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var hits = new List<object>();
            int filesScanned = 0, totalMatches = 0;

            try
            {
                var searchOption = string.Equals(folder, AgentRoot, StringComparison.OrdinalIgnoreCase)
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.*", searchOption))
                {
                    if (!TextExtensions.Contains((file.Extension ?? string.Empty).ToLowerInvariant())) continue;
                    if (file.Length > MaxSearchFileBytes) continue;

                    string content;
                    try { content = File.ReadAllText(file.FullName); }
                    catch { continue; }
                    filesScanned++;

                    var matches = pattern.Matches(content);
                    if (matches.Count == 0) continue;
                    totalMatches += matches.Count;

                    var excerpts = new List<string>();
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (excerpts.Count >= 2) break;
                        int start = Math.Max(0, match.Index - 100);
                        int length = Math.Min(content.Length - start, match.Length + 220);
                        excerpts.Add((start > 0 ? "…" : "") + content.Substring(start, length).Replace("\r", " ").Replace("\n", " ") + (start + length < content.Length ? "…" : ""));
                    }

                    hits.Add(new { path = file.FullName, matches = matches.Count, excerpts = excerpts });
                    if (hits.Count >= maxResults) break;
                }
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Recherche impossible : " + ex.Message);
            }

            var result = new
            {
                query = query.Trim(),
                folder = folder,
                files_scanned = filesScanned,
                total_matches = totalMatches,
                results = hits
            };
            return AiToolResult.Success(JsonSerializer.Serialize(result));
        }

        // ==================================================================
        // TOOL : create_text_file
        // ==================================================================

        public static AiToolResult CreateTextFile(JsonElement arguments)
        {
            string rawName = GetString(arguments, "name");
            string name = SanitizeName(rawName);
            if (name == null)
                return AiToolResult.Error("Paramètre 'name' obligatoire : nom du fichier à créer.");

            string content = GetString(arguments, "content") ?? string.Empty;
            if (content.Length > 500000)
                return AiToolResult.Error("Contenu trop long (" + content.Length + " caractères, max 500 000).");

            string format = (GetString(arguments, "format") ?? "txt").Trim('.').ToLowerInvariant();
            if (format.Length == 0 || format.Length > 5) format = "txt";
            if (format != "txt" && format != "json" && format != "md")
                return AiToolResult.Error("Format non pris en charge : " + format + ". Formats disponibles : txt, json, md (utilise create_pdf_file ou create_word_file pour les documents mis en page).");

            string extension;
            if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                extension = Path.GetExtension(name).ToLowerInvariant();
                name = name.Substring(0, name.Length - extension.Length);
                string extensionFormat = extension.TrimStart('.');
                if (extensionFormat == "txt" || extensionFormat == "json" || extensionFormat == "md")
                    format = extensionFormat; // l'extension fournie prime
            }
            else extension = "." + format;

            if (format == "json")
            {
                try
                {
                    using (JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "null" : content)) { }
                }
                catch (JsonException ex)
                {
                    return AiToolResult.Error("Le contenu n'est pas un JSON valide : " + ex.Message);
                }
            }

            string directory = ResolveWriteSubfolder(GetString(arguments, "subfolder"));
            if (directory == null)
                return AiToolResult.Error("Sous-dossier refusé : les écritures sont limitées au dossier DataEdge (Documents\\DataEdge). Utilise un chemin relatif, ex: 'Documents\\Analyses'.");

            string cleanName = SanitizeName(name) ?? "fichier";
            string targetPath;
            try { targetPath = GetAvailablePath(directory, cleanName, extension); }
            catch (Exception ex) { return AiToolResult.Error("Création du dossier impossible : " + ex.Message); }

            try
            {
                File.WriteAllText(targetPath, content, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Écriture impossible : " + ex.Message);
            }

            var result = new
            {
                created = true,
                path = targetPath,
                size = DescribeSize(new FileInfo(targetPath).Length),
                message = "Fichier créé : " + targetPath
            };
            return AiToolResult.Success(JsonSerializer.Serialize(result));
        }

        // ==================================================================
        // TOOL : create_pdf_file
        // ==================================================================

        public static AiToolResult CreatePdfFile(JsonElement arguments)
        {
            string rawName = GetString(arguments, "name");
            string name = SanitizeName(rawName);
            if (name == null)
                return AiToolResult.Error("Paramètre 'name' obligatoire : nom du PDF à créer (sans extension).");

            if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            string content = GetString(arguments, "content") ?? string.Empty;
            if (content.Trim().Length == 0)
                return AiToolResult.Error("Le contenu du PDF est vide.");
            if (content.Length > 500000)
                return AiToolResult.Error("Contenu trop long (" + content.Length + " caractères, max 500 000).");

            string title = GetString(arguments, "title");
            if (string.IsNullOrWhiteSpace(title)) title = name;

            string directory = ResolveWriteSubfolder(GetString(arguments, "subfolder"));
            if (directory == null)
                return AiToolResult.Error("Sous-dossier refusé : les écritures sont limitées au dossier DataEdge (Documents\\DataEdge). Utilise un chemin relatif, ex: 'Documents\\Analyses'.");

            try
            {
                string targetPath = PdfExportService.ExportDocumentPdf(title, content, directory);
                var result = new
                {
                    created = true,
                    path = targetPath,
                    size = DescribeSize(new FileInfo(targetPath).Length),
                    message = "PDF créé : " + targetPath
                };
                return AiToolResult.Success(JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Génération du PDF impossible : " + ex.Message);
            }
        }

        // ==================================================================
        // TOOL : create_word_file
        // ==================================================================

        public static AiToolResult CreateWordFile(JsonElement arguments)
        {
            string rawName = GetString(arguments, "name");
            string name = SanitizeName(rawName);
            if (name == null)
                return AiToolResult.Error("Paramètre 'name' obligatoire : nom du document Word à créer (sans extension).");

            if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);

            string content = GetString(arguments, "content") ?? string.Empty;
            if (content.Trim().Length == 0)
                return AiToolResult.Error("Le contenu du document Word est vide.");
            if (content.Length > 500000)
                return AiToolResult.Error("Contenu trop long (" + content.Length + " caractères, max 500 000).");

            string directory = ResolveWriteSubfolder(GetString(arguments, "subfolder"));
            if (directory == null)
                return AiToolResult.Error("Sous-dossier refusé : les écritures sont limitées au dossier DataEdge (Documents\\DataEdge). Utilise un chemin relatif, ex: 'Documents\\Analyses'.");

            try
            {
                string targetPath = WordDocumentBuilder.CreateDocument(directory, name, title: null, markdown: content);
                var result = new
                {
                    created = true,
                    path = targetPath,
                    size = DescribeSize(new FileInfo(targetPath).Length),
                    message = "Document Word créé : " + targetPath
                };
                return AiToolResult.Success(JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Génération du document Word impossible : " + ex.Message);
            }
        }

        // ==================================================================
        // TOOL : open_folder
        // ==================================================================

        public static AiToolResult OpenFolder(JsonElement arguments)
        {
            var guard = ResolveOrError(GetString(arguments, "path"), false, out string path);
            if (guard != null) return guard;

            string directory = path;
            if (File.Exists(path)) directory = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return AiToolResult.Error("Dossier introuvable : " + directory);

            try
            {
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + directory + "\"",
                    UseShellExecute = true
                })) { }
            }
            catch (Exception ex)
            {
                return AiToolResult.Error("Ouverture de l'Explorateur impossible : " + ex.Message);
            }

            return AiToolResult.Success(JsonSerializer.Serialize(new { opened = true, folder = directory, message = "Dossier ouvert dans l'Explorateur Windows." }));
        }

        // ==================================================================
        // API utilisée par l'interface du chat (pièces jointes / bouton dossier)
        // ==================================================================

        /// <summary>
        /// Copie un fichier choisi par l'utilisateur dans Documents\DataEdge\Imports
        /// afin que l'agent puisse le lire avec read_local_file. Retourne false + message
        /// d'erreur en cas d'échec.
        /// </summary>
        public static bool SaveImportedFile(string sourcePath, out string importedPath, out string error)
        {
            importedPath = null;
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    error = "Fichier introuvable.";
                    return false;
                }

                string extension = (Path.GetExtension(sourcePath) ?? string.Empty).ToLowerInvariant();
                if (extension == ".doc")
                {
                    error = "Les anciens fichiers Word (.doc) ne sont pas pris en charge par l'agent. Enregistre le fichier en .docx, .pdf ou .txt.";
                    return false;
                }

                string baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath)) ?? "fichier";
                string targetPath = GetAvailablePath(ImportsRoot, baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), extension);
                File.Copy(sourcePath, targetPath);
                importedPath = targetPath;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Ouvre le dossier maître de l'agent (Documents\DataEdge) dans l'Explorateur.</summary>
        public static void OpenAgentFolder()
        {
            Directory.CreateDirectory(AgentRoot);
            using (Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + AgentRoot + "\"",
                UseShellExecute = true
            })) { }
        }
    }

    /// <summary>
    /// Générateur de documents Word (.docx) via DocumentFormat.OpenXml (licence MIT) :
    /// titres, paragraphes, listes à puces, citations, séparateurs — à partir du même
    /// markdown léger que le générateur PDF (AgentMarkdown).
    /// </summary>
    internal static class WordDocumentBuilder
    {
        public static string CreateDocument(string directory, string fileName, string title, string markdown)
        {
            string safeName = fileName;
            foreach (var ch in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(ch.ToString(), "_");

            string targetPath = AgentFileService.GetAvailablePath(directory, safeName, ".docx");
            var blocks = AgentMarkdown.Parse(markdown);

            using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                targetPath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body());
                var body = mainPart.Document.Body;

                AddStyles(mainPart);

                // Titre du document (Heading1, style défini dans AddStyles)
                body.Append(BuildParagraph(BuildBodyRuns(AgentMarkdown.ParseInline(string.IsNullOrWhiteSpace(title) ? safeName : title)), "Heading1"));

                foreach (var block in blocks)
                {
                    switch (block.Kind)
                    {
                        case "heading":
                            string style = block.Level == 1 ? "Heading2" : block.Level == 2 ? "Heading3" : "Heading4";
                            body.Append(BuildParagraph(BuildBodyRuns(AgentMarkdown.ParseInline(block.PlainText)), style));
                            break;
                        case "bullet":
                            body.Append(BuildParagraph(BuildBodyRuns(block.Spans), null, "•  ", indent: 360));
                            break;
                        case "quote":
                            body.Append(BuildParagraph(BuildBodyRuns(block.Spans), null, color: "595959", indent: 360, forceItalic: true));
                            break;
                        case "rule":
                            body.Append(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders(
                                        new DocumentFormat.OpenXml.Wordprocessing.BottomBorder { Val = DocumentFormat.OpenXml.Wordprocessing.BorderValues.Single, Size = 6U, Color = "BFBFBF" }),
                                    new DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines { After = "160" })));
                            break;
                        default:
                            body.Append(BuildParagraph(BuildBodyRuns(block.Spans), null));
                            break;
                    }
                }

                // Format A4 + marges 2 cm
                body.Append(new DocumentFormat.OpenXml.Wordprocessing.SectionProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = 11906U, Height = 16838U },
                    new DocumentFormat.OpenXml.Wordprocessing.PageMargin { Top = 1134, Right = 1134U, Bottom = 1134, Left = 1134U, Header = 708U, Footer = 708U, Gutter = 0U }));

                mainPart.Document.Save();
            }
            return targetPath;
        }

        private static DocumentFormat.OpenXml.Wordprocessing.Paragraph BuildParagraph(
            IEnumerable<DocumentFormat.OpenXml.Wordprocessing.Run> runs,
            string styleId,
            string bulletPrefix = null,
            int indent = 0,
            string color = null,
            bool forceItalic = false)
        {
            var paragraphProperties = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties();
            if (styleId != null)
                paragraphProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = styleId });
            if (indent > 0)
                paragraphProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.Indentation { Left = indent.ToString() });
            paragraphProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines { After = "120", Line = "276", LineRule = DocumentFormat.OpenXml.Wordprocessing.LineSpacingRuleValues.Auto });

            var paragraph = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
            paragraph.Append(paragraphProperties);
            if (bulletPrefix != null)
                paragraph.Append(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(bulletPrefix) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
            foreach (var run in runs)
                paragraph.Append(run);
            return paragraph;
        }

        private static List<DocumentFormat.OpenXml.Wordprocessing.Run> BuildBodyRuns(List<AgentMarkdown.MdSpan> spans)
        {
            var runs = new List<DocumentFormat.OpenXml.Wordprocessing.Run>();
            foreach (var span in spans)
            {
                var runProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties();
                if (span.Bold) runProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.Bold());
                if (span.Italic) runProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.Italic());
                if (span.Code)
                {
                    runProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
                    runProperties.Append(new DocumentFormat.OpenXml.Wordprocessing.Color { Val = "C7254E" });
                }
                var run = new DocumentFormat.OpenXml.Wordprocessing.Run();
                if (runProperties.HasChildren) run.Append(runProperties);
                run.Append(new DocumentFormat.OpenXml.Wordprocessing.Text(span.Text ?? string.Empty) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                runs.Add(run);
            }
            if (runs.Count == 0)
                runs.Add(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(string.Empty)));
            return runs;
        }

        private static void AddStyles(DocumentFormat.OpenXml.Packaging.MainDocumentPart mainPart)
        {
            var styles = new DocumentFormat.OpenXml.Wordprocessing.Styles();

            var normal = new DocumentFormat.OpenXml.Wordprocessing.Style { Type = DocumentFormat.OpenXml.Wordprocessing.StyleValues.Paragraph, StyleId = "Normal", Default = true };
            normal.Append(new DocumentFormat.OpenXml.Wordprocessing.StyleName { Val = "Normal" });
            normal.Append(new DocumentFormat.OpenXml.Wordprocessing.PrimaryStyle());
            normal.Append(new DocumentFormat.OpenXml.Wordprocessing.StyleRunProperties(
                new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI" },
                new DocumentFormat.OpenXml.Wordprocessing.Color { Val = "1A1A1A" },
                new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "22" },
                new DocumentFormat.OpenXml.Wordprocessing.FontSizeComplexScript { Val = "22" }));
            styles.Append(normal);

            AddHeadingStyle(styles, "Heading1", "Titre 1", "0F5C8C", "40");
            AddHeadingStyle(styles, "Heading2", "Titre 2", "0F5C8C", "32");
            AddHeadingStyle(styles, "Heading3", "Titre 3", "2E74B5", "28");
            AddHeadingStyle(styles, "Heading4", "Titre 4", "44546A", "24");

            var stylesPart = mainPart.AddNewPart<DocumentFormat.OpenXml.Packaging.StyleDefinitionsPart>();
            stylesPart.Styles = styles;
            stylesPart.Styles.Save();
        }

        private static void AddHeadingStyle(DocumentFormat.OpenXml.Wordprocessing.Styles styles, string id, string name, string color, string sizeHalfPoints)
        {
            var style = new DocumentFormat.OpenXml.Wordprocessing.Style { Type = DocumentFormat.OpenXml.Wordprocessing.StyleValues.Paragraph, StyleId = id };
            style.Append(new DocumentFormat.OpenXml.Wordprocessing.StyleName { Val = name });
            style.Append(new DocumentFormat.OpenXml.Wordprocessing.BasedOn { Val = "Normal" });
            style.Append(new DocumentFormat.OpenXml.Wordprocessing.PrimaryStyle());
            style.Append(new DocumentFormat.OpenXml.Wordprocessing.StyleParagraphProperties(
                new DocumentFormat.OpenXml.Wordprocessing.KeepNext(),
                new DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines { Before = "240", After = "80" }));
            style.Append(new DocumentFormat.OpenXml.Wordprocessing.StyleRunProperties(
                new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                new DocumentFormat.OpenXml.Wordprocessing.Color { Val = color },
                new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = sizeHalfPoints },
                new DocumentFormat.OpenXml.Wordprocessing.FontSizeComplexScript { Val = sizeHalfPoints }));
            styles.Append(style);
        }
    }

    /// <summary>
    /// Parseur markdown léger partagé par les générateurs PDF (QuestPDF) et Word
    /// (OpenXML). Support : #/##/### titres, **gras**, *italique* et _italique_,
    /// `code`, - listes à puces, > citations, --- séparateurs. Les tableaux markdown
    /// sont rendus comme du texte brut.
    /// </summary>
    internal static class AgentMarkdown
    {
        public sealed class MdSpan
        {
            public string Text;
            public bool Bold;
            public bool Italic;
            public bool Code;
            public MdSpan(string text, bool bold = false, bool italic = false, bool code = false)
            {
                Text = text; Bold = bold; Italic = italic; Code = code;
            }
        }

        public sealed class MdBlock
        {
            public string Kind; // "heading", "paragraph", "bullet", "quote", "rule"
            public int Level;
            public List<MdSpan> Spans = new List<MdSpan>();
            public string PlainText => string.Concat(Spans.Select(s => s.Text));
        }

        public static List<MdBlock> Parse(string markdown)
        {
            var blocks = new List<MdBlock>();
            if (string.IsNullOrEmpty(markdown)) return blocks;

            var paragraphLines = new List<string>();
            foreach (var rawLine in markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) { FlushParagraph(blocks, paragraphLines); continue; }
                if (line == "---" || line == "***" || line == "___") { FlushParagraph(blocks, paragraphLines); blocks.Add(new MdBlock { Kind = "rule" }); continue; }

                int level = HeadingLevel(line);
                if (level > 0)
                {
                    FlushParagraph(blocks, paragraphLines);
                    blocks.Add(new MdBlock { Kind = "heading", Level = level, Spans = ParseInline(line.Substring(level).Trim()) });
                    continue;
                }
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ") || line.StartsWith("• "))
                {
                    FlushParagraph(blocks, paragraphLines);
                    blocks.Add(new MdBlock { Kind = "bullet", Spans = ParseInline(line.Substring(2).Trim()) });
                    continue;
                }
                if (line.StartsWith("> "))
                {
                    FlushParagraph(blocks, paragraphLines);
                    blocks.Add(new MdBlock { Kind = "quote", Spans = ParseInline(line.Substring(2).Trim()) });
                    continue;
                }
                paragraphLines.Add(line);
            }
            FlushParagraph(blocks, paragraphLines);
            return blocks;
        }

        private static int HeadingLevel(string line)
        {
            int level = 0;
            while (level < line.Length && line[level] == '#') level++;
            if (level > 0 && level <= 4 && level < line.Length && line[level] == ' ')
                return Math.Min(level, 3);
            return 0;
        }

        private static void FlushParagraph(List<MdBlock> blocks, List<string> lines)
        {
            if (lines.Count == 0) return;
            blocks.Add(new MdBlock { Kind = "paragraph", Spans = ParseInline(string.Join(" ", lines)) });
            lines.Clear();
        }

        public static List<MdSpan> ParseInline(string text)
        {
            var spans = new List<MdSpan>();
            if (string.IsNullOrEmpty(text)) { spans.Add(new MdSpan("")); return spans; }

            var buffer = new System.Text.StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > 0)
                    {
                        Flush();
                        spans.Add(new MdSpan(text.Substring(i + 2, end - i - 2), bold: true));
                        i = end + 2;
                        continue;
                    }
                }
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > 0)
                    {
                        Flush();
                        spans.Add(new MdSpan(text.Substring(i + 1, end - i - 1), code: true));
                        i = end + 1;
                        continue;
                    }
                }
                if (text[i] == '*' || text[i] == '_')
                {
                    char marker = text[i];
                    int end = text.IndexOf(marker, i + 1);
                    if (end > i + 1)
                    {
                        Flush();
                        spans.Add(new MdSpan(text.Substring(i + 1, end - i - 1), italic: true));
                        i = end + 1;
                        continue;
                    }
                }
                buffer.Append(text[i]);
                i++;
            }
            Flush();
            return spans;

            void Flush()
            {
                if (buffer.Length > 0)
                {
                    spans.Add(new MdSpan(buffer.ToString()));
                    buffer.Clear();
                }
            }
        }
    }
}
