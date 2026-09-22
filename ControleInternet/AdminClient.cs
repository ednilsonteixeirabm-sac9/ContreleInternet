using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
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
                    VerifyServiceIdentity(pipe);
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

        private static void VerifyServiceIdentity(NamedPipeClientStream pipe)
        {
            uint processId;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out processId))
            {
                throw new IOException("Não foi possível verificar a identidade do serviço.");
            }

            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                throw new IOException("Não foi possível verificar o processo do serviço.");
            }

            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TokenQuery, out token))
                {
                    throw new IOException("Não foi possível verificar o usuário do serviço.");
                }

                int required;
                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out required);
                IntPtr buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, buffer, required, out required))
                    {
                        throw new IOException("Não foi possível ler a identidade do serviço.");
                    }

                    TokenUserInfo tokenUser = (TokenUserInfo)Marshal.PtrToStructure(
                        buffer,
                        typeof(TokenUserInfo));
                    SecurityIdentifier actual = new SecurityIdentifier(tokenUser.User.Sid);
                    SecurityIdentifier expected = new SecurityIdentifier(
                        WellKnownSidType.LocalSystemSid,
                        null);
                    if (!actual.Equals(expected))
                    {
                        throw new IOException("A conexão administrativa não pertence ao serviço LocalSystem.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }

                CloseHandle(process);
            }
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenUser = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            public IntPtr Sid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenUserInfo
        {
            public SidAndAttributes User;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(
            SafePipeHandle pipe,
            out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr token,
            int informationClass,
            IntPtr information,
            int informationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
