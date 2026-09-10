using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backtest.Services
{
    public class FxCloudService
    {
        public static HttpClient _httpClient;
        public static string defaultUrl = "https://fxdataedge.com/";
        private const string TokenFileName = "session.bin";
        private readonly string _localProfileCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        private readonly string _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        public readonly string _sessionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_v1.json");
        private static readonly object _logLock = new object();

        // État d'authentification PARTAGÉ par toutes les instances du service :
        // la connexion / création de compte passe par l'instance de SettingsView
        // tandis que l'agent IA et la synchro utilisent celle de MainWindow.
        // Statique = dès qu'un compte se connecte, tout le logiciel le voit
        // immédiatement, sans redémarrage (bug : l'agent IA restait « non
        // connecté » jusqu'à la réouverture de l'application).
        public static string CurrentToken { get; private set; }
        public static string RefreshToken { get; private set; }
        public string AppId { get; private set; }

        // ---- Paramètres de synchronisation (modifiables depuis Settings) ----
        // false => le dossier cacheimage/ (cache d'images re-générable, souvent
        // volumineux) est exclu du manifest de synchronisation.
        public static bool SyncCacheImageEnabled = true;
        // Nombre de fichiers par requête batch (endpoint /api/cloud/sync-batch).
        private static readonly int BatchFileCount = 8;
        // Passe à false si le serveur ne connaît pas encore sync-batch (repli unitaire).
        private bool _batchSupported = true;

        static FxCloudService()
        {
            // Réglages réseau de .NET Framework (ServicePoint) — sans eux, les défauts
            // plombent toutes les communications avec le serveur :
            //  - DefaultConnectionLimit = 2 : dès que le chat IA (flux SSE longue durée)
            //    occupe une connexion, sync/profil/données marché font la file d'attente ;
            //  - Expect100Continue = true : chaque POST attend d'abord un aller-retour
            //    "Expect: 100-continue" avant d'envoyer son corps ;
            //  - UseNagleAlgorithm = true : délais de groupement TCP (jusqu'à ~200 ms).
            ServicePointManager.DefaultConnectionLimit = 64;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        }

        public FxCloudService()
        {
            InitializeService();
        }

        public HttpClient GetHttpClient()
        {
            return _httpClient;
        }

        #region CONFIGURATION ET INITIALISATION

        private void InitializeService()
        {
            string serverUrl = LoadConfiguration();

            if (_httpClient == null)
            {
                _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    // Défaut .NET = 100 s : coupait les réponses dont les en-têtes tardent
                    // (sync multi-fichiers, tours agent IA). La lecture des flux SSE n'est
                    // pas concernée (HttpCompletionOption.ResponseHeadersRead côté agent).
                    Timeout = TimeSpan.FromMinutes(10)
                };
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            LoadTokens();
            if (!Directory.Exists(_localProfileCache)) Directory.CreateDirectory(_localProfileCache);
        }
        public static string getServeur() { return defaultUrl; }

        /// <summary>
        /// Active/désactive la synchronisation du dossier cacheimage/ et persiste
        /// le choix dans config.txt (partagé par toutes les instances du service).
        /// </summary>
        public static void SetSyncCacheImageEnabled(bool enabled)
        {
            SyncCacheImageEnabled = enabled;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                var lines = new List<string>();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (line.Trim().StartsWith("sync_cacheimage=", StringComparison.OrdinalIgnoreCase)) continue;
                        lines.Add(line);
                    }
                }
                lines.Add("sync_cacheimage=" + (enabled ? "true" : "false"));
                File.WriteAllText(path, String.Join("\r\n", lines));
            }
            catch { /* Réglage non persistant : l'appli continue avec la valeur en mémoire */ }
        }
        private string LoadConfiguration()
        {
            
            AppId = "FX_DATAEDGE";

            if (File.Exists(_configFilePath))
            {
                var lines = File.ReadAllLines(_configFilePath);
                foreach (var line in lines)
                {
                    string cleanLine = line.Trim();
                    if (cleanLine.StartsWith("server=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = cleanLine.Substring(7).Trim();
                        defaultUrl = url;
                        if (!string.IsNullOrEmpty(url)) defaultUrl = url.EndsWith("/") ? url : url + "/";
                    }
                    else if (cleanLine.StartsWith("app_id=", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = cleanLine.Substring(7).Trim();
                        if (!string.IsNullOrEmpty(id)) AppId = id;
                    }
                    else if (cleanLine.StartsWith("sync_cacheimage=", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = cleanLine.Substring(16).Trim().ToLowerInvariant();
                        SyncCacheImageEnabled = v == "true" || v == "1" || v == "oui";
                    }
                    else if (cleanLine.StartsWith("sync_folder_", StringComparison.OrdinalIgnoreCase))
                    {
                        // sync_folder_<nom>=false → dossier exclu de la synchro
                        int eq = cleanLine.IndexOf('=');
                        string folder = cleanLine.Substring(12, eq - 12).Trim();
                        string val = cleanLine.Substring(eq + 1).Trim().ToLowerInvariant();
                        if (val != "true" && val != "1" && val != "oui")
                            SyncExcludedFolders.Add(folder);
                        else
                            SyncExcludedFolders.Remove(folder);
                    }
                }
            }
            else
            {
                string configContent = "# CONFIGURATION FX-GLOBAL\nserver=https://fxdataedge.com/\napp_id=FX_DATAEDGE";
                File.WriteAllText(_configFilePath, configContent);
            }
            return defaultUrl;
        }

        #endregion

        #region AUTHENTIFICATION

        public async Task<string> RegisterAsync(string email, string phone, string password, string fullName, string bio, string imagePath)
        {
            string status = await GetCloudStatusAsync();
            if (status == "OFFLINE_NO_INTERNET") return "pas d'internet";
            if (status == "OFFLINE_SERVER_DOWN") return "serveur inaccessible";

            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(email ?? ""), "email");
                content.Add(new StringContent(password), "password");
                content.Add(new StringContent(fullName), "fullName");
                content.Add(new StringContent(phone ?? ""), "phone");
                content.Add(new StringContent(bio ?? ""), "bio");

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    var fileBytes = File.ReadAllBytes(imagePath);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
                    content.Add(fileContent, "image", Path.GetFileName(imagePath));
                }

                var response = await _httpClient.PostAsync("api/register", content);

                if (response.IsSuccessStatusCode)
                {
                    await LoginAsync(email ?? phone, password);
                    return "yes";
                }
                return response.StatusCode == System.Net.HttpStatusCode.Conflict ? "email déjà utilisé" : "erreur serveur";
            }
            catch { return "erreur inconnue"; }
        }

        public async Task<string> LoginAsync(string identifier, string password)
        {
            string status = await GetCloudStatusAsync();
            if (status == "OFFLINE_NO_INTERNET") return "pas d'internet";
            if (status == "OFFLINE_SERVER_DOWN") return "serveur inaccessible";

            try
            {
                var data = new { identifier = identifier, password = password };
                var response = await _httpClient.PostAsync("/api/login", GetJsonContent(data));

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        CurrentToken = doc.RootElement.GetProperty("token").GetString();
                        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh))
                            RefreshToken = refresh.GetString();
                    }
                    SaveTokens();
                    return "yes";
                }
                return response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "identifiants incorrects" : "erreur serveur";
            }
            catch { return "erreur inconnue"; }
        }

        public async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(RefreshToken)) return false;
            try
            {
                var data = new { refresh_token = RefreshToken };
                var response = await _httpClient.PostAsync("token/refresh", GetJsonContent(data));
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        CurrentToken = doc.RootElement.GetProperty("token").GetString();
                        if (doc.RootElement.TryGetProperty("refresh_token", out var refresh))
                            RefreshToken = refresh.GetString();
                    }
                    SaveTokens();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public void Logout()
        {
            CurrentToken = null;
            RefreshToken = null;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);
            if (File.Exists(path)) File.Delete(path);
            DeleteSessionFromDisk();
        }

        #endregion

        #region PROFIL ET SESSION

        public async Task<UserSessionData> GetProfileAsync()
        {
            if (string.IsNullOrEmpty(CurrentToken)) return null;
            try
            {
                var response = await SecureRequestAsync(() => _httpClient.GetAsync("api/me"));
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var session = JsonSerializer.Deserialize<UserSessionData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    session.ImagePath = await DownloadProfileImageAsync(session.ImagePath);
                    SaveSessionToDisk(session.FullName, session.Email, session.Phone, session.Bio, session.ImagePath);
                    return session;
                }
            }
            catch { }
            return null;
        }

        public async Task<(bool success, string message, string newImagePath)> UpdateUserProfileAsync(string fullName, string phone, string bio, string localImagePath = null)
        {
            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(fullName ?? ""), "fullName");
                    content.Add(new StringContent(phone ?? ""), "phone");
                    content.Add(new StringContent(bio ?? ""), "bio");

                    if (!string.IsNullOrEmpty(localImagePath) && File.Exists(localImagePath))
                    {
                        var fileBytes = File.ReadAllBytes(localImagePath);
                        var fileContent = new ByteArrayContent(fileBytes);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                        content.Add(fileContent, "image", Path.GetFileName(localImagePath));
                    }

                    var response = await SecureRequestAsync(() => _httpClient.PostAsync("api/user/update", content));
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using (var doc = JsonDocument.Parse(json))
                        {
                            string serverImg = doc.RootElement.GetProperty("imagePath").GetString();
                            string mail = doc.RootElement.GetProperty("email").GetString();
                            SaveSessionToDisk(fullName, mail, phone, bio, serverImg);
                            return (true, "Profil mis à jour !", serverImg);
                        }
                    }
                    return (false, $"Erreur: {response.StatusCode}", null);
                }
            }
            catch (Exception ex) { return (false, $"Erreur réseau : {ex.Message}", null); }
        }

        #endregion

        #region SYNCHRONISATION CLOUD

public async Task<List<string>> FullSyncAsync()
        {
            var reports = new List<string> { $"--- Début synchro ({DateTime.Now:HH:mm}) ---" };
            try
            {
                reports.Add("Récupération du manifest serveur...");

                // 1. Manifest serveur UNIQUE : liste des fichiers distants + hash
                //    (une seule requête au lieu d'un file-info par fichier).
                var remoteFiles = await FetchCloudManifestAsync();
                if (remoteFiles == null)
                {
                    reports.Add("!!! Erreur : liste serveur inaccessible");
                    return reports;
                }

                var cache = LoadSyncCache();
                _batchSupported = true;

                // 2. Inventaire local : hash md5 calculé une seule fois par synchro.
                var localFiles = ScanLocalFiles();
                bool isInitialRestore = localFiles.Count == 0 && remoteFiles.Count > 0;
                var toUpload = new List<SyncLocalFile>();
                foreach (var lf in localFiles)
                {
                    var rf = FindRemote(remoteFiles, lf.RelativePath);
                    // Fichier absent côté serveur OU contenu différent = à pousser.
                    if (rf == null || rf.hash != lf.Hash) toUpload.Add(lf);
                }

                if (isInitialRestore)
                    reports.Add("Restauration initiale : aucune donnée locale, récupération de tout le cloud...");

                // 3. Upload par lots (endpoint batch : 1 requête pour N fichiers).
                await UploadFilesAsync(toUpload, remoteFiles, cache, reports);

                // 4. Téléchargement des fichiers absents / modifiés côté serveur,
                //    avec résolution de conflits (plus aucun écrasement silencieux).
                int downloadCount = await DownloadFromServerAsync(remoteFiles, cache, reports);
                reports.Add($"Récupération des fichiers distants : {downloadCount} fichier(s) mis à jour.");

                SaveSyncCache(cache);
                UpdateLocalLastSync(DateTime.Now);
                reports.Add("--- Synchronisation terminée ---");
            }
            catch (Exception ex)
            {
                reports.Add("!!! Erreur : " + ex.Message);
            }
            return reports;
        }

        // ------------------------------------------------------------------
        // Récupère la liste des fichiers distants (GET /api/cloud/list)
        // ------------------------------------------------------------------
        private async Task<List<CloudFileInfo>> FetchCloudManifestAsync()
        {
            try
            {
                var response = await SecureRequestAsync(() => _httpClient.GetAsync($"api/cloud/list?app_id={AppId}"));
                if (!response.IsSuccessStatusCode) return null;
                var manifest = JsonSerializer.Deserialize<CloudManifest>(await response.Content.ReadAsStringAsync());
                return manifest != null ? manifest.files : null;
            }
            catch { return null; }
        }

        // Version publique (onglet CLOUD des paramètres) du manifest distant.
        public async Task<List<CloudFileInfo>> FetchCloudManifestPublicAsync()
            => await FetchCloudManifestAsync();

        // ------------------------------------------------------------------
        // Pousse les fichiers locaux vers le serveur par lots de BatchFileCount.
        // Repli automatique sur l'endpoint unitaire (sync-file) si le serveur
        // ne connaît pas encore /api/cloud/sync-batch ou si un lot échoue.
        // ------------------------------------------------------------------
        private async Task UploadFilesAsync(List<SyncLocalFile> files, List<CloudFileInfo> remoteFiles, Dictionary<string, string> cache, List<string> reports)
        {
            if (files.Count == 0) return;
            reports.Add($"Envoi de {files.Count} fichier(s)...");

            int i = 0;
            while (i < files.Count)
            {
                int end = i + BatchFileCount;
                if (end > files.Count) end = files.Count;

                var batch = new List<SyncLocalFile>();
                for (int j = i; j < end; j++) batch.Add(files[j]);

                bool ok = false;
                if (_batchSupported)
                {
                    ok = await TryUploadBatchAsync(batch, remoteFiles, cache, reports);
                }

                if (!ok)
                {
                    // Repli unitaire : isole les erreurs fichier par fichier.
                    foreach (var f in batch)
                    {
                        string res = await SyncFileAsync(f.LocalFullPath, f.RelativePath);
                        if (res == "success")
                        {
                            cache[f.RelativePath] = f.Hash;
                            MarkServerHash(remoteFiles, f.RelativePath, f.Hash);
                            reports.Add(f.RelativePath + ": success");
                        }
                        else
                        {
                            reports.Add($"{f.RelativePath}: {res}");
                        }
                    }
                }

                i = end;
            }
        }

        private async Task<bool> TryUploadBatchAsync(List<SyncLocalFile> batch, List<CloudFileInfo> remoteFiles, Dictionary<string, string> cache, List<string> reports)
        {
            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(AppId), "app_id");
                foreach (var f in batch)
                {
                    content.Add(new StringContent(f.RelativePath), "paths");
                    content.Add(new StringContent(f.Hash), "hashes");
                    content.Add(new ByteArrayContent(File.ReadAllBytes(f.LocalFullPath)), "files", Path.GetFileName(f.LocalFullPath));
                }

                var res = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/sync-batch", content));

                // Route absente sur le serveur (backend pas encore déployé) : repli unitaire.
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound
                    || res.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    _batchSupported = false;
                    return false;
                }

                if (res.IsSuccessStatusCode)
                {
                    foreach (var f in batch)
                    {
                        cache[f.RelativePath] = f.Hash;
                        MarkServerHash(remoteFiles, f.RelativePath, f.Hash);
                        reports.Add(f.RelativePath + ": success");
                    }
                    return true;
                }

                return false; // 400 / 413 / 5xx → repli unitaire
            }
            catch
            {
                return false; // erreur réseau → repli unitaire
            }
        }

        // Met à jour le hash dans le manifest local : la phase download ne
        // re-télécharge pas le contenu qu'on vient de pousser.
        private void MarkServerHash(List<CloudFileInfo> remoteFiles, string path, string hash)
        {
            var rf = FindRemote(remoteFiles, path);
            if (rf != null) rf.hash = hash;
        }

        // ------------------------------------------------------------------
        // Téléchargement des fichiers absents ou modifiés, avec résolution de
        // conflits basée sur le cache du dernier hash serveur vu (pas besoin
        // d'horodatage : on compare les états « vus » des deux côtés).
        // ------------------------------------------------------------------
        private async Task<int> DownloadFromServerAsync(List<CloudFileInfo> remoteFiles, Dictionary<string, string> cache, List<string> reports)
        {
            int count = 0;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var remote in remoteFiles)
            {
                if (IsSyncInternalPath(remote.path)) continue;
                if (!SyncCacheImageEnabled && remote.path.StartsWith("cacheimage/", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsCloudFolderExcluded(remote.path)) continue;

                string local = Path.Combine(baseDir, remote.path);
                bool exists = File.Exists(local);
                string localHash = exists ? GetFileHash(local) : null;

                if (exists && localHash == remote.hash) continue; // déjà à jour

                if (!exists)
                {
                    if (await DownloadFileAsync(remote.path, local))
                    {
                        count++;
                        cache[remote.path] = remote.hash;
                        reports.Add(remote.path + ": success");
                    }
                    else
                    {
                        reports.Add(remote.path + ": Erreur téléchargement");
                    }
                    continue;
                }

                // Conflit local/distant : on tranche grâce au cache du dernier état serveur vu.
                string cachedHash = cache.ContainsKey(remote.path) ? cache[remote.path] : null;

                if (cachedHash == null)
                {
                    // Jamais synchronisé avec le serveur : la version locale est conservée
                    // (elle vient d'être poussée par la phase upload, aucun écrasement).
                    cache[remote.path] = localHash;
                    reports.Add(remote.path + ": conservé (premier passage)");
                }
                else if (cachedHash == remote.hash)
                {
                    // Le serveur n'a pas bougé depuis notre dernière sync : la modification
                    // est locale (elle a déjà été poussée ci-dessus) → on garde local.
                    cache[remote.path] = localHash;
                    reports.Add(remote.path + ": mis à jour (local)");
                }
                else if (cachedHash == localHash)
                {
                    // Local inchangé depuis notre dernière sync ET serveur modifié → on récupère.
                    if (await DownloadFileAsync(remote.path, local))
                    {
                        count++;
                        cache[remote.path] = remote.hash;
                        reports.Add(remote.path + ": success");
                    }
                    else
                    {
                        reports.Add(remote.path + ": Erreur téléchargement");
                    }
                    }
                    else
                    {
                        // Conflit réel (les deux ont changé) : version locale conservée,
                        // ancienne version distante préservée côté serveur (.bak).
                        cache[remote.path] = localHash;
                        reports.Add(remote.path + ": conflit, version locale conservée");
                    }
                }

            return count;
        }
        
// ------------------------------------------------------------------
        // Endpoint unitaire : vérifie le hash distant puis pousse le fichier.
        // Utilisé uniquement en repli (serveur sans sync-batch, lot en erreur).
        // ------------------------------------------------------------------
        private async Task<string> SyncFileAsync(string localPath, string remotePath)
        {
            try
            {
                string hash = GetFileHash(localPath);
                var checkContent = new MultipartFormDataContent();
                checkContent.Add(new StringContent(AppId), "app_id");
                checkContent.Add(new StringContent(remotePath), "target_path");

                var check = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/file-info", checkContent));
                if (check.IsSuccessStatusCode)
                {
                    using (var doc = JsonDocument.Parse(await check.Content.ReadAsStringAsync()))
                        if (doc.RootElement.TryGetProperty("hash", out var sHash) && sHash.GetString() == hash) return "à jour";
                }

                var upload = new MultipartFormDataContent();
                upload.Add(new StringContent(AppId), "app_id");
                upload.Add(new StringContent(remotePath), "target_path");
                upload.Add(new StringContent(hash), "file_hash");
                upload.Add(new ByteArrayContent(File.ReadAllBytes(localPath)), "file", Path.GetFileName(localPath));

                var res = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/sync-file", upload));
                return res.IsSuccessStatusCode ? "success" : "erreur serveur";
            }
            catch (Exception ex) { return "erreur: " + ex.Message; }
        }

        public async Task<bool> DownloadFileAsync(string remotePath, string localPath)
        {
            try
            {
                string url = $"api/cloud/download?app_id={AppId}&target_path={Uri.EscapeDataString(remotePath)}";
                var res = await SecureRequestAsync(() => _httpClient.GetAsync(url));
                if (res.IsSuccessStatusCode)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    using (var fs = new FileStream(localPath, FileMode.Create))
                        await res.Content.CopyToAsync(fs);
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ------------------------------------------------------------------
        // Inventaire local : liste les fichiers du manifest applicatif et calcule
        // leur hash md5 (une seule passe par synchro).
        // ------------------------------------------------------------------
        private List<SyncLocalFile> ScanLocalFiles()
        {
            var result = new List<SyncLocalFile>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var item in GetAppSyncManifest())
            {
                string fullPath = Path.Combine(baseDir, item.LocalPath);
                if (item.IsDirectory && Directory.Exists(fullPath))
                {
                    foreach (var file in Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories))
                    {
                        string relative = file.Replace(fullPath, "").Replace("\\", "/").TrimStart('/');
                        string remote = Path.Combine(item.RemoteRelativePath, relative).Replace("\\", "/");
                        AddLocalFile(result, file, remote);
                    }
                }
                else if (File.Exists(fullPath))
                {
                    AddLocalFile(result, fullPath, item.RemoteRelativePath);
                }
            }
            return result;
        }
        private void AddLocalFile(List<SyncLocalFile> result, string fullPath, string remotePath)
        {
            if (IsSyncInternalPath(remotePath)) return;
            if (!SyncCacheImageEnabled && remotePath.StartsWith("cacheimage/", StringComparison.OrdinalIgnoreCase)) return;
            if (IsCloudFolderExcluded(remotePath)) return;
            result.Add(new SyncLocalFile { LocalFullPath = fullPath, RelativePath = remotePath, Hash = GetFileHash(fullPath) });
        }

        private CloudFileInfo FindRemote(List<CloudFileInfo> remoteFiles, string path)
        {
            foreach (var f in remoteFiles)
                if (f.path == path) return f;
            return null;
        }

        // Fichier interne de suivi de synchro : jamais envoyé ni téléchargé.
        private bool IsSyncInternalPath(string rel)
        {
            return rel == "metadata/synccache.json" || rel == "metadata/hashcache.json";
        }

        // ------------------------------------------------------------------
        // Cache du dernier hash serveur par fichier. Il départage un conflit :
        // état serveur inchangé depuis la dernière sync → modif locale ;
        // état serveur changé alors que le local ne bougeait pas → serveur gagne.
        // Persisté dans metadata/synccache.json (exclu de la synchro).
        // ------------------------------------------------------------------
        private string GetSyncCachePath()
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metadata", "synccache.json");

        private Dictionary<string, string> LoadSyncCache()
        {
            try
            {
                string path = GetSyncCachePath();
                if (!File.Exists(path)) return new Dictionary<string, string>();
                var root = JsonSerializer.Deserialize<SyncCacheRoot>(File.ReadAllText(path));
                return (root != null && root.hashes != null) ? root.hashes : new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        private void SaveSyncCache(Dictionary<string, string> cache)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GetSyncCachePath()));
                File.WriteAllText(GetSyncCachePath(), JsonSerializer.Serialize(new SyncCacheRoot { hashes = cache }));
            }
            catch { }
        }
public async Task<string> DownloadProfileImageAsync(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string local = Path.Combine(_localProfileCache, fileName);
            if (File.Exists(local)) return local;

            try
            {
                var res = await _httpClient.GetAsync($"profiles/{fileName}");
                if (res.IsSuccessStatusCode)
                {
                    File.WriteAllBytes(local, await res.Content.ReadAsByteArrayAsync());
                    return local;
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region GESTION CLOUD (Settings > onglet CLOUD)

        // ------------------------------------------------------------------
        // Exclusions de synchronisation par dossier de premier niveau
        // (ex. "cacheimage", "Notes"). Persistées dans config.txt sous la
        // forme sync_folder_<nom>=false. Le toggle cacheimage historique
        // reste maitrisé par SyncCacheImageEnabled (config sync_cacheimage=).
        // ------------------------------------------------------------------
        public static readonly HashSet<string> SyncExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsCloudFolderExcluded(string remoteRelativePath)
        {
            if (string.IsNullOrEmpty(remoteRelativePath)) return false;
            int slash = remoteRelativePath.IndexOf('/');
            string folder = slash > 0 ? remoteRelativePath.Substring(0, slash) : remoteRelativePath;
            if (folder.Equals("cacheimage", StringComparison.OrdinalIgnoreCase))
                return !SyncCacheImageEnabled;
            return SyncExcludedFolders.Contains(folder);
        }

        /// <summary>
        /// Active/désactive la synchronisation d'un dossier de premier niveau
        /// ("data", "etudes", "Notes", "cacheimage", "metadata") et persiste
        /// le choix dans config.txt (partagé par toutes les instances).
        /// </summary>
        public static void SetCloudFolderSyncEnabled(string folder, bool enabled)
        {
            if (string.IsNullOrEmpty(folder)) return;

            if (folder.Equals("cacheimage", StringComparison.OrdinalIgnoreCase))
            {
                SetSyncCacheImageEnabled(enabled);
                return;
            }

            string key = "sync_folder_" + folder.ToLowerInvariant() + "=";
            if (enabled) SyncExcludedFolders.Remove(folder);
            else SyncExcludedFolders.Add(folder);

            PersistConfigKey(key, enabled ? "true" : "false");
        }

        // Écrit une clé dans config.txt en supprimant l'ancienne ligne (pattern
        // identique à SetSyncCacheImageEnabled, factorisé ici).
        private static void PersistConfigKey(string key, string value)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                var lines = new List<string>();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (line.Trim().StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
                        lines.Add(line);
                    }
                }
                lines.Add(key + value);
                File.WriteAllText(path, String.Join("\r\n", lines));
            }
            catch { /* Réglage non persistant : l'appli continue avec la valeur en mémoire */ }
        }

        // ------------------------------------------------------------------
        // API « gestion cloud » (onglet CLOUD des paramètres) : backups,
        // restauration, suppression, quota. Endpoints /api/cloud/* du backend.
        // ------------------------------------------------------------------

        /// <summary>
        /// Liste les sauvegardes .bak du compte (serveur), les plus récentes d'abord.
        /// </summary>
        public async Task<List<CloudBackupInfo>> GetCloudBackupsAsync()
        {
            try
            {
                var res = await SecureRequestAsync(() => _httpClient.GetAsync($"api/cloud/list-backups?app_id={AppId}"));
                if (!res.IsSuccessStatusCode) return null;
                var root = JsonSerializer.Deserialize<CloudBackupList>(await res.Content.ReadAsStringAsync());
                return root != null ? root.backups : null;
            }
            catch (Exception ex) { Log("GetCloudBackupsAsync: " + ex.Message); return null; }
        }

        /// <summary>
        /// Restaure une sauvegarde .bak sur le fichier principal (côté serveur).
        /// Retour : (succès, message).
        /// </summary>
        public async Task<(bool success, string message)> RestoreCloudBackupAsync(string backupPath)
        {
            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(AppId), "app_id");
                content.Add(new StringContent(backupPath), "backup_path");

                var res = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/restore-backup", content));
                if (res.IsSuccessStatusCode) return (true, "Sauvegarde restaurée !");
                return (false, "Erreur serveur (" + (int)res.StatusCode + ")");
            }
            catch (Exception ex) { return (false, "Erreur réseau : " + ex.Message); }
        }

        /// <summary>
        /// Supprime un fichier distant (et ses backups .bak associés).
        /// Retour : (succès, message).
        /// </summary>
        public async Task<(bool success, string message)> DeleteCloudFileAsync(string targetPath)
        {
            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(AppId), "app_id");
                content.Add(new StringContent(targetPath), "target_path");

                var res = await SecureRequestAsync(() => _httpClient.PostAsync("api/cloud/delete-file", content));
                if (res.IsSuccessStatusCode) return (true, "Fichier supprimé du cloud.");
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return (false, "Fichier introuvable côté serveur.");
                return (false, "Erreur serveur (" + (int)res.StatusCode + ")");
            }
            catch (Exception ex) { return (false, "Erreur réseau : " + ex.Message); }
        }

        /// <summary>
        /// Résumé du stockage cloud : taille totale, nb fichiers, taille des backups.
        /// </summary>
        public async Task<CloudStorageInfo> GetCloudStorageInfoAsync()
        {
            try
            {
                var res = await SecureRequestAsync(() => _httpClient.GetAsync($"api/cloud/storage?app_id={AppId}"));
                if (!res.IsSuccessStatusCode) return null;
                return JsonSerializer.Deserialize<CloudStorageInfo>(await res.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { Log("GetCloudStorageInfoAsync: " + ex.Message); return null; }
        }

        #endregion

        #region UTILITAIRES ET RÉSEAU
        public static void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(logPath, logLine);
                }
            }
            catch
            {
                // On ne lève pas d'exception pour un log pour ne pas bloquer le logiciel
            }
        }

        public async Task<HttpResponseMessage> SecureRequestAsync(Func<Task<HttpResponseMessage>> requestFunc)
        {
            SetAuthHeader();
            var res = await requestFunc();
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                if (await RefreshTokenAsync())
                {
                    SetAuthHeader();
                    return await requestFunc();
                }
            }
            return res;
        }

        private void SetAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization = !string.IsNullOrEmpty(CurrentToken)
                ? new AuthenticationHeaderValue("Bearer", CurrentToken) : null;
        }

        // Cache court du statut cloud : évite de rejouer les sondes (ping 8.8.8.8 +
        // GET /home) avant chaque message de l'agent IA. Seul un statut positif est
        // mémorisé : un échec reste toujours vérifié en temps réel.
        private const int CloudStatusCacheSeconds = 30;
        private string _cachedCloudStatus;
        private DateTime _cachedCloudStatusAt;

        public async Task<string> GetCloudStatusAsync(bool useCachedIfFresh = false)
        {
            if (useCachedIfFresh
                && _cachedCloudStatus == "READY"
                && (DateTime.UtcNow - _cachedCloudStatusAt).TotalSeconds < CloudStatusCacheSeconds)
            {
                return _cachedCloudStatus;
            }

            // Les deux sondes sont indépendantes : on les lance en parallèle pour ne
            // pas additionner leurs délais avant chaque message envoyé à l'agent IA.
            var internetTask = IsInternetAvailableAsync();
            var serverTask = IsServerReachableAsync();
            await Task.WhenAll(internetTask, serverTask);

            // La sonde HTTP vers le serveur fait foi : si fxdataedge.com répond,
            // la connexion est fonctionnelle même si l'ICMP sortant (ping 8.8.8.8)
            // est bloqué par le réseau (VPN, pare-feu d'entreprise/public, FAI).
            // Avant, le ping était testé EN PREMIER : le client annonçait
            // « Connexion Internet non disponible » alors que le serveur répondait
            // 200 (visible dans les logs) — faux négatif désormais impossible.
            if (serverTask.Result)
            {
                string status = string.IsNullOrEmpty(CurrentToken) ? "ONLINE_NO_ACCOUNT" : "READY";
                if (status == "READY")
                {
                    _cachedCloudStatus = status;
                    _cachedCloudStatusAt = DateTime.UtcNow;
                }
                return status;
            }

            // Serveur injoignable : la sonde Internet (ping + replis) permet de
            // départager « pas d'internet » d'un « serveur down ».
            if (!internetTask.Result) return "OFFLINE_NO_INTERNET";
            return "OFFLINE_SERVER_DOWN";
        }

        public async Task<bool> IsInternetAvailableAsync()
        {
            // Sonde primaire : ICMP vers 8.8.8.8 (le plus rapide). Mais de nombreux
            // réseaux bloquent l'ICMP sortant sans toucher au HTTP : on retente un
            // second point d'entrée ICMP (1.1.1.1) puis une sonde HTTP légère
            // (gstatic generate_204 → 204 sans corps). Si tout échoue, pas d'internet.
            try
            {
                using (var p = new Ping())
                {
                    if ((await p.SendPingAsync("8.8.8.8", 1000)).Status == IPStatus.Success) return true;
                    if ((await p.SendPingAsync("1.1.1.1", 1000)).Status == IPStatus.Success) return true;
                }
            }
            catch { }

            try
            {
                // HttpWebRequest isolé (pas le _httpClient partagé) : ce dernier porte
                // l'en-tête Authorization Bearer par défaut — inutile de l'envoyer à
                // un domaine tiers. Sonde sans corps (HEAD), bornée à 2 s.
                var probe = (HttpWebRequest)WebRequest.Create("https://www.gstatic.com/generate_204");
                probe.Method = "HEAD";
                probe.Timeout = 2000;
                probe.ReadWriteTimeout = 2000;
                using (var res = (HttpWebResponse)(await probe.GetResponseAsync()))
                {
                    return (int)res.StatusCode < 500;
                }
            }
            catch { return false; }
        }

        public async Task<bool> IsServerReachableAsync()
        {
            try
            {
                // Sonde bornée à 2 s et limitée aux en-têtes (HttpCompletionOption) :
                // avant, on téléchargeait la page "home" complète (Twig + moteur) à
                // chaque appel — inutile pour vérifier que le serveur répond.
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                using (var res = await _httpClient.GetAsync("home", HttpCompletionOption.ResponseHeadersRead, cts.Token))
                {
                    return (int)res.StatusCode < 500;
                }
            }
            catch { return false; }
        }

        private string GetFileHash(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        private StringContent GetJsonContent(object data)
            => new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

        public List<SyncItem> GetAppSyncManifest()
            => new List<SyncItem> {
                new SyncItem { LocalPath = "data/", RemoteRelativePath = "data/", IsDirectory = true },
                new SyncItem { LocalPath = "etudes/", RemoteRelativePath = "etudes/", IsDirectory = true },
                new SyncItem { LocalPath = "Notes/", RemoteRelativePath = "Notes/", IsDirectory = true },
                new SyncItem { LocalPath = "cacheimage/", RemoteRelativePath = "cacheimage/", IsDirectory = true },
                new SyncItem { LocalPath = "metadata/", RemoteRelativePath = "metadata/", IsDirectory = true },

            };

        #endregion

        #region GESTION DISQUE LOCALE

        public void SaveSessionToDisk(string name, string email, string phone, string bio, string imgPath)
        {
            try
            {
                var data = new UserSessionData { IsLoggedIn = true, FullName = name, Email = email, Phone = phone, Bio = bio, ImagePath = imgPath };
                File.WriteAllText(_sessionFilePath, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        public void UpdateLocalLastSync(DateTime date)
        {
            if (!File.Exists(_sessionFilePath)) return;
            try
            {
                var user = JsonSerializer.Deserialize<UserSessionData>(File.ReadAllText(_sessionFilePath));
                user.LastSyncDate = date;
                File.WriteAllText(_sessionFilePath, JsonSerializer.Serialize(user));
            }
            catch { }
        }

        public void DeleteSessionFromDisk() { if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath); }

        // ------------------------------------------------------------------
        // Persistance des tokens (session.bin) CHIFFRÉE via DPAPI
        // (ProtectedData.CurrentUser) : le fichier n'est lisible que par le
        // compte Windows qui l'a créé — plus aucun JWT / refresh en clair.
        // Migration automatique depuis l'ancien format en clair au premier
        // lancement (lecture → re-sauvegarde chiffrée).
        // ------------------------------------------------------------------
        private string GetTokenFilePath()
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TokenFileName);

        private void SaveTokens()
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes((CurrentToken ?? "") + "\n" + (RefreshToken ?? ""));
                byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllText(GetTokenFilePath(), Convert.ToBase64String(encrypted));
            }
            catch
            {
                // Secours : si le chiffrement échoue, on conserve l'ancien format
                // plutôt que de perdre la session de l'utilisateur.
                File.WriteAllLines(GetTokenFilePath(), new[] { CurrentToken ?? "", RefreshToken ?? "" });
            }
        }

        private void LoadTokens()
        {
            // Les tokens sont partagés (statique) : si une connexion vient d'avoir
            // lieu en mémoire (SettingsView), ne jamais l'écraser avec le contenu
            // potentiellement périmé du disque lors d'une construction d'instance.
            if (!string.IsNullOrEmpty(CurrentToken)) return;

            string path = GetTokenFilePath();
            if (!File.Exists(path)) return;

            string content = File.ReadAllText(path);
            bool migrated = false;
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(content), null, DataProtectionScope.CurrentUser);
                string[] parts = Encoding.UTF8.GetString(plain).Split('\n');
                if (parts.Length > 0) CurrentToken = parts[0];
                if (parts.Length > 1) RefreshToken = parts[1];
            }
            catch
            {
                // Ancien format en clair (session pré-migration) : lecture directe,
                // puis re-sauvegarde immédiate au format chiffré.
                string[] lines = content.Split('\n');
                if (lines.Length > 0) CurrentToken = lines[0].Trim();
                if (lines.Length > 1) RefreshToken = lines[1].Trim();
                migrated = true;
            }
            if (migrated && !string.IsNullOrEmpty(CurrentToken)) SaveTokens();
        }

        #endregion

        #region software handshake and control

        // Structure pour mapper le JSON de Symfony
      
        public async Task<HandshakeResponse> CheckSoftwareStatusAsync(string currentVersion, string username = "Guest")
        {
            try
            {
                var data = new
                {
                    app_id = AppId,
                    version = currentVersion,
                    username = username,
                    machine_id = Environment.MachineName
                };

                var response = await _httpClient.PostAsync("software/handshake", GetJsonContent(data));

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<HandshakeResponse>(json, options);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log("Erreur Handshake critique: " + ex.Message);
            }
            return null;
        }
        public async Task SendCrashReportAsync(string errorStack)
        {
            try
            {
                var data = new
                {
                    app_id = AppId,
                    machine_id = Environment.MachineName,
                    error_stack = errorStack
                };
                await _httpClient.PostAsync("software/report-crash", GetJsonContent(data));
            }
            catch { /* On ne bloque pas si l'envoi du log échoue */ }
        }
        public async Task<bool> SendSupportMessageAsync(string type, string message, string user = "Guest")
        {
            try
            {
                var data = new
                {
                    user = user,
                    type = type, // ex: "Suggestion", "Bug", "Contact"
                    content = message,
                    machine_id = Environment.MachineName
                };

                var response = await _httpClient.PostAsync("support/send", GetJsonContent(data));
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }
        #endregion
    }

    #region CLASSES DE DONNÉES
    public class SyncItem { public string LocalPath { get; set; } public string RemoteRelativePath { get; set; } public bool IsDirectory { get; set; } }
    public class SyncLocalFile { public string LocalFullPath { get; set; } public string RelativePath { get; set; } public string Hash { get; set; } }
    public class SyncCacheRoot { public Dictionary<string, string> hashes { get; set; } = new Dictionary<string, string>(); }
    public class CloudManifest { public string app_id { get; set; } public List<CloudFileInfo> files { get; set; } }
    public class CloudFileInfo { public string path { get; set; } public string hash { get; set; } public long size { get; set; } public long last_modified { get; set; } }
    public class CloudBackupInfo { public string path { get; set; } public long size { get; set; } public long last_modified { get; set; } }
    public class CloudBackupList { public string app_id { get; set; } public List<CloudBackupInfo> backups { get; set; } = new List<CloudBackupInfo>(); public long server_time { get; set; } }
    public class CloudStorageInfo { public string app_id { get; set; } public long total_bytes { get; set; } public long file_count { get; set; } public long backup_count { get; set; } public long backup_bytes { get; set; } }
    public class UserSession{ public string FullName { get; set; }public string Email { get; set; }public string Phone { get; set; }public string Bio { get; set; } public string LocalImagePath { get; set; } public DateTime? LastSyncDate { get; set; } }
    public class UserSessionData{public bool IsLoggedIn { get; set; }public string FullName { get; set; }public string Email { get; set; }public string Phone { get; set; }public string Bio { get; set; } public DateTime? LastSyncDate { get; set; }public string ImagePath { get; set; }}
    public class HandshakeResponse
    {
        [JsonPropertyName("is_locked")]
        public bool IsLocked { get; set; }

        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; }

        [JsonPropertyName("system_message")]
        public SystemMessage SystemMessage { get; set; }

        // On passe en List ici
        [JsonPropertyName("push_notifications")]
        public List<PushNotification> PushNotifications { get; set; } = new List<PushNotification>();

        [JsonPropertyName("server_info")]
        public ServerInfo ServerInfo { get; set; }
    }
    public class SystemMessage
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string Type { get; set; } // upgrade, info, danger
    }
   
    public class PushNotification
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; } // offer, alert, info
        public string Date { get; set; }
    }

    public class ServerInfo
    {
        public string Time { get; set; }
        public string Status { get; set; }
    }
    #endregion
}
