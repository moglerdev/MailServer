using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace MailServer {
    class Program {
        static void Main(string[] args)
        {

            Console.WriteLine("Hello World!");
        }
    }

    class ServerHandler {
        IPAddress _address = IPAddress.Parse("148.251.124.112");
        Int32 _port = 25;

        IPEndPoint _endPoint;
        TcpListener _listener;
        Task _loop;

        List<ClientHandler> _clients = new List<ClientHandler>();

        public ServerHandler()
        {
            this._endPoint = new IPEndPoint(this._address, this._port);
            this._listener = new TcpListener(this._endPoint);
        }

        public void Start()
        {
            this._listener.Start();

            this._loop = Task.Run(this._Loop);
        }

        private void _Loop()
        {
            TcpClient client;
            while ((client = this._listener.AcceptTcpClient()) != null)
            {
                this._clients.Add(new ClientHandler(client));
            }            
        }

        public void Stop()
        {
            this._loop.Dispose();
            this._listener.Stop();
        }
    }

    class ClientHandler : IDisposable {
        private TcpClient _client;
        private SslStream _sslStream;

        protected StreamReader reader;
        protected StreamWriter writer;

        public Boolean IsEncrypted { get => this._sslStream != null; }

        public ClientHandler(TcpClient client)
        {
            this._client = client;
        }

        protected void GetMessageAsync(CancellationToken cancellationToken)
        {
            
        }

        public void Dispose()
        {
            this._sslStream.Dispose();
            this._client.Dispose();
        }
    }

    class SmtpMessage {
        
    }
}
