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

using MailServer.Common;
using MimeKit;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Timers;
using System.Security.Authentication;
using System.Collections.Generic;
using System.Threading.Tasks;
using MailServer.Interface;

// TODO:
//  - Add Authentication
//  - Validate / Verify commands
//  - Validate / Verify connected client
//  - Add Whitelist
//  - Handle send commands better

namespace MailServer.MTA
{
    public enum SmtpMessageType
    {
        QUIT = 1,
        HELO = 2,
        EHLO = 3,
        MAIL = 4,
        RCPT = 5,
        DATA = 6,
        RSET = 7,
        AUTH = 8,
        NOOP = 9,
        STARTTLS,
    }

    public delegate void MailArrived(ReceivedMessage mail);
    public delegate void ClientDisconnect(SmtpClientHandler client);

    public class SmtpClientHandler : IClientHandler, IDisposable
    {
        #region Static
        const int bufferSize = 4069;

        private static SmtpMessageType GetMessageType(String message)
        {
            try
            {
                if (message.Length > 7 && message.Substring(0, 8) == "STARTTLS")
                {
                    return SmtpMessageType.STARTTLS;
                }

                if(message.Length > 3)
                {
                    String prefix = message.Substring(0, 4);
                    if (prefix == "QUIT")
                        return SmtpMessageType.QUIT;
                    else if (prefix == "AUTH")
                        return SmtpMessageType.AUTH;
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
                    else if (prefix == "RSET")
                        return SmtpMessageType.RSET;
                }
            }
            catch (Exception e) { }
            throw new NotSupportedException("Received message not supported!");
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

        #endregion

        #region Properties
        public TcpClient Client { get; private set; }
        public Stream Stream { get; private set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        public String ClientName { get; private set; }
        public String ClientDomain { get; private set; }
        public IPAddress ClientAddress { get => (this._clientSocket.RemoteEndPoint as IPEndPoint).Address; }
        public Boolean IsAuthenticated { get; private set; }
        public SslProtocols SslProtocol { get; private set; } = SslProtocols.None;
        #endregion

        #region Events
        public event MailArrived OnMailArrived;
        public event ClientDisconnect OnDisconnect;
        #endregion

        #region Readonly
        private readonly Socket _clientSocket;
        private readonly Timer _timer = new Timer(30000);

        public readonly Boolean IsDeliverService;
        #endregion

        #region Instance
        private Encoding _encoder = Encoding.UTF8;
        #endregion

        #region Constructor
        public SmtpClientHandler(TcpClient client, Boolean isDeliverService = false, SslProtocols encryption = SslProtocols.None)
        {
            this.IsDeliverService = isDeliverService;

            this.Client = client;
            this._clientSocket = client.Client;

            if(encryption != SslProtocols.None)
            {
                this.InitEncryptedStream(encryption);
            }
            this.Stream = client.GetStream();

            this.OnMailArrived += this.MailArrived;

            this._timer.Elapsed += this.CheckConnection;
            this._timer.AutoReset = false;

            var lookup = new DnsClient.LookupClient();
            var query  = lookup.QueryReverse(this.ClientAddress);
            this.ClientDomain = query.Answers.PtrRecords().FirstOrDefault()?.PtrDomainName;
        }
        #endregion

        #region Methods

        public void Start()
        {
            this.SendMessage($"220 {Config.Current.Domain} ESMTP MAIL Service ready at {DateTimeOffset.Now.ToString()}");
            this.ReceivingData();
        }

        public async Task StartAsync()
        {
            await Task.Run(this.Start);
        }

        protected virtual void CheckConnection(object sender, EventArgs eventArgs)
        {
            this._timer.Stop();

            if (this.IsConnected)
                this.Close($"451 Timeout waiting for client input [{Config.Current.Domain}]");
            else
                this.OnDisconnect?.Invoke(this);
        }

        protected virtual void MailArrived(ReceivedMessage mail)
        {
            mail.Save();
        }

        protected Boolean VerifyCommand(SmtpMessageType type)//, out String message)
        {
            if (type == SmtpMessageType.QUIT)
                return true;
           
            if (String.IsNullOrEmpty(this.ClientName))
            {
                if (type == SmtpMessageType.EHLO || type == SmtpMessageType.HELO)
                    return true;
            }
            else
            {
                if (type == SmtpMessageType.STARTTLS)
                    return this.SslProtocol == SslProtocols.None;

                if (type == SmtpMessageType.AUTH)
                    return this.IsDeliverService && this.SslProtocol != SslProtocols.None;

                if (type == SmtpMessageType.MAIL)
                {
                    if (this.IsAuthenticated)
                    {
                        return true; // TODO: is the User authenticated
                    }
                    return DnsHelper.CheckSpf(this.msg.Sender.Address, this.ClientAddress);
                }

                if (type == SmtpMessageType.RCPT)
                    return this.msg != null;

                if (type == SmtpMessageType.DATA)
                    return this.msg != null && this.msg.Receivers.Any();

                if (type == SmtpMessageType.RSET)
                    return true;
            }

            return false;
        }

        protected virtual void InitEncryptedStream(SslProtocols protocols = SslProtocols.Tls12 | SslProtocols.Tls13)
        {
            SslStream encryptedStream = new SslStream(this.Client.GetStream(), false, new RemoteCertificateValidationCallback((sender, cert, chain, ssl) => true));
            encryptedStream.AuthenticateAsServer(MailTransferAgent.Certificate, false, protocols, false); // TODO: Über Config die erlaubten Verschlüsselungen einstellen
            this.SslProtocol = encryptedStream.SslProtocol;
            this.Stream = encryptedStream;
        }

        protected Boolean VerifySender(String mail)
        {

            return false;
        }

        protected virtual void SendExentsions()
        {
            List<String> exentsions = new List<string>();
            // 50 AUTH CRAM-MD5 LOGIN PLAIN
            // Send Exentsions
            exentsions.Add("STARTTLS");
            if(this.IsDeliverService)
                exentsions.Add("AUTH LOGIN PLAIN");

            for(int i = 0; i < exentsions.Count; ++i)
            {
                this.SendMessage($"250{(exentsions.Count - 1 < i ? "-" : " " )}"+ exentsions[i]);
            }
        }

        private void SendMessage(String message)
        {
            Console.WriteLine("[Server]:" + message);
            this.Stream.Write(this._encoder.GetBytes(message + "\r\n"));
        }


        ReceivedMessage msg = null;

        private void ReceivingData()
        {
            Int32 readData, testedLength = 0;
            Boolean isData = false;
            Byte[] buffer = new byte[bufferSize];
            MemoryStream ms = new MemoryStream();
            this._timer.Start();
            while (( readData = this.Stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                try
                {
                    this._timer.Stop();
                    ms.Write(buffer, 0, readData);
                    // TODO Max Buffer Size
                    byte[] msBuffer = ms.GetBuffer();
                    for (int i = testedLength; i < msBuffer.Length; ++i)
                    {
                        if (i + 1 < msBuffer.Length && msBuffer[i+1] == (byte)'\0')
                            break;
                        if (isData && i + 4 < msBuffer.Length)
                        {
                            if (msBuffer[i] == (byte)'\r' && msBuffer[i+1] == (byte)'\n' &&
                                msBuffer[i+2] == (byte)'.' &&
                                msBuffer[i+3] == (byte)'\r' && msBuffer[i+4] == (byte)'\n')
                            {
                                using (MemoryStream mimeMs = new MemoryStream(ms.ToArray()))
                                    msg.MimeMessage = MimeMessage.Load(mimeMs); // TODO: <CLRF>.<CLRF> wird in die Mime mit geschrieben

                                this.OnMailArrived?.Invoke(msg);

                                this.SendMessage("250 Requested mail action okay, completed");

                                Console.WriteLine("<Client>: {MIME MESSAGE}");

                                ms.Dispose();
                                ms = null;

                                isData = false;
                            }
                        }
                        else if (i + 2 < msBuffer.Length)
                        {
                            if (msBuffer[i] == (byte)'\r' && msBuffer[i+1] == (byte)'\n')
                            {
                                String message = this._encoder.GetString(ms.ToArray());

                                switch (HandleCommand(message))
                                {
                                    case SmtpMessageType.DATA: isData = true; break;
                                    case SmtpMessageType.RSET: isData = false; break;
                                    case SmtpMessageType.QUIT: return;
                                }

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

        private SmtpMessageType HandleCommand(String message)
        {
            SmtpMessageType? type = null;

            try
            {
                type = GetMessageType(message);
                Console.WriteLine("<Client>:" + message);
            }
            catch (Exception e)
            {
                this.SendMessage("500 Syntax error, command unrecognised");
            }

            if (type == SmtpMessageType.QUIT)
            {
                this.Close($"221 {Config.Current.Domain} Service closing transmission channel");
                return SmtpMessageType.QUIT;
            }
            else if (type == SmtpMessageType.HELO || type == SmtpMessageType.EHLO)
            {
                this.ClientName = message.Substring(4, message.Length - 4).Trim();
                this.SendMessage($"250-{Config.Current.Domain} Hello [{this.ClientAddress.ToString()}]");

                if (type == SmtpMessageType.EHLO)
                    this.SendExentsions();
            }
            else if (type == SmtpMessageType.STARTTLS)
            {
                if (this.VerifyCommand(SmtpMessageType.STARTTLS))
                {
                    this.SendMessage("220 SMTP server ready");
                    this.InitEncryptedStream();
                }

            }
            else if (type == SmtpMessageType.MAIL)
            {
                msg = new ReceivedMessage(GetAddress(message));
                if (VerifyCommand(SmtpMessageType.MAIL))
                    this.SendMessage("250 Requested mail action okay, completed");
                else
                {
                    this.Close($"421 {Config.Current.Domain} Service not available, closing transmission channel!");
                    return SmtpMessageType.QUIT;
                }
            }
            else if (type == SmtpMessageType.RCPT)
            {
                MailboxAddress recv = GetAddress(message);
                if (ExistMailbox(recv))
                {
                    msg.Receivers.Add(recv);
                    this.SendMessage("250 Requested mail action okay, completed");
                }
                else
                    this.SendMessage("550 Requested action not taken: mailbox unavailable");
            }
            else if (type == SmtpMessageType.DATA)
            {
                this.SendMessage("354 Start mail input; end with <CRLF>.<CRLF>");
                return SmtpMessageType.DATA;
            }
            else
            {
                this.Close($"421 {Config.Current.Domain} Service not available, closing transmission channel!");
                return SmtpMessageType.QUIT;
            }

            return type ?? SmtpMessageType.QUIT;
        }

        public void Close(String message)
        {
            if (this.IsConnected)
            {
                try
                {
                    this.SendMessage(message);
                    this.Client?.Close();
                }
                catch(IOException e)
                {

                }
            }
            this.OnDisconnect?.Invoke(this);
        }

        private void Clear()
        {
            this.OnDisconnect = null;
            this.OnMailArrived = null;

            if (this.IsConnected)
                this.Client?.Close();

            this.IsAuthenticated = false;
            this.ClientName = null;
            this.SslProtocol = SslProtocols.None;

            this.Stream?.Dispose();

            this.Client?.Dispose();
            this.Client = null;
        }

        public void Dispose()
        {
            this.Clear();
        }
        #endregion
    }
}
