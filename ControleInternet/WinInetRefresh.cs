using System;
using System.Runtime.InteropServices;

namespace ControleInternet
{
    internal static class WinInetRefresh
    {
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr internet,
            int option,
            IntPtr buffer,
            int bufferLength);

        public static void NotifyCurrentSession()
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
    }
}
