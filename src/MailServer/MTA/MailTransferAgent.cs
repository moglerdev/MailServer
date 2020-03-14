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

// TODO: Max. Verbindungen über Config einstellen

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Sockets;
using MailServer.Common;
using System.Threading.Tasks;
using System.Threading;
using System.Security.Authentication;

namespace MailServer.MTA {
    class MailTransferAgent : IDisposable {
        public static X509Certificate Certificate { get; set; }

        private readonly List<SmtpClientHandler> _connectedClientList = new List<SmtpClientHandler>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TcpListener _listener;
        private readonly IPEndPoint _endpoint;

        public IPAddress Address { get => this._endpoint.Address; }
        public Int32 Port { get => this._endpoint.Port; }
        public SslProtocols SslProtocols { get; private set; }

        public Boolean IsListening { get; private set; }


        public MailTransferAgent(Int32 port, SslProtocols protocols = SslProtocols.None)
        {
            this.SslProtocols = protocols;

            IPAddress adr;
            if (!IPAddress.TryParse(Config.Current.Listen, out adr))
                adr = IPAddress.Any;

            this._endpoint = new IPEndPoint(adr, port);
            this._listener = new TcpListener(this._endpoint);

            this._listener.AllowNatTraversal(true);
        }

        public async Task StartAsync()
        {
            this._listener.Start();
            this.IsListening = true;

            Boolean running = true;
            do
            {
                this._cts.Token.ThrowIfCancellationRequested();
                try
                {
                    SmtpClientHandler client = new SmtpClientHandler(
                        await this._listener.AcceptTcpClientAsync(),
                        false,
                        this.SslProtocols);

                    client.OnDisconnect += ClientDisconnected;
#pragma warning disable CS4014 // Da auf diesen Aufruf nicht gewartet wird, wird die Ausführung der aktuellen Methode vor Abschluss des Aufrufs fortgesetzt.
                    client.StartAsync().ContinueWith(t => Console.WriteLine(t), TaskContinuationOptions.OnlyOnFaulted);
#pragma warning restore CS4014 // Da auf diesen Aufruf nicht gewartet wird, wird die Ausführung der aktuellen Methode vor Abschluss des Aufrufs fortgesetzt.
                    this._connectedClientList.Add(client);
                }
                catch (OperationCanceledException e)
                {
                    running = false;
                }
                catch (Exception e)
                {

                }
            } while (running);

        }

        public void Stop()
        {
            this._cts.Cancel();
            this._listener.Stop();
            this.IsListening = false;
        }

        protected void ClientDisconnected(SmtpClientHandler smtp)
        {
            Console.WriteLine("Client disconnected!");
            smtp.Dispose();
            this._connectedClientList.Remove(smtp);
        }

        public void Dispose()
        {
            this.Stop();
        }
    }
}
