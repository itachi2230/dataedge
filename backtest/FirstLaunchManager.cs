using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace backtest
{
    public static class FirstLaunchManager
    {
        private const string FirstLaunchFile = "firstLaunch.txt";
        private const string SpotlightPendingFile = "spotlight_pending.txt";

        public static bool IsFirstLaunch()
        {
            if (!File.Exists(FirstLaunchFile))
            {
                File.WriteAllText(FirstLaunchFile, "Launched");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Marque que le spotlight tour est en attente (appelé par Demo avant d'ouvrir MainWindow).
        /// </summary>
        public static void MarkSpotlightPending()
        {
            File.WriteAllText(SpotlightPendingFile, "pending");
        }

        /// <summary>
        /// Vérifie si le spotlight tour doit être affiché.
        /// </summary>
        public static bool IsSpotlightPending()
        {
            return File.Exists(SpotlightPendingFile);
        }

        /// <summary>
        /// Efface le flag spotlight (appelé quand le tour est terminé ou ignoré).
        /// </summary>
        public static void ClearSpotlightPending()
        {
            if (File.Exists(SpotlightPendingFile))
                File.Delete(SpotlightPendingFile);
        }
    }
}
