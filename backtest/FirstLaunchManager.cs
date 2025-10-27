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

        public static bool IsFirstLaunch()
        {
            if (!File.Exists(FirstLaunchFile))
            {
                File.WriteAllText(FirstLaunchFile, "Launched");
                return true;
            }
            return false;
        }
    }
}
