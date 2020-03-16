﻿// MailServer - Easy and fast Mailserver
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace MailServer.Interface {
    public interface IClientHandler {
        public TcpClient Client { get; }
        public Stream Stream { get; }
        public Boolean IsConnected { get; }
        public IPAddress ClientAddress { get; }
        public Boolean IsAuthenticated { get; }
        public SslProtocols SslProtocol { get; }

        public void Start();
        public Task StartAsync();

        public void Close(String message);
    }
}