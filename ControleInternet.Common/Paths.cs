using System;
using System.IO;

namespace ControleInternet.Common
{
    public static class Paths
    {
        public const string ServiceName = "ControleInternetService";
        public const string PipeName = "ControleInternet.Admin";
        public const int ProxyPort = 18754;

        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ControleInternet");

        public static readonly string ConfigFile = Path.Combine(DataDirectory, "config.json");
        public static readonly string ProxyBackupFile = Path.Combine(DataDirectory, "windows-backup.json");
        public static readonly string LogFile = Path.Combine(DataDirectory, "service.log");
    }
}
