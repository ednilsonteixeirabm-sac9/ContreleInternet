using System;
using System.ServiceProcess;

namespace ControleInternet.Service
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1
                && string.Equals(args[0], "--restore-proxy", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    new SystemProxy().Restore();
                    return 0;
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha na restauração administrativa do proxy.", exception);
                    return 1;
                }
            }

            ServiceBase.Run(new ServiceBase[] { new ControleInternetWindowsService() });
            return 0;
        }
    }
}
