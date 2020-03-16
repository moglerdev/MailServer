// MailServer - Easy and fast Mailserver
//
// Copyright(C) 2020 Christopher Mogler
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program. If not, see<https://www.gnu.org/licenses/>.

using MailServer.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace MailServer.MDA {
    public class PopClientHandler : IClientHandler, IDisposable {
        public TcpClient Client { get; private set; }
        public Stream Stream { get; private set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        public IPAddress ClientAddress { get => ( this._clientSocket.RemoteEndPoint as IPEndPoint ).Address; }
        public Boolean IsAuthenticated { get; private set; }
        public SslProtocols SslProtocol { get; private set; } = SslProtocols.None;
        
        private readonly Socket _clientSocket;

        public PopClientHandler(TcpClient client, SslProtocols protocol = SslProtocols.None)
        {
            this.Client = client;
            this._clientSocket = this.Client.Client;

            this.SslProtocol = protocol;
        }

        public void Start()
        {

        }

        public async Task StartAsync()
        {
            await Task.Run(this.Start);
        }

        public void Close(String message)
        {

        }

        public void Dispose()
        {

        }
    }
}
