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

using MailKit.Net.Smtp;
using MailServer.Common;
using MailServer.SMTP;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MailServer {
    /*
     * https://www.greenend.org.uk/rjk/tech/smtpreplies.html
     */
    class Program {
        public static X509Certificate Certificate { get; private set; }

        static List<SmtpClientHandler> clientList = new List<SmtpClientHandler>();

        static void Main(string[] args)
        {
            Config.Current = Newtonsoft.Json.JsonConvert.DeserializeObject<Config>(File.ReadAllText("Config.json"));

            Certificate = new X509Certificate2(Config.Current.Certificate.Filename, Config.Current.Certificate.Password, X509KeyStorageFlags.MachineKeySet);

            Test();

            IPAddress adr;
            if (!IPAddress.TryParse(Config.Current.Listen, out adr))
                adr = IPAddress.Any;

            IPEndPoint endPoint = new IPEndPoint(adr, 25);
            TcpListener listener = new TcpListener(endPoint);
            listener.AllowNatTraversal(true);
            listener.Start();
            while (true)
            {
                SmtpClientHandler client = new SmtpClientHandler(listener.AcceptTcpClient());
                client.OnDisconnect += ClientDisconnect;
                clientList.Add(client);
            }
        }

        static void Test()
        {
            Task clientTask = Task.Run(() =>
            {
                using (SmtpClient _client = new SmtpClient())
                {
                    _client.Connect("localhost", 25, MailKit.Security.SecureSocketOptions.StartTls);

                    MimeMessage msg = new MimeMessage();
                    msg.From.Add(new MailboxAddress("test", "test@test.de"));
                    msg.To.Add(new MailboxAddress("Test-Email", "debug@debug"));
                    msg.Subject = "Test";

                    BodyBuilder bb = new BodyBuilder();
                    bb.HtmlBody = "<h2>Hello World</h2>";

                    msg.Body = bb.ToMessageBody();

                    _client.Send(msg);

                    _client.Disconnect(true);
                }

                Console.WriteLine("Test is finished!");
            });
        }

        static void ClientDisconnect(SmtpClientHandler smtp)
        {
            Console.WriteLine("Client disconnected!");
            smtp.Dispose();
            clientList.Remove(smtp);
        }
    }
}