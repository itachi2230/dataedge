using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace backtest.Services
{
    /// <summary>
    /// Correction du comportement « maximiser » des fenêtres sans bordure
    /// (WindowStyle="None", avec ou sans WindowChrome).
    ///
    /// Problèmes natifs :
    ///  - Fenêtre WS_POPUP (WindowStyle="None" seul) : maximiser couvre TOUT
    ///    l'écran, taskbar incluse.
    ///  - Fenêtre avec WindowChrome (GlassFrameThickness="0") : maximiser
    ///    agrandit la fenêtre à la zone de travail + l'épaisseur de la bordure
    ///    système → le contenu est coupé d'environ 8 px sur les 4 bords.
    ///
    /// Solution : intercepter WM_GETMINMAXINFO pour forcer la taille maximisée
    /// à EXACTEMENT la zone de travail du moniteur concerné (taskbar visible,
    /// multi-écrans et DPI gérés). La zone client devient alors égale à la zone
    /// de travail : plus rien n'est masqué aux bords.
    ///
    /// Usage : appeler MaximizeHelper.Hook(this) dans OnSourceInitialized.
    /// </summary>
    public static class MaximizeHelper
    {
        public static void Hook(Window window)
        {
            var source = (HwndSource)PresentationSource.FromVisual(window);
            if (source != null)
                source.AddHook(WndProc);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg != WM_GETMINMAXINFO)
                return IntPtr.Zero;

            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return IntPtr.Zero;

            MONITORINFO info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info))
                return IntPtr.Zero;

            // Taille maximisée = zone de travail exacte du moniteur (taskbar exclue)
            mmi.ptMaxPosition.x = Math.Abs(info.rcWork.Left - info.rcMonitor.Left);
            mmi.ptMaxPosition.y = Math.Abs(info.rcWork.Top - info.rcMonitor.Top);
            mmi.ptMaxSize.x = Math.Abs(info.rcWork.Right - info.rcWork.Left);
            mmi.ptMaxSize.y = Math.Abs(info.rcWork.Bottom - info.rcWork.Top);
            // Tirer les bords reste possible jusqu'à couvrir tout l'écran
            mmi.ptMaxTrackSize.x = Math.Abs(info.rcMonitor.Right - info.rcMonitor.Left);
            mmi.ptMaxTrackSize.y = Math.Abs(info.rcMonitor.Bottom - info.rcMonitor.Top);

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
            return IntPtr.Zero;
        }

        #region Win32
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        #endregion
    }
}
