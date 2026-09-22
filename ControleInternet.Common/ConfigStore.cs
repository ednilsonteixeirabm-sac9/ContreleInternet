using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace ControleInternet.Common
{
    public sealed class ConfigStore
    {
        private readonly string _path;

        public ConfigStore()
            : this(Paths.ConfigFile)
        {
        }

        public ConfigStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Caminho de configuração inválido.", "path");
            }

            _path = path;
        }

        public AppConfig Load()
        {
            if (!File.Exists(_path))
            {
                return new AppConfig();
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppConfig));
            using (FileStream stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                AppConfig config = (AppConfig)serializer.ReadObject(stream);
                if (config == null)
                {
                    throw new InvalidDataException("O arquivo de configuração está vazio.");
                }

                config.Normalize();
                return config;
            }
        }

        public void Save(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            config.Normalize();
            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);

            string temporary = _path + ".tmp";
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppConfig));
            using (FileStream stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                serializer.WriteObject(stream, config);
                stream.Flush();
            }

            if (File.Exists(_path))
            {
                string backup = _path + ".previous";
                File.Replace(temporary, _path, backup, true);
                TryDelete(backup);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
