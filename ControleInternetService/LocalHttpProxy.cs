using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ControleInternet.Common;

namespace ControleInternet.Service
{
    internal sealed class LocalHttpProxy
    {
        private const int MaximumHeaderSize = 64 * 1024;
        private const int IoTimeoutMilliseconds = 30000;
        private readonly object _sync = new object();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private AppConfig _config = new AppConfig();

        public void Start(AppConfig config)
        {
            Update(config);
            lock (_sync)
            {
                if (_running)
                {
                    return;
                }

                _listener = new TcpListener(IPAddress.Loopback, Paths.ProxyPort);
                _listener.Start(100);
                _running = true;
                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "ControleInternet HTTP Proxy"
                };
                _acceptThread.Start();
                Logger.Info("Proxy HTTP local iniciado em 127.0.0.1:" + Paths.ProxyPort + ".");
            }
        }

        public void Update(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            lock (_sync)
            {
                _config = config.Clone();
            }
        }

        public void Stop()
        {
            Thread acceptThread;
            lock (_sync)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener = null;
                }

                acceptThread = _acceptThread;
                _acceptThread = null;
            }

            if (acceptThread != null && acceptThread.IsAlive)
            {
                acceptThread.Join(2000);
            }

            Logger.Info("Proxy HTTP local parado.");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { HandleClient(client); });
                }
                catch (SocketException)
                {
                    if (_running)
                    {
                        Logger.Error("Falha ao aceitar conexão no proxy local.", null);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha inesperada no listener do proxy.", exception);
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = IoTimeoutMilliseconds;
                client.SendTimeout = IoTimeoutMilliseconds;

                try
                {
                    NetworkStream clientStream = client.GetStream();
                    HeaderReadResult request = ReadHeader(clientStream);
                    ParsedRequest parsed = ParseRequest(request.Header);

                    if (!IsAllowed(parsed.Host))
                    {
                        WriteBlocked(clientStream);
                        return;
                    }

                    if (parsed.IsConnect)
                    {
                        HandleConnect(clientStream, request.Remainder, parsed);
                    }
                    else
                    {
                        HandleHttp(clientStream, request.Remainder, parsed);
                    }
                }
                catch (InvalidDataException exception)
                {
                    Logger.Info("Pedido recusado pelo proxy: " + exception.Message);
                    TryWriteBadRequest(client);
                }
                catch (IOException)
                {
                    // Navegadores cancelam conexões durante navegação normal.
                }
                catch (SocketException)
                {
                    // Falha de rede do destino; o navegador exibirá seu erro normal.
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao processar conexão do proxy.", exception);
                }
            }
        }

        private bool IsAllowed(string host)
        {
            AppConfig config;
            lock (_sync)
            {
                config = _config;
            }

            if (!config.BlockAllSites)
            {
                return true;
            }

            return config.AllowListedSites && DomainName.IsAllowed(host, config.AllowedDomains);
        }

        private static void HandleConnect(
            NetworkStream clientStream,
            byte[] remainder,
            ParsedRequest request)
        {
            using (TcpClient destination = Connect(request.Host, request.Port))
            {
                NetworkStream destinationStream = destination.GetStream();
                WriteAscii(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n");
                if (remainder.Length > 0)
                {
                    destinationStream.Write(remainder, 0, remainder.Length);
                }

                Relay(clientStream, destinationStream);
            }
        }

        private static void HandleHttp(
            NetworkStream clientStream,
            byte[] remainder,
            ParsedRequest request)
        {
            using (TcpClient destination = Connect(request.Host, request.Port))
            {
                NetworkStream destinationStream = destination.GetStream();
                byte[] forwardedHeader = Encoding.ASCII.GetBytes(request.ForwardedHeader);
                destinationStream.Write(forwardedHeader, 0, forwardedHeader.Length);
                if (remainder.Length > 0)
                {
                    destinationStream.Write(remainder, 0, remainder.Length);
                }

                Relay(clientStream, destinationStream);
            }
        }

        private static TcpClient Connect(string host, int port)
        {
            TcpClient destination = new TcpClient();
            destination.ReceiveTimeout = IoTimeoutMilliseconds;
            destination.SendTimeout = IoTimeoutMilliseconds;

            IAsyncResult result = destination.BeginConnect(host, port, null, null);
            if (!result.AsyncWaitHandle.WaitOne(10000))
            {
                destination.Close();
                throw new IOException("Tempo esgotado ao conectar ao destino.");
            }

            try
            {
                destination.EndConnect(result);
                return destination;
            }
            catch
            {
                destination.Close();
                throw;
            }
            finally
            {
                result.AsyncWaitHandle.Close();
            }
        }

        private static void Relay(NetworkStream left, NetworkStream right)
        {
            Task leftToRight = left.CopyToAsync(right);
            Task rightToLeft = right.CopyToAsync(left);
            Task.WaitAny(leftToRight, rightToLeft);
        }

        private static HeaderReadResult ReadHeader(NetworkStream stream)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[4096];
                while (buffer.Length <= MaximumHeaderSize)
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                    {
                        throw new InvalidDataException("Cabeçalho HTTP incompleto.");
                    }

                    buffer.Write(chunk, 0, read);
                    byte[] content = buffer.GetBuffer();
                    int total = (int)buffer.Length;
                    int end = FindHeaderEnd(content, total);
                    if (end >= 0)
                    {
                        int headerLength = end + 4;
                        byte[] header = new byte[headerLength];
                        Buffer.BlockCopy(content, 0, header, 0, headerLength);
                        byte[] remainder = new byte[total - headerLength];
                        if (remainder.Length > 0)
                        {
                            Buffer.BlockCopy(content, headerLength, remainder, 0, remainder.Length);
                        }

                        return new HeaderReadResult { Header = header, Remainder = remainder };
                    }
                }
            }

            throw new InvalidDataException("Cabeçalho HTTP muito grande.");
        }

        private static int FindHeaderEnd(byte[] content, int length)
        {
            for (int index = 0; index <= length - 4; index++)
            {
                if (content[index] == 13
                    && content[index + 1] == 10
                    && content[index + 2] == 13
                    && content[index + 3] == 10)
                {
                    return index;
                }
            }

            return -1;
        }

        private static ParsedRequest ParseRequest(byte[] headerBytes)
        {
            string header = Encoding.ASCII.GetString(headerBytes);
            string[] lines = header.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestParts = lines[0].Split(new[] { ' ' }, 3);
            if (requestParts.Length != 3 || !requestParts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Linha de requisição inválida.");
            }

            string method = requestParts[0].ToUpperInvariant();
            if (method == "CONNECT")
            {
                HostAndPort target = ParseHostAndPort(requestParts[1], 443);
                return new ParsedRequest
                {
                    IsConnect = true,
                    Host = NormalizeHost(target.Host),
                    Port = target.Port
                };
            }

            Uri uri;
            string hostHeader = GetHeader(lines, "Host");
            string path = requestParts[1];
            HostAndPort destination;

            if (Uri.TryCreate(requestParts[1], UriKind.Absolute, out uri))
            {
                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Somente HTTP e HTTPS são aceitos.");
                }

                destination = new HostAndPort { Host = uri.Host, Port = uri.Port };
                path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(hostHeader) || !path.StartsWith("/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Destino HTTP ausente.");
                }

                destination = ParseHostAndPort(hostHeader, 80);
            }

            destination.Host = NormalizeHost(destination.Host);
            StringBuilder forwarded = new StringBuilder();
            forwarded.Append(method).Append(' ').Append(path).Append(' ').Append(requestParts[2]).Append("\r\n");
            for (int index = 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Length == 0)
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    throw new InvalidDataException("Cabeçalho HTTP inválido.");
                }

                string name = line.Substring(0, colon).Trim();
                if (string.Equals(name, "Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                forwarded.Append(line).Append("\r\n");
            }

            forwarded.Append("Host: ").Append(destination.Host);
            if (destination.Port != 80)
            {
                forwarded.Append(':').Append(destination.Port);
            }

            forwarded.Append("\r\n");
            forwarded.Append("Connection: close\r\n\r\n");
            return new ParsedRequest
            {
                IsConnect = false,
                Host = destination.Host,
                Port = destination.Port,
                ForwardedHeader = forwarded.ToString()
            };
        }

        private static string GetHeader(IEnumerable<string> lines, string requestedName)
        {
            foreach (string line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0
                    && string.Equals(
                        line.Substring(0, colon).Trim(),
                        requestedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(colon + 1).Trim();
                }
            }

            return null;
        }

        private static HostAndPort ParseHostAndPort(string value, int defaultPort)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("[", StringComparison.Ordinal)
                || value.IndexOf('/') >= 0)
            {
                throw new InvalidDataException("Host inválido.");
            }

            string host = value.Trim();
            int port = defaultPort;
            int colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                int parsedPort;
                if (!int.TryParse(host.Substring(colon + 1), out parsedPort)
                    || parsedPort <= 0
                    || parsedPort > 65535)
                {
                    throw new InvalidDataException("Porta inválida.");
                }

                port = parsedPort;
                host = host.Substring(0, colon);
            }

            return new HostAndPort { Host = host, Port = port };
        }

        private static string NormalizeHost(string host)
        {
            try
            {
                return DomainName.Normalize(host);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(exception.Message);
            }
        }

        private static void WriteBlocked(Stream stream)
        {
            const string body = "<html><body><h1>Acesso bloqueado</h1></body></html>";
            WriteAscii(
                stream,
                "HTTP/1.1 403 Forbidden\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
                    + Encoding.UTF8.GetByteCount(body)
                    + "\r\nConnection: close\r\n\r\n");
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void TryWriteBadRequest(TcpClient client)
        {
            try
            {
                WriteAscii(client.GetStream(), "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
            }
            catch
            {
            }
        }

        private static void WriteAscii(Stream stream, string content)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        private sealed class HeaderReadResult
        {
            public byte[] Header { get; set; }
            public byte[] Remainder { get; set; }
        }

        private sealed class ParsedRequest
        {
            public bool IsConnect { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string ForwardedHeader { get; set; }
        }

        private sealed class HostAndPort
        {
            public string Host { get; set; }
            public int Port { get; set; }
        }
    }
}
