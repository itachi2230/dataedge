using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace backtest.Services
{
    /// <summary>
    /// Stocke la dernière position de replay (timestamp UNIX) par stratégie+paire+timeframe.
    /// Les données sont persistées dans %LOCALAPPDATA%/DataEdge/replay_positions.json
    /// </summary>
    public static class ReplayPositionStore
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DataEdge",
            "replay_positions.json"
        );

        private static Dictionary<string, long> _positions = new Dictionary<string, long>();
        private static bool _loaded = false;
        private static readonly object _lock = new object();

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                if (File.Exists(FilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(FilePath);
                        _positions = JsonConvert.DeserializeObject<Dictionary<string, long>>(json) 
                                     ?? new Dictionary<string, long>();
                    }
                    catch 
                    { 
                        _positions = new Dictionary<string, long>(); 
                    }
                }
            }
        }

        /// <summary>
        /// Construit la clé unique pour une stratégie, une paire et un timeframe.
        /// Format : STRATEGIE_SYMBOL_TIMEFRAME
        /// </summary>
        public static string BuildKey(string strategy, string symbol, string timeframe)
        {
            return $"{strategy}_{symbol}_{timeframe}".ToUpperInvariant();
        }

        /// <summary>
        /// Retourne le timestamp UNIX de la dernière position replay pour cette stratégie/paire/timeframe,
        /// ou null si aucun replay n'a jamais été fait.
        /// </summary>
        public static long? Get(string strategy, string symbol, string timeframe)
        {
            EnsureLoaded();
            var key = BuildKey(strategy, symbol, timeframe);
            lock (_lock)
            {
                if (_positions.TryGetValue(key, out var ts))
                    return ts;
            }
            return null;
        }

        /// <summary>
        /// Sauvegarde la position replay (timestamp UNIX) pour une stratégie/paire/timeframe.
        /// </summary>
        public static void Set(string strategy, string symbol, string timeframe, long timestamp)
        {
            EnsureLoaded();
            var key = BuildKey(strategy, symbol, timeframe);
            lock (_lock)
            {
                _positions[key] = timestamp;
                SaveToDisk();
            }
        }

        /// <summary>
        /// Supprime la position replay sauvegardée pour une stratégie/paire/timeframe.
        /// </summary>
        public static void Remove(string strategy, string symbol, string timeframe)
        {
            EnsureLoaded();
            var key = BuildKey(strategy, symbol, timeframe);
            lock (_lock)
            {
                _positions.Remove(key);
                SaveToDisk();
            }
        }

        private static void SaveToDisk()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_positions, Formatting.Indented));
            }
            catch 
            { 
                // Échec silencieux : la persistence n'est pas critique
            }
        }
    }
}