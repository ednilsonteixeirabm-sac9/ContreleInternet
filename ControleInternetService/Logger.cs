using System;
using System.IO;
using ControleInternet.Common;

namespace ControleInternet.Service
{
    internal static class Logger
    {
        private static readonly object Sync = new object();

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERRO", message + (exception == null ? string.Empty : " " + exception));
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Paths.DataDirectory);
                    File.AppendAllText(
                        Paths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message
                            + Environment.NewLine);
                }
            }
            catch
            {
                // O serviço não deve parar somente porque o log não pôde ser gravado.
            }
        }
    }
}
