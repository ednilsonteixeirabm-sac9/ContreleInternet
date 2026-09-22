using System;
using System.ComponentModel;
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
        private const string ConnectionsPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionPerConnectionOption = 75;
        private const int InternetPerConnFlags = 1;
        private const int InternetPerConnProxyServer = 2;
        private const int InternetPerConnProxyBypass = 3;
        private const int InternetPerConnAutoConfigUrl = 4;
        private const int ProxyTypeProxy = 0x00000002;
        private static readonly object Sync = new object();

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
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

                ApplyPerConnectionSettings();
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
                using (RegistryKey connections = Registry.LocalMachine.CreateSubKey(ConnectionsPath, true))
                {
                    if (policy == null || settings == null || connections == null)
                    {
                        throw new InvalidOperationException("Não foi possível restaurar as configurações de proxy.");
                    }

                    RestoreDword(policy, "ProxySettingsPerUser", backup.ProxySettingsPerUser);
                    RestoreDword(settings, "ProxyEnable", backup.ProxyEnable);
                    RestoreString(settings, "ProxyServer", backup.ProxyServer);
                    RestoreString(settings, "ProxyOverride", backup.ProxyOverride);
                    RestoreString(settings, "AutoConfigURL", backup.AutoConfigUrl);
                    RestoreDword(settings, "AutoDetect", backup.AutoDetect);
                    RestoreBinary(
                        connections,
                        "DefaultConnectionSettings",
                        backup.DefaultConnectionSettings);
                    RestoreBinary(
                        connections,
                        "SavedLegacySettings",
                        backup.SavedLegacySettings);
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
            using (RegistryKey connections = Registry.LocalMachine.OpenSubKey(ConnectionsPath, false))
            {
                backup.ProxySettingsPerUser = ReadDword(policy, "ProxySettingsPerUser");
                backup.ProxyEnable = ReadDword(settings, "ProxyEnable");
                backup.ProxyServer = ReadString(settings, "ProxyServer");
                backup.ProxyOverride = ReadString(settings, "ProxyOverride");
                backup.AutoConfigUrl = ReadString(settings, "AutoConfigURL");
                backup.AutoDetect = ReadDword(settings, "AutoDetect");
                backup.DefaultConnectionSettings = ReadBinary(connections, "DefaultConnectionSettings");
                backup.SavedLegacySettings = ReadBinary(connections, "SavedLegacySettings");
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

            return new RegistryString
            {
                Exists = true,
                Value = Convert.ToString(value),
                Kind = (int)key.GetValueKind(name)
            };
        }

        private static RegistryBinary ReadBinary(RegistryKey key, string name)
        {
            object value = key == null ? null : key.GetValue(name, null);
            byte[] bytes = value as byte[];
            return bytes == null
                ? new RegistryBinary()
                : new RegistryBinary { Exists = true, Value = bytes };
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
                RegistryValueKind kind = backup.Kind == (int)RegistryValueKind.ExpandString
                    ? RegistryValueKind.ExpandString
                    : RegistryValueKind.String;
                key.SetValue(name, backup.Value ?? string.Empty, kind);
            }
            else
            {
                DeleteValue(key, name);
            }
        }

        private static void RestoreBinary(RegistryKey key, string name, RegistryBinary backup)
        {
            if (backup != null && backup.Exists)
            {
                key.SetValue(name, backup.Value ?? new byte[0], RegistryValueKind.Binary);
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

        private static void ApplyPerConnectionSettings()
        {
            string proxy = "http=127.0.0.1:" + Paths.ProxyPort
                + ";https=127.0.0.1:" + Paths.ProxyPort;
            string bypass = "localhost;127.*;[::1]";
            IntPtr proxyPointer = Marshal.StringToHGlobalAuto(proxy);
            IntPtr bypassPointer = Marshal.StringToHGlobalAuto(bypass);
            IntPtr optionsPointer = IntPtr.Zero;
            try
            {
                InternetPerConnectionOption[] options =
                {
                    CreateDwordOption(InternetPerConnFlags, ProxyTypeProxy),
                    CreatePointerOption(InternetPerConnProxyServer, proxyPointer),
                    CreatePointerOption(InternetPerConnProxyBypass, bypassPointer),
                    CreatePointerOption(InternetPerConnAutoConfigUrl, IntPtr.Zero)
                };

                int optionSize = Marshal.SizeOf(typeof(InternetPerConnectionOption));
                optionsPointer = Marshal.AllocCoTaskMem(optionSize * options.Length);
                for (int index = 0; index < options.Length; index++)
                {
                    Marshal.StructureToPtr(
                        options[index],
                        new IntPtr(optionsPointer.ToInt64() + (long)index * optionSize),
                        false);
                }

                InternetPerConnectionOptionList list = new InternetPerConnectionOptionList
                {
                    Size = Marshal.SizeOf(typeof(InternetPerConnectionOptionList)),
                    Connection = IntPtr.Zero,
                    OptionCount = options.Length,
                    OptionError = 0,
                    Options = optionsPointer
                };

                IntPtr listPointer = Marshal.AllocCoTaskMem(list.Size);
                try
                {
                    Marshal.StructureToPtr(list, listPointer, false);
                    if (!InternetSetOption(
                            IntPtr.Zero,
                            InternetOptionPerConnectionOption,
                            listPointer,
                            list.Size))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Não foi possível desativar PAC/WPAD e aplicar o proxy manual.");
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(listPointer);
                }
            }
            finally
            {
                if (optionsPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(optionsPointer);
                }

                Marshal.FreeHGlobal(proxyPointer);
                Marshal.FreeHGlobal(bypassPointer);
            }
        }

        private static InternetPerConnectionOption CreateDwordOption(int option, int value)
        {
            return new InternetPerConnectionOption
            {
                Option = option,
                Value = new InternetPerConnectionOptionValue { Dword = value }
            };
        }

        private static InternetPerConnectionOption CreatePointerOption(int option, IntPtr value)
        {
            return new InternetPerConnectionOption
            {
                Option = option,
                Value = new InternetPerConnectionOptionValue { Pointer = value }
            };
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

            [DataMember(Order = 7)]
            public RegistryBinary DefaultConnectionSettings { get; set; }

            [DataMember(Order = 8)]
            public RegistryBinary SavedLegacySettings { get; set; }
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

            [DataMember(Order = 3)]
            public int Kind { get; set; }
        }

        [DataContract]
        private sealed class RegistryBinary
        {
            [DataMember(Order = 1)]
            public bool Exists { get; set; }

            [DataMember(Order = 2)]
            public byte[] Value { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InternetPerConnectionOptionList
        {
            public int Size;
            public IntPtr Connection;
            public int OptionCount;
            public int OptionError;
            public IntPtr Options;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InternetPerConnectionOption
        {
            public int Option;
            public InternetPerConnectionOptionValue Value;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InternetPerConnectionOptionValue
        {
            [FieldOffset(0)]
            public int Dword;

            [FieldOffset(0)]
            public IntPtr Pointer;
        }
    }
}
