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
using MailServer.Common.Base;

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
        UNKOWN = 0,
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

    public class SmtpClientHandler : ClientHandlerBase, IDisposable {
        private static SmtpMessageType GetMessageType(String message)
        {
            try
            {
                if (message.Length > 7 && message.Substring(0, 8) == "STARTTLS")
                {
                    return SmtpMessageType.STARTTLS;
                }

                if (message.Length > 3)
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


        public event MailArrived OnMailArrived;

        public String ClientName { get; private set; }
        public String ClientDomain { get; private set; }

        public readonly Boolean IsDeliverService;

        public SmtpClientHandler(TcpClient client, Boolean isDeliverService = false, SslProtocols sslProtocols = SslProtocols.None)
            : base(client, sslProtocols)
        {
            this.IsDeliverService = isDeliverService;

            this.OnMailArrived += this.MailArrived;

            var lookup = new DnsClient.LookupClient();
            var query  = lookup.QueryReverse(this.ClientAddress);
            this.ClientDomain = query.Answers.PtrRecords().FirstOrDefault()?.PtrDomainName;
        }

        public override void SendWelcomeMessage()
        {
            this.Send($"220 {Config.Current.Domain} ESMTP MAIL Service ready at {DateTimeOffset.Now.ToString()}");
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
            if (this.IsDeliverService)
                exentsions.Add("AUTH LOGIN PLAIN");

            for (int i = 0; i < exentsions.Count; ++i)
            {
                this.Send($"250{( exentsions.Count - 1 < i ? "-" : " " )}" + exentsions[i]);
            }
        }

        ReceivedMessage msg = null;
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
                        if (isData && i + 4 < msBuffer.Length)
                        {
                            if (this.IsEndOfData(buffer, i))
                            {
                                using (MemoryStream mimeMs = new MemoryStream(ms.ToArray()))
                                    msg.MimeMessage = MimeMessage.Load(mimeMs); // TODO: <CLRF>.<CLRF> wird in die Mime mit geschrieben

                                this.OnMailArrived?.Invoke(msg);

                                this.Send("250 Requested mail action okay, completed");

                                Log.WriteLine(LogType.Debug, "SmtpClientHandler", "ReceivingData", "<{0}>: {MIME MESSAGE}", this.ClientAddress.ToString());

                                ms.Dispose();
                                ms = null;

                                isData = false;
                            }
                        }
                        else if (i + 2 < msBuffer.Length)
                        {
                            if (this.IsEndOfLine(buffer, i))
                            {
                                String message = this.Encoding.GetString(ms.ToArray());

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
            }
            ms?.Dispose();
            this.Disconnected();
        }


        private SmtpMessageType HandleCommand(String message)
        {
            SmtpMessageType? type = null;

            try
            {
                type = GetMessageType(message);
                Log.WriteLine(LogType.Debug, "SmtpClientHandler", "HandleCommand", "<Client>:{0}", message);
            }
            catch (Exception e)
            {
                type = SmtpMessageType.UNKOWN;
            }

            if (type == SmtpMessageType.QUIT)
            {
                this.Close($"221 {Config.Current.Domain} Service closing transmission channel");
                return SmtpMessageType.QUIT;
            }
            else if (type == SmtpMessageType.HELO || type == SmtpMessageType.EHLO)
            {
                this.ClientName = message.Substring(4, message.Length - 4).Trim();
                this.Send($"250-{Config.Current.Domain} Hello [{this.ClientAddress.ToString()}]");

                if (type == SmtpMessageType.EHLO)
                    this.SendExentsions();
            }
            else if (type == SmtpMessageType.STARTTLS)
            {
                if (this.VerifyCommand(SmtpMessageType.STARTTLS))
                {
                    this.Send("220 SMTP server ready");
                    this.InitEncryptedStream();
                }

            }
            else if (type == SmtpMessageType.MAIL)
            {
                msg = new ReceivedMessage(GetAddress(message));
                if (VerifyCommand(SmtpMessageType.MAIL))
                    this.Send("250 Requested mail action okay, completed");
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
                    this.Send("250 Requested mail action okay, completed");
                }
                else
                    this.Send("550 Requested action not taken: mailbox unavailable");
            }
            else if (type == SmtpMessageType.DATA)
            {
                this.Send("354 Start mail input; end with <CRLF>.<CRLF>");
            }
            else if (type == SmtpMessageType.UNKOWN)
            {
                // Syntax error (also a command line may be too long). The server cannot recognize the command
                this.Send($"500 {Config.Current.Domain} Syntax error (also a command line may be too long). The server cannot recognize the command!");
            }
            else
            {
                this.Close($"421 {Config.Current.Domain} Service not available, closing transmission channel!");
                return SmtpMessageType.QUIT;
            }

            return type ?? SmtpMessageType.UNKOWN;
        }

        protected override void Clear()
        {
            this.OnMailArrived = null;
            this.ClientName = null;

            base.Clear();
        }
    }
}
