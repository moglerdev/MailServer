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

using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;


namespace MailServer.MTA {
    class ReceivedMessage
    {
        public MailboxAddress Sender { get; set; }
        public List<MailboxAddress> Receivers { get; set; }
        public MimeMessage MimeMessage { get; set; }

        public ReceivedMessage(MailboxAddress sender)
        {
            this.Receivers = new List<MailboxAddress>();
            this.Sender = sender;
        }

        public void Save()
        {
            foreach (MailboxAddress recv in this.Receivers)
            {
                String file = this.GetClearFilename() + ".eml";
                String path = Path.Combine(recv.Address, this.Sender.Address);
                Directory.CreateDirectory(path);
                path = Path.Combine(path, file);
                this.MimeMessage.WriteTo(path);
            }
        }

        private String GetClearFilename()
        {
            String name = this.MimeMessage.MessageId ?? Guid.NewGuid().ToString();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name.Replace(invalid, '_');
            }
            return name;
        }
    }
}
