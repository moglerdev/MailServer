using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace MailServer.MDA {
    class PopClientHandler : IDisposable {

        public TcpClient Client { get; private set; }
        protected Stream Stream { get; private set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        public IPAddress ClientAddress { get => ( this._clientSocket.RemoteEndPoint as IPEndPoint ).Address; }
        public Boolean IsAuthenticated { get; private set; }
        public SslProtocols Encryption { get; private set; } = SslProtocols.None;

        private readonly Socket _clientSocket;

        public PopClientHandler(TcpClient client, SslProtocols protocol = SslProtocols.None)
        {
            this.Client = client;
            this._clientSocket = this.Client.Client;

            this.Encryption = protocol;
        }


        public void Dispose()
        {

        }
    }
}
