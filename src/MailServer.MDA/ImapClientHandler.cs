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

using MailServer.Common.Base;
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
    public class ImapClientHandler : ClientHandlerBase, IDisposable {

        public ImapClientHandler(TcpClient client, SslProtocols sslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13)
            : base(client, sslProtocols)
        {

        }

        protected override void ListenForData()
        {
            Int32 readData, testedLength = 0;
            Boolean isData = false;
            Byte[] buffer = new byte[BufferSize];
            MemoryStream ms = new MemoryStream();
            while (( readData = this.Read(buffer, 0, buffer.Length) ) > 0)
            {
                try
                {
                    ms.Write(buffer, 0, readData);
                    // TODO Max Buffer Size
                    byte[] msBuffer = ms.GetBuffer();
                    for (int i = testedLength; i < msBuffer.Length; ++i)
                    {
                        if (i + 1 < msBuffer.Length && msBuffer[i + 1] == (byte)'\0')
                            break;

                        if (i + 2 < msBuffer.Length)
                        {
                            if (this.IsEndOfLine(buffer, i))
                            {
                                String message = this.Encoding.GetString(ms.ToArray());

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
            }
            ms?.Dispose();
            this.Disconnected();
        }
    }
}
