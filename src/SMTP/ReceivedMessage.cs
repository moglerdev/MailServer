using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MailServer.SMTP
{
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
