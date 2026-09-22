using System;
using System.Globalization;
using System.Net;

namespace ControleInternet.Common
{
    public static class DomainName
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("Informe um domínio.");
            }

            string domain = value.Trim().TrimEnd('.');
            if (domain.IndexOf("://", StringComparison.Ordinal) >= 0
                || domain.IndexOf('/') >= 0
                || domain.IndexOf('\\') >= 0
                || domain.IndexOf('@') >= 0
                || domain.IndexOf(':') >= 0)
            {
                throw new FormatException("Informe somente o domínio, sem protocolo, porta ou caminho.");
            }

            try
            {
                domain = new IdnMapping().GetAscii(domain);
            }
            catch (ArgumentException)
            {
                throw new FormatException("O domínio informado não é válido.");
            }

            domain = domain.ToLowerInvariant();
            if (domain.Length > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns)
            {
                throw new FormatException("O domínio informado não é válido.");
            }

            IPAddress address;
            if (IPAddress.TryParse(domain, out address))
            {
                throw new FormatException("Informe um domínio, não um endereço IP.");
            }

            return domain;
        }

        public static bool IsAllowed(string host, System.Collections.Generic.IEnumerable<string> allowedDomains)
        {
            if (string.IsNullOrWhiteSpace(host) || allowedDomains == null)
            {
                return false;
            }

            string normalizedHost;
            try
            {
                normalizedHost = Normalize(host);
            }
            catch (FormatException)
            {
                return false;
            }

            foreach (string value in allowedDomains)
            {
                string domain;
                try
                {
                    domain = Normalize(value);
                }
                catch (FormatException)
                {
                    continue;
                }

                if (string.Equals(normalizedHost, domain, StringComparison.OrdinalIgnoreCase)
                    || normalizedHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
