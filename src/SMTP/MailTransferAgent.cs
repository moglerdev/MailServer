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

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Sockets;
using MailServer.Common;
using System.Threading.Tasks;
using System.Threading;

namespace MailServer.SMTP {
    class MailTransferAgent : IDisposable {
        public static X509Certificate Certificate { get; set; }

        private readonly List<SmtpClientHandler> _connectedClientList = new List<SmtpClientHandler>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public Boolean IsListening { get; private set; }

        public MailTransferAgent() 
        {
        }

        public Task StartAsync(CancellationToken? cancellationToken = null)
        {
            return Task.Run(() => this.Start(cancellationToken));
        }

        private void Start(CancellationToken? cancellationToken = null)
        {
            IPAddress adr;
            if (!IPAddress.TryParse(Config.Current.Listen, out adr))
                adr = IPAddress.Any;

            IPEndPoint endPoint = new IPEndPoint(adr, 25);
            TcpListener listener = new TcpListener(endPoint);
            listener.AllowNatTraversal(true);
            listener.Start();
            this.IsListening = true;

            Boolean running = true;

            while (running)
            {
                this._cts.Token.ThrowIfCancellationRequested();
                cancellationToken?.ThrowIfCancellationRequested();

                try
                {
                    SmtpClientHandler client = new SmtpClientHandler(listener.AcceptTcpClient());
                    client.OnDisconnect += ClientDisconnected;
                    this._connectedClientList.Add(client);
                }
                catch (OperationCanceledException e)
                {
                    running = false;
                }
                catch (Exception e)
                {

                }
            }

            listener.Stop();
            this.IsListening = false;
        }

        protected void ClientDisconnected(SmtpClientHandler smtp)
        {
            Console.WriteLine("Client disconnected!");
            smtp.Dispose();
            this._connectedClientList.Remove(smtp);
            smtp = null;
        }

        public void Dispose()
        {
            this._cts?.Cancel();
        }
    }
}
