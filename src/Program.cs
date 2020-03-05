using MailKit.Net.Smtp;
using MailServer.SMTP;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MailServer {
    /*
     * https://www.greenend.org.uk/rjk/tech/smtpreplies.html
     * https://mxtoolbox.com/SuperTool.aspx?action=mx%3adotbehindyou.de&run=toolpage
     *      SMTP TLS
     *      SMTP Open Relay
     *      SMTP Banner
     */
    class Program {
        static List<SmtpClientHandler> clientList = new List<SmtpClientHandler>();

        static void Main(string[] args)
        {
            Console.Write("IP:");
            String _adr = Console.ReadLine();

            Test();

            IPAddress adr;
            if (!IPAddress.TryParse(_adr, out adr))
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

            //Console.WriteLine("Hello World!");
        }

        static void Test()
        {
            Task clientTask = Task.Run(() =>
            {
                using (SmtpClient _client = new SmtpClient())
                {
                    _client.Connect("localhost", 25, MailKit.Security.SecureSocketOptions.None);

                    MimeMessage msg = new MimeMessage();
                    msg.From.Add(new MailboxAddress("test", "test@test.de"));
                    msg.To.Add(new MailboxAddress("chris", "chris@chris.de"));
                    msg.Subject = "Test";

                    BodyBuilder bb = new BodyBuilder();
                    bb.HtmlBody = "<h2>Hello World</h2>";

                    msg.Body = bb.ToMessageBody();

                    _client.Send(msg);

                    //_client.Disconnect(true);
                }

                Console.WriteLine("Client is fin!");
            });
        }

        static void ClientDisconnect(SmtpClientHandler smtp)
        {
            smtp.Dispose();
            clientList.Remove(smtp);
        }
    }
}