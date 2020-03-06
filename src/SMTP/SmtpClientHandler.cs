// MailServer - Easy and Fast Mailserver 
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
// You should have received a copy of the GNU General Public License along with this program.If not, see<https://www.gnu.org/licenses/>.

using MailServer.Common;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace MailServer.SMTP
{
    enum SmtpMessageType
    {
        QUIT = 0,
        HELO = 1,
        EHLO = 2,
        MAIL = 3,
        RCPT = 4,
        DATA = 5,
        STARTTLS = 6,
    }

    delegate void MailArrived(ReceivedMessage mail);
    delegate void ClientDisconnect(SmtpClientHandler client);

    class SmtpClientHandler : IDisposable
    {
        const int bufferSize = 4069;

        private static SmtpMessageType GetMessageType(String message)
        {
            try
            {
                if (message.Trim() == "STARTTLS")
                {
                    return SmtpMessageType.STARTTLS;
                }

                String prefix = message.Substring(0, 4);
                if (prefix == "QUIT")
                    return SmtpMessageType.QUIT;
                else if (prefix == "HELO")
                    return SmtpMessageType.HELO;
                else if (prefix == "EHLO")
                    return SmtpMessageType.EHLO;
                else if (prefix == "MAIL")
                    return SmtpMessageType.MAIL;
                else if (prefix == "RCPT")
                    return SmtpMessageType.RCPT;
                else if (prefix == "DATA")
                    return SmtpMessageType.DATA;
            }
            catch (Exception e) { }
            throw new Exception("Received message not supported!");
        }

        public static MailboxAddress GetAddress(String message)
        {
            String name = null;
            String mail = null;

            Int32 nameBegin = message.IndexOf('\'');
            Int32 nameEnd = message.IndexOf('\'');

            if (nameBegin < nameEnd)
                name = message.Substring(nameBegin, nameEnd - nameBegin);

            Int32 mailBegin = message.IndexOf('<');
            Int32 mailEnd = message.IndexOf('>');
            if (mailBegin < mailEnd)
                mail = message.Substring(mailBegin + 1, mailEnd - mailBegin - 1);

            return new MailboxAddress(name, mail);
        }

        public static Boolean ExistMailbox(MailboxAddress address) => Config.Current.Accounts.Any(x => x == address.Address);


        public event MailArrived OnMailArrived;
        public event ClientDisconnect OnDisconnect;

        public TcpClient Client { get; private set; }
        private readonly Socket clientSocket;
        protected Stream Stream { get; private set; }

        public Boolean IsConnected { get => clientSocket.Poll(1000, SelectMode.SelectRead); }

        private Encoding encoder = Encoding.UTF8;

        public SmtpClientHandler(TcpClient client)
        {
            this.Client = client;
            this.clientSocket = client.Client;
            this.Stream = client.GetStream();

            this.OnMailArrived += this.MailArrived;

            this.SendMessage($"220 {Config.Current.Domain} ESMTP MAIL Service ready at {DateTimeOffset.Now.ToString()}");

            this.BeginReadMessage();
        }

        private Boolean isData = false;

        protected virtual void MailArrived(ReceivedMessage mail)
        {
            mail.Save();
        }

        private Byte[] buffer = new byte[bufferSize];
        private IAsyncResult BeginReadMessage()
        {
            lock (this.buffer)
                return this.Stream.BeginRead(this.buffer, 0, this.buffer.Length, new AsyncCallback(this.ReceiveMessageCallback), this.Stream);
        }

        private void SendMessage(String message)
        {
            Console.WriteLine("[Server]:" + message);
            this.Stream.Write(this.encoder.GetBytes(message + "\r\n"));
        }

        private ReceivedMessage currentMail;
        MemoryStream memoryBuffer = null;
        private void ReceiveMessageCallback(IAsyncResult result)
        {
            Int32 readBytes = this.Stream.EndRead(result);

            if (readBytes < 2)
            {
                if (memoryBuffer == null)
                    memoryBuffer = new MemoryStream();
                lock (this.buffer)
                    memoryBuffer.Write(this.buffer, 0, readBytes);
                this.BeginReadMessage();
                return;
            }

            Byte[] _buffer = null;

            if (isData)
            {
                lock (this.buffer)
                {
                    if (memoryBuffer == null)
                        memoryBuffer = new MemoryStream();

                    memoryBuffer.Write(this.buffer, 0, readBytes);

                    if (this.buffer[readBytes - 5] == (byte)'\r' && this.buffer[readBytes - 4] == (byte)'\n'
                        && this.buffer[readBytes - 3] == (byte)'.'
                        && this.buffer[readBytes - 2] == (byte)'\r' && this.buffer[readBytes - 1] == (byte)'\n')
                    {
                        _buffer = memoryBuffer.ToArray();
                        memoryBuffer.Dispose();
                        memoryBuffer = null;
                    }
                    else
                    {
                        this.BeginReadMessage();
                        return;
                    }
                }
                using (MemoryStream ms = new MemoryStream(_buffer))
                    this.currentMail.MimeMessage = MimeMessage.Load(ms);

                Console.WriteLine("<Client>: {MIME MESSAGE}");

                this.OnMailArrived?.Invoke(this.currentMail);
                this.SendMessage("250 Requested mail action okay, completed");
                this.isData = false;

                this.BeginReadMessage();
            }
            else
            {
                lock (this.buffer)
                {
                    if (memoryBuffer == null)
                        memoryBuffer = new MemoryStream();

                    memoryBuffer.Write(this.buffer, 0, readBytes);

                    if (this.buffer[readBytes - 2] == (byte)'\r' && this.buffer[readBytes - 1] == (byte)'\n')
                    {
                        _buffer = memoryBuffer.ToArray();
                        memoryBuffer.Dispose();
                        memoryBuffer = null;
                    }
                    else
                    {
                        this.BeginReadMessage();
                        return;
                    }
                }

                String message = this.encoder.GetString(_buffer);
                SmtpMessageType type;

                try
                {
                    type = GetMessageType(message);
                }
                catch (Exception e)
                {
                    this.SendMessage("500 Syntax error, command unrecognised");
                    this.BeginReadMessage();
                    return;
                }

                Console.WriteLine("<Client>:" + message);

                if (type == SmtpMessageType.QUIT)
                {
                    this.Close();
                }
                else if (type == SmtpMessageType.HELO || type == SmtpMessageType.EHLO)
                {
                    var spfValidator = new ARSoft.Tools.Net.Spf.SpfValidator();
                    this.SendMessage($"250-{Config.Current.Domain} Hello [{this.clientSocket.RemoteEndPoint.ToString()}]");

                    if(type == SmtpMessageType.EHLO)
                    {
                        // Send Exentsions
                        this.SendMessage("250-STARTTLS");
                        //this.SendMessage("250-SIZE 12345678");
                        this.SendMessage("250 HELP");
                    }

                    this.BeginReadMessage();
                }
                else if (type == SmtpMessageType.STARTTLS)
                {
                    this.SendMessage("220 SMTP server ready");
                    SslStream tlsStream = new SslStream(this.Client.GetStream(), false, new RemoteCertificateValidationCallback((sender, cert, chain, ssl) => true));
                    tlsStream.AuthenticateAsServer(Program.Certificate, false, System.Security.Authentication.SslProtocols.Tls12, false);
                    this.Stream = tlsStream;

                    this.BeginReadMessage();
                }
                else if (type == SmtpMessageType.MAIL)
                {
                    currentMail = new ReceivedMessage(GetAddress(message));
                    this.SendMessage("250 Requested mail action okay, completed");

                    this.BeginReadMessage();
                }
                else if (type == SmtpMessageType.RCPT)
                {
                    MailboxAddress recv = GetAddress(message);
                    if (ExistMailbox(recv))
                    {
                        this.currentMail.Receivers.Add(recv);
                        this.SendMessage("250 Requested mail action okay, completed");
                    }
                    else
                        this.SendMessage("550 Requested action not taken: mailbox unavailable");

                    this.BeginReadMessage();
                }
                else if (type == SmtpMessageType.DATA)
                {
                    isData = true;
                    this.SendMessage("354 Start mail input; end with <CRLF>.<CRLF>");

                    this.BeginReadMessage();
                }
                else
                {
                    this.SendMessage($"421 {Config.Current.Domain} Service not available, closing transmission channel");

                    this.BeginReadMessage();
                }
            }
        }

        public void Close()
        {
            this.OnDisconnect?.Invoke(this);
            if (this.Client != null && this.Client.Connected)
                this.SendMessage("221 <domain> Service closing transmission channel");

            this.Client?.Close();

            Console.WriteLine("Connection closed");
        }

        public void Dispose()
        {
            this.Client?.Dispose();
            this.Client = null;
            this.currentMail = null;
            this.memoryBuffer?.Dispose();
            this.memoryBuffer = null;
            this.buffer = null;
        }
    }
}
