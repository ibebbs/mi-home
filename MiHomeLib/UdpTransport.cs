using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace MiHomeLib
{
    public class UdpTransport: IDisposable
    {
        private readonly string _gatewayWritePassword;
        private readonly string _multicastAddress;
        private readonly int _serverPort;
        private readonly Socket _socket;
        
        private readonly byte[] _initialVector =
            {0x17, 0x99, 0x6d, 0x09, 0x3d, 0x28, 0xdd, 0xb3, 0xba, 0x69, 0x5a, 0x2e, 0x6f, 0x58, 0x56, 0x2e};

        private static string _currentToken;

        public UdpTransport(string gatewayWritePassword, string multicastAddress = "224.0.0.50", int serverPort = 9898, int multicastPort = 4321)
        {
            _gatewayWritePassword = gatewayWritePassword;
            _multicastAddress = multicastAddress;
            _serverPort = serverPort;
        
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(IPAddress.Parse(_multicastAddress)));
            _socket.Bind(new IPEndPoint(IPAddress.Any, _serverPort));
        }

        private static string GetWriteKey(byte[] key, byte[] iv)
        {
            byte[] encrypted;
            
            using (var aesCbc128 = new RijndaelManaged())
            {
                aesCbc128.KeySize = 128;
                aesCbc128.BlockSize = 128;
                aesCbc128.IV = iv;
                aesCbc128.Key = key;
                aesCbc128.Mode = CipherMode.CBC;
                aesCbc128.Padding = PaddingMode.None;

                var encryptor = aesCbc128.CreateEncryptor(aesCbc128.Key, aesCbc128.IV);

                using (var ms = new MemoryStream())
                {
                    using (var cryptoStream = new StreamWriter(new CryptoStream(ms, encryptor, CryptoStreamMode.Write)))
                    {
                        cryptoStream.Write(_currentToken);
                    }

                    encrypted = ms.ToArray();
                }
            }
            
            return BitConverter.ToString(encrypted).Replace("-", string.Empty);
        }

        public int SendReadCommand(string sid, string data)
        {
            var buffer = Encoding.ASCII.GetBytes(data);

            return _socket.SendTo(buffer, 0, buffer.Length, 0, new IPEndPoint(IPAddress.Parse(_multicastAddress), _serverPort));
        }

        public int SendWriteCommand(string sid, string data)
        {
            while (true)
            {
                if (_currentToken == null)
                {
                    Thread.Sleep(5000);
                    continue;
                }

                var jObj = JObject.Parse(data);

                jObj["key"] = GetWriteKey(Encoding.ASCII.GetBytes(_gatewayWritePassword), _initialVector);
                
                var buffer = Encoding.ASCII.GetBytes($"{{\"cmd\":\"write\",\"sid\":\"{sid}\", \"data\":\"{jObj}\"}}");

                return _socket.SendTo(buffer, 0, buffer.Length, 0, new IPEndPoint(IPAddress.Parse(_multicastAddress), _serverPort));
            }
        }

        public async Task<string> ReceiveAsync()
        {
            var data = new byte[1024];
            var len = await _socket.ReceiveAsync(data, SocketFlags.None).ConfigureAwait(false);
            
            return Encoding.ASCII.GetString(data, 0, len);
        }

        public void Dispose()
        {
            _socket?.Shutdown(SocketShutdown.Both);
            _socket?.Close();
        }

        public void SetToken(string token)
        {
            _currentToken = token;
        }
    }
}