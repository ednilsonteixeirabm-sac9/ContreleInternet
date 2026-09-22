using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ControleInternet.Common
{
    [DataContract]
    public sealed class AdminRequest
    {
        [DataMember(Name = "operation", Order = 1)]
        public string Operation { get; set; }

        [DataMember(Name = "password", Order = 2, EmitDefaultValue = false)]
        public string Password { get; set; }

        [DataMember(Name = "config", Order = 3, EmitDefaultValue = false)]
        public AppConfig Config { get; set; }
    }

    [DataContract]
    public sealed class AdminResponse
    {
        [DataMember(Name = "success", Order = 1)]
        public bool Success { get; set; }

        [DataMember(Name = "initialized", Order = 2)]
        public bool Initialized { get; set; }

        [DataMember(Name = "error", Order = 3, EmitDefaultValue = false)]
        public string Error { get; set; }

        [DataMember(Name = "config", Order = 4, EmitDefaultValue = false)]
        public AppConfig Config { get; set; }

        public static AdminResponse Fail(string error)
        {
            return new AdminResponse { Success = false, Error = error };
        }
    }

    public static class AdminProtocol
    {
        public const string StatusOperation = "status";
        public const string InitializeOperation = "initialize";
        public const string AuthenticateOperation = "authenticate";
        public const string SaveOperation = "save";
        private const int MaximumMessageLength = 1024 * 1024;

        public static void Write<T>(Stream stream, T message)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            byte[] content;
            using (MemoryStream buffer = new MemoryStream())
            {
                serializer.WriteObject(buffer, message);
                content = buffer.ToArray();
            }

            if (content.Length > MaximumMessageLength)
            {
                throw new InvalidDataException("Mensagem administrativa muito grande.");
            }

            byte[] length = BitConverter.GetBytes(content.Length);
            stream.Write(length, 0, length.Length);
            stream.Write(content, 0, content.Length);
            stream.Flush();
        }

        public static T Read<T>(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            byte[] lengthBytes = ReadExactly(stream, sizeof(int));
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > MaximumMessageLength)
            {
                throw new InvalidDataException("Tamanho de mensagem administrativa inválido.");
            }

            byte[] content = ReadExactly(stream, length);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream buffer = new MemoryStream(content, false))
            {
                return (T)serializer.ReadObject(buffer);
            }
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            byte[] result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(result, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("A conexão administrativa foi encerrada.");
                }

                offset += read;
            }

            return result;
        }
    }
}
