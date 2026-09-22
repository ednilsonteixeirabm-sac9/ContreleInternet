using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;
using ControleInternet.Common;

namespace ControleInternet.Service
{
    internal sealed class SystemProxy
    {
        private const string PolicyPath =
            @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string InternetSettingsPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;
        private static readonly object Sync = new object();

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr internet,
            int option,
            IntPtr buffer,
            int bufferLength);

        public void Apply()
        {
            lock (Sync)
            {
                EnsureBackup();

                using (RegistryKey policy = Registry.LocalMachine.CreateSubKey(PolicyPath, true))
                using (RegistryKey settings = Registry.LocalMachine.CreateSubKey(InternetSettingsPath, true))
                {
                    if (policy == null || settings == null)
                    {
                        throw new InvalidOperationException("Não foi possível abrir as configurações de proxy.");
                    }

                    policy.SetValue("ProxySettingsPerUser", 0, RegistryValueKind.DWord);
                    settings.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    settings.SetValue(
                        "ProxyServer",
                        "http=127.0.0.1:" + Paths.ProxyPort + ";https=127.0.0.1:" + Paths.ProxyPort,
                        RegistryValueKind.String);
                    settings.SetValue(
                        "ProxyOverride",
                        "localhost;127.*;[::1]",
                        RegistryValueKind.String);
                    DeleteValue(settings, "AutoConfigURL");
                    settings.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                }

                NotifyChange();
                Logger.Info("Proxy WinINet por máquina ativado.");
            }
        }

        public void Restore()
        {
            lock (Sync)
            {
                if (!File.Exists(Paths.ProxyBackupFile))
                {
                    return;
                }

                ProxyBackup backup = LoadBackup();
                using (RegistryKey policy = Registry.LocalMachine.CreateSubKey(PolicyPath, true))
                using (RegistryKey settings = Registry.LocalMachine.CreateSubKey(InternetSettingsPath, true))
                {
                    if (policy == null || settings == null)
                    {
                        throw new InvalidOperationException("Não foi possível restaurar as configurações de proxy.");
                    }

                    RestoreDword(policy, "ProxySettingsPerUser", backup.ProxySettingsPerUser);
                    RestoreDword(settings, "ProxyEnable", backup.ProxyEnable);
                    RestoreString(settings, "ProxyServer", backup.ProxyServer);
                    RestoreString(settings, "ProxyOverride", backup.ProxyOverride);
                    RestoreString(settings, "AutoConfigURL", backup.AutoConfigUrl);
                    RestoreDword(settings, "AutoDetect", backup.AutoDetect);
                }

                NotifyChange();
                File.Delete(Paths.ProxyBackupFile);
                Logger.Info("Configuração original de proxy restaurada.");
            }
        }

        private static void EnsureBackup()
        {
            if (File.Exists(Paths.ProxyBackupFile))
            {
                return;
            }

            Directory.CreateDirectory(Paths.DataDirectory);
            ProxyBackup backup = new ProxyBackup();
            using (RegistryKey policy = Registry.LocalMachine.OpenSubKey(PolicyPath, false))
            using (RegistryKey settings = Registry.LocalMachine.OpenSubKey(InternetSettingsPath, false))
            {
                backup.ProxySettingsPerUser = ReadDword(policy, "ProxySettingsPerUser");
                backup.ProxyEnable = ReadDword(settings, "ProxyEnable");
                backup.ProxyServer = ReadString(settings, "ProxyServer");
                backup.ProxyOverride = ReadString(settings, "ProxyOverride");
                backup.AutoConfigUrl = ReadString(settings, "AutoConfigURL");
                backup.AutoDetect = ReadDword(settings, "AutoDetect");
            }

            string temporary = Paths.ProxyBackupFile + ".tmp";
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProxyBackup));
            using (FileStream stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                serializer.WriteObject(stream, backup);
                stream.Flush();
            }

            File.Move(temporary, Paths.ProxyBackupFile);
        }

        private static ProxyBackup LoadBackup()
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProxyBackup));
            using (FileStream stream = File.OpenRead(Paths.ProxyBackupFile))
            {
                return (ProxyBackup)serializer.ReadObject(stream);
            }
        }

        private static RegistryDword ReadDword(RegistryKey key, string name)
        {
            object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                return new RegistryDword();
            }

            return new RegistryDword { Exists = true, Value = Convert.ToInt32(value) };
        }

        private static RegistryString ReadString(RegistryKey key, string name)
        {
            object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                return new RegistryString();
            }

            return new RegistryString { Exists = true, Value = Convert.ToString(value) };
        }

        private static void RestoreDword(RegistryKey key, string name, RegistryDword backup)
        {
            if (backup != null && backup.Exists)
            {
                key.SetValue(name, backup.Value, RegistryValueKind.DWord);
            }
            else
            {
                DeleteValue(key, name);
            }
        }

        private static void RestoreString(RegistryKey key, string name, RegistryString backup)
        {
            if (backup != null && backup.Exists)
            {
                key.SetValue(name, backup.Value ?? string.Empty, RegistryValueKind.String);
            }
            else
            {
                DeleteValue(key, name);
            }
        }

        private static void DeleteValue(RegistryKey key, string name)
        {
            key.DeleteValue(name, false);
        }

        private static void NotifyChange()
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }

        [DataContract]
        private sealed class ProxyBackup
        {
            [DataMember(Order = 1)]
            public RegistryDword ProxySettingsPerUser { get; set; }

            [DataMember(Order = 2)]
            public RegistryDword ProxyEnable { get; set; }

            [DataMember(Order = 3)]
            public RegistryString ProxyServer { get; set; }

            [DataMember(Order = 4)]
            public RegistryString ProxyOverride { get; set; }

            [DataMember(Order = 5)]
            public RegistryString AutoConfigUrl { get; set; }

            [DataMember(Order = 6)]
            public RegistryDword AutoDetect { get; set; }
        }

        [DataContract]
        private sealed class RegistryDword
        {
            [DataMember(Order = 1)]
            public bool Exists { get; set; }

            [DataMember(Order = 2)]
            public int Value { get; set; }
        }

        [DataContract]
        private sealed class RegistryString
        {
            [DataMember(Order = 1)]
            public bool Exists { get; set; }

            [DataMember(Order = 2)]
            public string Value { get; set; }
        }
    }
}
