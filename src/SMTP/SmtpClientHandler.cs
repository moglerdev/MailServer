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
// You should have received a copy of the GNU General Public License along with this program. If not, see<https://www.gnu.org/licenses/>.

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
using System.Timers;

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

        #endregion

        #region Properties
        public TcpClient Client { get; private set; }
        protected Stream Stream { get; private set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        #endregion

        #region Events
        public event MailArrived OnMailArrived;
        public event ClientDisconnect OnDisconnect;
        #endregion

        #region Readonly
        private readonly Socket _clientSocket;
        private readonly Timer _timer = new Timer(30000);
        #endregion

        #region Instance
        private Encoding _encoder = Encoding.UTF8;
        private Boolean _isData = false;
        private Byte[] _buffer = new byte[bufferSize];
        private ReceivedMessage _currentMail;
        private MemoryStream _memoryBuffer = null;
        #endregion

        #region Constructor
        public SmtpClientHandler(TcpClient client)
        {
            this.Client = client;
            this._clientSocket = client.Client;
            this.Stream = client.GetStream();

            this.OnMailArrived += this.MailArrived;

            this._timer.Elapsed += this.CheckConnection;
            this._timer.AutoReset = false;

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

        private Boolean VerifyCommand(SmtpMessageType type, String message)
        {
            // TODO: Verify Command and Send specified error, when command not valid!

            return false;
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

                    this.BeginReadMessage();
                }
                else
                {
                    if (type == SmtpMessageType.QUIT)
                    {
                        this.Close($"221 {Config.Current.Domain} Service closing transmission channel");
                    }
                    else if (type == SmtpMessageType.HELO || type == SmtpMessageType.EHLO)
                    {
                        //var spfValidator = new ARSoft.Tools.Net.Spf.SpfValidator();
                        this.SendMessage($"250-{Config.Current.Domain} Hello [{this._clientSocket.RemoteEndPoint.ToString()}]");

                        if (type == SmtpMessageType.EHLO)
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
                        tlsStream.AuthenticateAsServer(MailTransferAgent.Certificate, false, System.Security.Authentication.SslProtocols.Tls12, false);
                        this.Stream = tlsStream;

                        this.BeginReadMessage();
                    }
                    else if (type == SmtpMessageType.MAIL)
                    {
                        this._currentMail = new ReceivedMessage(GetAddress(message));
                        this.SendMessage("250 Requested mail action okay, completed");

                        this.BeginReadMessage();
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

                        this.BeginReadMessage();
                    }
                    else if (type == SmtpMessageType.DATA)
                    {
                        this._isData = true;
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
            catch(IOException e)
            {

            }
            catch(Exception e)
            {
                this.BeginReadMessage();
            }            
        }

        public void Close(String message)
        {
            if (this.IsConnected)
            {
                this.SendMessage(message);
                this.Client?.Close();
            }
            this.OnDisconnect?.Invoke(this);

            this.Clear();
        }

        private void Clear()
        {
            this.OnDisconnect = null;
            this.OnMailArrived = null;

            if (this.IsConnected)
                this.Client?.Close();

            this.Stream?.Dispose();

            this.Client?.Dispose();
            this.Client = null;
            this._currentMail = null;
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
