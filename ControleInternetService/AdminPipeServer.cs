using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using ControleInternet.Common;

namespace ControleInternet.Service
{
    internal sealed class AdminPipeServer : IDisposable
    {
        private readonly Func<AdminRequest, AdminResponse> _handler;
        private readonly object _sync = new object();
        private volatile bool _running;
        private bool _firstInstance = true;
        private NamedPipeServerStream _waitingPipe;
        private Thread _acceptThread;

        public AdminPipeServer(Func<AdminRequest, AdminResponse> handler)
        {
            _handler = handler;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "ControleInternet Admin Pipe"
            };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            lock (_sync)
            {
                if (_waitingPipe != null)
                {
                    _waitingPipe.Dispose();
                    _waitingPipe = null;
                }
            }

            if (_acceptThread != null && _acceptThread.IsAlive)
            {
                _acceptThread.Join(2000);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = CreatePipe(_firstInstance);
                    _firstInstance = false;
                    lock (_sync)
                    {
                        if (!_running)
                        {
                            pipe.Dispose();
                            return;
                        }

                        _waitingPipe = pipe;
                    }

                    pipe.WaitForConnection();
                    lock (_sync)
                    {
                        if (ReferenceEquals(_waitingPipe, pipe))
                        {
                            _waitingPipe = null;
                        }
                    }

                    NamedPipeServerStream connectedPipe = pipe;
                    pipe = null;
                    ThreadPool.QueueUserWorkItem(delegate { HandleConnection(connectedPipe); });
                }
                catch (ObjectDisposedException)
                {
                    if (_running)
                    {
                        Logger.Error("O pipe administrativo foi fechado inesperadamente.", null);
                    }
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao aceitar conexão administrativa.", exception);
                    Thread.Sleep(500);
                }
                finally
                {
                    if (pipe != null)
                    {
                        pipe.Dispose();
                    }
                }
            }
        }

        private void HandleConnection(NamedPipeServerStream pipe)
        {
            using (pipe)
            using (Timer timeout = new Timer(
                delegate
                {
                    try
                    {
                        pipe.Dispose();
                    }
                    catch
                    {
                    }
                },
                null,
                10000,
                Timeout.Infinite))
            {
                try
                {
                    AdminRequest request = AdminProtocol.Read<AdminRequest>(pipe);
                    timeout.Change(Timeout.Infinite, Timeout.Infinite);
                    AdminResponse response = _handler(request);
                    AdminProtocol.Write(pipe, response);
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao processar pedido administrativo.", exception);
                    try
                    {
                        AdminProtocol.Write(pipe, AdminResponse.Fail("Não foi possível processar a solicitação."));
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static NamedPipeServerStream CreatePipe(bool firstInstance)
        {
            PipeSecurity security = new PipeSecurity();
            SecurityIdentifier authenticatedUsers = new SecurityIdentifier(
                WellKnownSidType.AuthenticatedUserSid,
                null);
            SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            SecurityIdentifier administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
            SecurityIdentifier network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);

            security.AddAccessRule(new PipeAccessRule(
                network,
                PipeAccessRights.FullControl,
                AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(
                authenticatedUsers,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                system,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                administrators,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return new NamedPipeServerStream(
                Paths.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | (firstInstance ? (PipeOptions)0x00080000 : PipeOptions.None),
                4096,
                4096,
                security);
        }
    }
}
