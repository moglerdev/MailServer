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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MailServer.MTA {
    public class SmtpCommand {
        public static SmtpCommand ServiceReady = new SmtpCommand(220, $"{Config.Current.Domain} ESMTP MAIL Service ready at {DateTimeOffset.Now.ToString()}");
        public static SmtpCommand Ok = new SmtpCommand(250, "Requested mail action okay, completed");
        public static SmtpCommand LocalError = new SmtpCommand(451, "Requested action aborted: local error in processing");
        public static SmtpCommand SyntaxError = new SmtpCommand(500, $"{Config.Current.Domain} Syntax error (also a command line may be too long). The server cannot recognize the command!");
        public static SmtpCommand ServiceError = new SmtpCommand(421, $"{Config.Current.Domain} Service not available, closing transmission channel!");
        public static SmtpCommand Close = new SmtpCommand(221, $"{Config.Current.Domain} Service closing transmission channel");
        public static SmtpCommand MailboxNotAvailable = new SmtpCommand(550, "Requested action not taken: mailbox unavailable");
        public static SmtpCommand DataReady = new SmtpCommand(354, "Start mail input; end with <CRLF>.<CRLF>");
        public static SmtpCommand WelcomeMessage = new SmtpCommand(250, $"{Config.Current.Domain} Hello [{{ClientAddress}}]");

        public static SmtpCommand ExtensionStartTls = new SmtpCommand(250, "STARTTLS", false);
        public static SmtpCommand ExtensionAuthLogin = new SmtpCommand(250, "AUTH LOGIN PLAIN", false);

        public Int32 StatusCode { get; set; }
        public String ExtendedStatusCode { get; set; }
        public String Message { get; set; }
        public Boolean EndOfMessage { get; set; } = true;

        public SmtpCommand(Int32 statusCode, String message, Boolean endOfMessage = true)
        {
            this.StatusCode = statusCode;
            this.Message = message;
            this.EndOfMessage = endOfMessage;
        }

        public SmtpCommand(Int32 statusCode, String message, String extendedStatusCode, Boolean endOfMessage = true)
            : this(statusCode: statusCode, message: message, endOfMessage: endOfMessage)
        {
            this.ExtendedStatusCode = extendedStatusCode;
        }

        public String ToString(IEnumerable<SmtpCommand> commands)
        {
            SmtpCommand[] arrCmd = commands.ToArray();

            this.EndOfMessage = arrCmd.Length == 0;
            String txt = this.ToString();

            for (int i = 0; i < arrCmd.Length; ++i)
            {
                SmtpCommand cmd = arrCmd[i];
                cmd.EndOfMessage = i + 1 == arrCmd.Length;
                txt += $"\r\n{cmd.ToString()}";
            }

            return txt;
        }

        public override String ToString()
        {
            return $"{StatusCode}{(EndOfMessage ? " " : "-")}{(String.IsNullOrEmpty(ExtendedStatusCode) ? "" : ExtendedStatusCode + " ")}{Message}";
        }
    }
}
