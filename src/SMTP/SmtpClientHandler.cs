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

// TODO:
//  - Add Authentication
//  - Validate / Verify commands
//  - Validate / Verify connected client
//  - Add Whitelist
//  - Handle send commands better

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
        RSET = 7,
        AUTH = 8,
    }

    delegate void MailArrived(ReceivedMessage mail);
    delegate void ClientDisconnect(SmtpClientHandler client);

    class SmtpClientHandler : IDisposable
    {
        #region Static
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
        protected Stream Stream { get; private set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        public String ClientName { get; private set; }
        public String ClientDomain { get; private set; }
        public IPAddress ClientAddress { get => (this._clientSocket.RemoteEndPoint as IPEndPoint).Address; }
        public Boolean IsAuthenticated { get; private set; }
        public SslProtocols Encryption { get; private set; } = SslProtocols.None;
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
        private Boolean _isData = false;
        private Byte[] _buffer = new byte[bufferSize];
        private ReceivedMessage _currentMail;
        private MemoryStream _memoryBuffer = null;
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

            this.SendMessage($"220 {Config.Current.Domain} ESMTP MAIL Service ready at {DateTimeOffset.Now.ToString()}");

            this.BeginReadMessage();
        }
        #endregion

        #region Methods
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

        private IAsyncResult BeginReadMessage()
        {
            this._timer.Start();
            try
            {
                lock (this._buffer)
                    return this.Stream.BeginRead(this._buffer, 0, this._buffer.Length, new AsyncCallback(this.ReceiveMessageCallback), this.Stream);
            }
            catch (Exception e)
            {
                if (this.IsConnected)
                {
                    this.Close($"451 Requested action aborted: local error in processing");
                }
            }

            return null;
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
                    return this.Encryption == SslProtocols.None;

                if (type == SmtpMessageType.AUTH)
                    return this.IsDeliverService && this.Encryption != SslProtocols.None;

                if (type == SmtpMessageType.MAIL)
                {
                    if (this.IsAuthenticated)
                    {
                        return true; // TODO: Test SPF-DSN and MX-Record and check when E-Mail is outgo, is the User authenticated
                    }
                    return DnsHelper.CheckSpf(this._currentMail.Sender.Address, this.ClientAddress);
                }

                if (type == SmtpMessageType.RCPT)
                    return this._currentMail != null;

                if (type == SmtpMessageType.DATA)
                    return this._currentMail != null && this._currentMail.Receivers.Any();

                if (type == SmtpMessageType.RSET)
                    return true;
            }

            return false;
        }

        protected virtual void InitEncryptedStream(SslProtocols protocols = SslProtocols.Tls12 | SslProtocols.Tls13)
        {
            SslStream encryptedStream = new SslStream(this.Client.GetStream(), false, new RemoteCertificateValidationCallback((sender, cert, chain, ssl) => true));
            encryptedStream.AuthenticateAsServer(MailTransferAgent.Certificate, false, protocols, false); // TODO: Über Config die erlaubten Verschlüsselungen einstellen
            this.Encryption = encryptedStream.SslProtocol;
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

        private void ReceiveMessageCallback(IAsyncResult result)
        {
            this._timer.Stop();
            try
            {
                Int32 readBytes = this.Stream.EndRead(result);

                if (readBytes == 0)
                {
                    System.Threading.Thread.Sleep(250);
                    this.BeginReadMessage();
                    return;
                }

                if (readBytes < 2)
                {
                    if (this._memoryBuffer == null)
                        this._memoryBuffer = new MemoryStream();
                    lock (this._buffer)
                        this._memoryBuffer.Write(this._buffer, 0, readBytes);
                    this.BeginReadMessage();
                    return;
                }

                Byte[] _buffer = null;
                lock (this._buffer)
                {
                    if (this._memoryBuffer == null)
                        this._memoryBuffer = new MemoryStream();

                    this._memoryBuffer.Write(this._buffer, 0, readBytes);

                    _buffer = this._memoryBuffer.ToArray();
                    Int32 seek = _buffer.Length;

                    if (( !this._isData || ( seek > 4 && _buffer[seek - 5] == (byte)'\r' && _buffer[seek - 4] == (byte)'\n'
                        && _buffer[seek - 3] == (byte)'.' ) )
                        && _buffer[seek - 2] == (byte)'\r' && _buffer[seek - 1] == (byte)'\n')
                    {
                        this._memoryBuffer.Dispose();
                        this._memoryBuffer = null;
                    }
                    else
                    {
                        this.BeginReadMessage();
                        return;
                    }
                }

                String message = this._encoder.GetString(_buffer);
                SmtpMessageType? type = null;

                try
                {
                    type = GetMessageType(message);
                    Console.WriteLine("<Client>:" + message);
                }
                catch (Exception e)
                {
                    if (!this._isData)
                    {
                        this.SendMessage("500 Syntax error, command unrecognised");
                        this.BeginReadMessage();
                        return;
                    }
                }

                if (type == SmtpMessageType.RSET)
                {
                    this._isData = false;
                    this._memoryBuffer?.Dispose();
                    this._memoryBuffer = null;

                    this._currentMail = null;

                    this.SendMessage("250 Requested mail action okay, completed");
                    this.BeginReadMessage();
                    return;
                }


                if (this._isData)
                {
                    using (MemoryStream ms = new MemoryStream(_buffer))
                        this._currentMail.MimeMessage = MimeMessage.Load(ms);

                    Console.WriteLine("<Client>: {MIME MESSAGE}");

                    this.OnMailArrived?.Invoke(this._currentMail);
                    this.SendMessage("250 Requested mail action okay, completed");
                    this._isData = false;
                }
                else
                {
                    if (type == SmtpMessageType.QUIT)
                    {
                        this.Close($"221 {Config.Current.Domain} Service closing transmission channel");
                        return;
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
                        this._currentMail = new ReceivedMessage(GetAddress(message));
                        if(VerifyCommand(SmtpMessageType.MAIL))
                            this.SendMessage("250 Requested mail action okay, completed");
                        else
                        {
                            this.Close($"421 {Config.Current.Domain} Service not available, closing transmission channel!");
                            return;
                        }
                    }
                    else if (type == SmtpMessageType.RCPT)
                    {
                        MailboxAddress recv = GetAddress(message);
                        if (ExistMailbox(recv))
                        {
                            this._currentMail.Receivers.Add(recv);
                            this.SendMessage("250 Requested mail action okay, completed");
                        }
                        else
                            this.SendMessage("550 Requested action not taken: mailbox unavailable");
                    }
                    else if (type == SmtpMessageType.DATA)
                    {
                        this._isData = true;
                        this.SendMessage("354 Start mail input; end with <CRLF>.<CRLF>");
                    }
                    else
                    {
                        this.Close($"421 {Config.Current.Domain} Service not available, closing transmission channel!");
                        return;
                    }
                }
                this.BeginReadMessage();
            }
            catch(IOException e)
            {

            }
            catch(Exception e)
            {
                this.SendMessage("451 Requested action aborted: local error in processing");
                this.BeginReadMessage();
            }            
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
            this.Encryption = SslProtocols.None;
            this._currentMail = null;

            this.Stream?.Dispose();

            this.Client?.Dispose();
            this.Client = null;
            this._memoryBuffer?.Dispose();
            this._memoryBuffer = null;
            this._buffer = null;
        }

        public void Dispose()
        {
            this.Clear();
        }
        #endregion
    }
}
