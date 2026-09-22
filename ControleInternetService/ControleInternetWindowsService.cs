using System;
using System.IO;
using System.ServiceProcess;
using ControleInternet.Common;

namespace ControleInternet.Service
{
    internal sealed class ControleInternetWindowsService : ServiceBase
    {
        private readonly object _stateSync = new object();
        private readonly ConfigStore _store = new ConfigStore();
        private readonly SystemProxy _systemProxy = new SystemProxy();
        private readonly LocalHttpProxy _localProxy = new LocalHttpProxy();
        private AdminPipeServer _adminServer;
        private AppConfig _config;

        public ControleInternetWindowsService()
        {
            ServiceName = Paths.ServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            _config = LoadSafely();
            ApplyState(_config);

            _adminServer = new AdminPipeServer(HandleAdminRequest);
            _adminServer.Start();
            Logger.Info("Serviço iniciado.");
        }

        protected override void OnStop()
        {
            if (_adminServer != null)
            {
                _adminServer.Stop();
                _adminServer = null;
            }

            _localProxy.Stop();
            Logger.Info("Serviço parado; configuração do sistema preservada para comportamento fail-closed.");
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }

        private AppConfig LoadSafely()
        {
            try
            {
                if (!File.Exists(Paths.ConfigFile) && File.Exists(Paths.ProxyBackupFile))
                {
                    Logger.Info("Configuração ausente com proxy ativo; mantendo bloqueio fail-closed.");
                    return new AppConfig { BlockAllSites = true, AllowListedSites = false };
                }

                return _store.Load();
            }
            catch (Exception exception)
            {
                Logger.Error("Configuração inválida; adotando estado seguro.", exception);
                return new AppConfig
                {
                    BlockAllSites = File.Exists(Paths.ProxyBackupFile),
                    AllowListedSites = false
                };
            }
        }

        private AdminResponse HandleAdminRequest(AdminRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Operation))
            {
                return AdminResponse.Fail("Solicitação administrativa inválida.");
            }

            lock (_stateSync)
            {
                if (string.Equals(request.Operation, AdminProtocol.StatusOperation, StringComparison.Ordinal))
                {
                    return new AdminResponse { Success = true, Initialized = _config.HasPassword };
                }

                if (string.Equals(request.Operation, AdminProtocol.InitializeOperation, StringComparison.Ordinal))
                {
                    return InitializePassword(request.Password);
                }

                if (!_config.HasPassword || !PasswordHasher.Verify(_config, request.Password))
                {
                    return AdminResponse.Fail("Senha incorreta.");
                }

                if (string.Equals(request.Operation, AdminProtocol.AuthenticateOperation, StringComparison.Ordinal))
                {
                    return new AdminResponse
                    {
                        Success = true,
                        Initialized = true,
                        Config = _config.WithoutPassword()
                    };
                }

                if (string.Equals(request.Operation, AdminProtocol.SaveOperation, StringComparison.Ordinal))
                {
                    return SaveConfiguration(request.Config);
                }

                return AdminResponse.Fail("Operação administrativa desconhecida.");
            }
        }

        private AdminResponse InitializePassword(string password)
        {
            if (_config.HasPassword)
            {
                return AdminResponse.Fail("A senha do Controle de Internet já foi definida.");
            }

            try
            {
                PasswordHasher.SetPassword(_config, password);
                _store.Save(_config);
                Logger.Info("Senha administrativa definida.");
                return new AdminResponse
                {
                    Success = true,
                    Initialized = true,
                    Config = _config.WithoutPassword()
                };
            }
            catch (Exception exception)
            {
                Logger.Error("Não foi possível definir a senha.", exception);
                return AdminResponse.Fail(exception.Message);
            }
        }

        private AdminResponse SaveConfiguration(AppConfig requested)
        {
            if (requested == null)
            {
                return AdminResponse.Fail("Configuração não informada.");
            }

            AppConfig previous = _config.Clone();
            AppConfig next = requested.Clone();
            next.PasswordSalt = previous.PasswordSalt;
            next.PasswordHash = previous.PasswordHash;
            next.PasswordIterations = previous.PasswordIterations;

            try
            {
                next.Normalize();
                _store.Save(next);
                ApplyState(next);
                _config = next;
                Logger.Info("Nova configuração aplicada.");
                return new AdminResponse
                {
                    Success = true,
                    Initialized = true,
                    Config = next.WithoutPassword()
                };
            }
            catch (Exception exception)
            {
                Logger.Error("Falha ao aplicar configuração; restaurando estado anterior.", exception);
                try
                {
                    _store.Save(previous);
                    ApplyState(previous);
                    _config = previous;
                }
                catch (Exception rollbackException)
                {
                    Logger.Error("Falha ao restaurar o estado anterior.", rollbackException);
                }

                return AdminResponse.Fail("Não foi possível aplicar a configuração: " + exception.Message);
            }
        }

        private void ApplyState(AppConfig config)
        {
            if (config.BlockAllSites)
            {
                _localProxy.Start(config);
                _localProxy.Update(config);
                _systemProxy.Apply();
            }
            else
            {
                _systemProxy.Restore();
                _localProxy.Stop();
            }
        }
    }
}
