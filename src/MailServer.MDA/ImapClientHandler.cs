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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace MailServer.MDA {
    public class ImapClientHandler : IClientHandler, IDisposable {
        const int bufferSize = 4069;

        public TcpClient Client { get; private set; }
        public Stream Stream { get; private set; }
        public Boolean IsConnected { get; private set; }
        public IPAddress ClientAddress { get; private set; }
        public Boolean IsAuthenticated { get; private set; }
        public SslProtocols SslProtocol { get; private set; }

        private readonly Socket _clientSocket;
        private readonly Timer _timer = new Timer(30000);

        private Encoding _encoder = Encoding.UTF8;

        public void Start()
        {
        }

        public async Task StartAsync()
        {
            await Task.Run(this.Start);
        }

        private void ReceivingData()
        {
            Int32 readData, testedLength = 0;
            Byte[] buffer = new byte[bufferSize];
            MemoryStream ms = new MemoryStream();
            this._timer.Start();
            while (( readData = this.Stream.Read(buffer, 0, buffer.Length) ) > 0)
            {
                try
                {
                    this._timer.Stop();
                    ms.Write(buffer, 0, readData);
                    // TODO Max Buffer Size
                    byte[] msBuffer = ms.GetBuffer();
                    for (int i = testedLength; i < msBuffer.Length; ++i)
                    {
                        if (i + 1 < msBuffer.Length && msBuffer[i + 1] == (byte)'\0')
                            break;

                        if (i + 2 < msBuffer.Length)
                        {
                            if (msBuffer[i] == (byte)'\r' && msBuffer[i + 1] == (byte)'\n')
                            {
                                String message = this._encoder.GetString(ms.ToArray());

                                ms.Dispose();
                                ms = null;
                            }
                        }

                        if (ms == null)
                        {
                            ms = new MemoryStream();
                            testedLength = 0;
                        }
                        else
                            testedLength = i;
                    }
                }
                catch (Exception e)
                {
                    this.Close("451 Requested action aborted: local error in processing");
                }
                this._timer.Start();
            }
            this._timer.Stop();

            ms?.Dispose();
        }

        public void Close(String message)
        {
        }

        public void Dispose()
        {
        }
    }
}
