using System;
using System.IO;
using System.IO.Pipes;
using ControleInternet.Common;

namespace ControleInternet
{
    internal sealed class AdminClient
    {
        public AdminResponse Send(AdminRequest request)
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(
                    ".",
                    Paths.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.None))
                {
                    pipe.Connect(3000);
                    pipe.ReadMode = PipeTransmissionMode.Byte;
                    AdminProtocol.Write(pipe, request);
                    return AdminProtocol.Read<AdminResponse>(pipe);
                }
            }
            catch (TimeoutException)
            {
                throw new IOException("O serviço ControleInternet não respondeu. Verifique se ele está em execução.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new IOException("O serviço recusou a conexão administrativa.");
            }
        }

        public AdminResponse Status()
        {
            return Send(new AdminRequest { Operation = AdminProtocol.StatusOperation });
        }

        public AdminResponse Initialize(string password)
        {
            return Send(new AdminRequest
            {
                Operation = AdminProtocol.InitializeOperation,
                Password = password
            });
        }

        public AdminResponse Authenticate(string password)
        {
            return Send(new AdminRequest
            {
                Operation = AdminProtocol.AuthenticateOperation,
                Password = password
            });
        }

        public AdminResponse Save(string password, AppConfig config)
        {
            return Send(new AdminRequest
            {
                Operation = AdminProtocol.SaveOperation,
                Password = password,
                Config = config
            });
        }
    }
}
