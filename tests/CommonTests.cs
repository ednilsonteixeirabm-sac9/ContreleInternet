using System;
using System.Collections.Generic;
using System.IO;
using ControleInternet.Common;

namespace ControleInternet.Tests
{
    internal static class CommonTests
    {
        private static int _failures;

        private static int Main()
        {
            Run("Domínio exato e subdomínios", DomainBoundaries);
            Run("Normalização IDN", InternationalDomain);
            Run("Hash e validação da senha", PasswordHash);
            Run("Persistência JSON", ConfigurationRoundTrip);
            Run("Protocolo administrativo", ProtocolRoundTrip);

            Console.WriteLine(_failures == 0 ? "Todos os testes passaram." : _failures + " teste(s) falharam.");
            return _failures == 0 ? 0 : 1;
        }

        private static void DomainBoundaries()
        {
            string[] allowed = { "gov.br" };
            Assert(DomainName.IsAllowed("gov.br", allowed), "domínio exato");
            Assert(DomainName.IsAllowed("www.gov.br", allowed), "subdomínio");
            Assert(DomainName.IsAllowed("login.servicos.gov.br", allowed), "subdomínio aninhado");
            Assert(!DomainName.IsAllowed("outrogov.br", allowed), "prefixo semelhante");
            Assert(!DomainName.IsAllowed("meugov.br", allowed), "sufixo sem limite DNS");
            Assert(!DomainName.IsAllowed("gov.br.outrosite.com", allowed), "domínio como prefixo");
            Assert(!DomainName.IsAllowed("127.0.0.1", allowed), "endereço IP");
        }

        private static void InternationalDomain()
        {
            AssertEqual("xn--caf-dma.com.br", DomainName.Normalize("CAFÉ.com.br."), "punycode");
        }

        private static void PasswordHash()
        {
            AppConfig config = new AppConfig();
            PasswordHasher.SetPassword(config, "senha-segura");
            Assert(config.PasswordHash != "senha-segura", "senha não pode ser armazenada em texto puro");
            Assert(PasswordHasher.Verify(config, "senha-segura"), "senha correta");
            Assert(!PasswordHasher.Verify(config, "senha-errada"), "senha incorreta");
        }

        private static void ConfigurationRoundTrip()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ControleInternet.Tests." + Guid.NewGuid());
            string path = Path.Combine(directory, "config.json");
            try
            {
                ConfigStore store = new ConfigStore(path);
                AppConfig expected = new AppConfig
                {
                    BlockAllSites = true,
                    AllowListedSites = true,
                    AllowedDomains = new List<string> { "WWW.GOV.BR", "gov.br", "gov.br" }
                };
                PasswordHasher.SetPassword(expected, "teste-123");
                store.Save(expected);

                AppConfig actual = store.Load();
                Assert(actual.BlockAllSites, "blockAllSites");
                Assert(actual.AllowListedSites, "allowListedSites");
                AssertEqual(2, actual.AllowedDomains.Count, "remoção de duplicatas");
                Assert(PasswordHasher.Verify(actual, "teste-123"), "hash persistido");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void ProtocolRoundTrip()
        {
            AdminRequest request = new AdminRequest
            {
                Operation = AdminProtocol.SaveOperation,
                Password = "local",
                Config = new AppConfig { AllowedDomains = new List<string> { "gov.br" } }
            };

            using (MemoryStream stream = new MemoryStream())
            {
                AdminProtocol.Write(stream, request);
                stream.Position = 0;
                AdminRequest copy = AdminProtocol.Read<AdminRequest>(stream);
                AssertEqual(AdminProtocol.SaveOperation, copy.Operation, "operação");
                AssertEqual("local", copy.Password, "senha no IPC");
                AssertEqual("gov.br", copy.Config.AllowedDomains[0], "configuração no IPC");
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("[OK] " + name);
            }
            catch (Exception exception)
            {
                _failures++;
                Console.WriteLine("[FALHA] " + name + ": " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + " — esperado: " + expected + "; recebido: " + actual);
            }
        }
    }
}
