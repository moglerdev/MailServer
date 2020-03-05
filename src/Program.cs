using MailKit.Net.Smtp;
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
            /*
            Task clientTask = new Task(() =>
            {
                using(SmtpClient _client = new SmtpClient())
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

                    _client.Disconnect(true);
                }

                Console.WriteLine("Client is fin!");
            });
            */

            Console.Write("IP:");
            String _adr = Console.ReadLine();

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
        

        static void ClientDisconnect(SmtpClientHandler smtp)
        {
            smtp.Dispose();
            clientList.Remove(smtp);
        }
    }
}


delegate void MailArrived(Mail mail);
delegate void ClientDisconnect(SmtpClientHandler client);

class SmtpClientHandler : IDisposable {
    const int bufferSize = 4069;

    private static SmtpMessageType GetMessageType(String message)
    {
        try
        {
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
        catch(Exception e) { }
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

    event MailArrived OnMailArrived;
    public event ClientDisconnect OnDisconnect;

    public TcpClient Client { get; private set; }
    protected NetworkStream Stream { get; private set; }

    private Encoding encoder = Encoding.UTF8;

    public SmtpClientHandler(TcpClient client)
    {
        this.Client = client;
        this.Stream = client.GetStream();

        this.OnMailArrived += this.MailArrived;

        this.SendMessage("220 Hello MyServer!");

        this.BeginReadMessage();
    }

    private Boolean isData = false;

    protected virtual void MailArrived(Mail mail)
    {

    }

    private Byte[] buffer = new byte[bufferSize];
    private IAsyncResult BeginReadMessage()
    {
        lock(this.buffer)
            return this.Stream.BeginRead(this.buffer, 0, this.buffer.Length, new AsyncCallback(this.ReceiveMessageCallback), this.Stream);
    }

    private void SendMessage(String message)
    {
        Console.WriteLine("[Server]:" + message);
        this.Stream.Write(this.encoder.GetBytes(message + "\r\n"));
    }

    private Mail currentMail;
    MemoryStream memoryBuffer = null;
    private void ReceiveMessageCallback(IAsyncResult result)
    {
        Int32 readBytes = this.Stream.EndRead(result);

        if (readBytes < 2)
        {
            if (memoryBuffer == null)
                memoryBuffer = new MemoryStream();
            lock(this.buffer)
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

            Console.WriteLine("<Client>:" + this.currentMail.MimeMessage.ToString());

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
                // TODO EHLO -> Return Extensions
                this.SendMessage("250 Requested mail action okay, completed");

                this.BeginReadMessage();
            }
            else if (type == SmtpMessageType.MAIL)
            {
                currentMail = new Mail(GetAddress(message));
                this.SendMessage("250 Requested mail action okay, completed");

                this.BeginReadMessage();
            }
            else if (type == SmtpMessageType.RCPT)
            {
                this.currentMail.To.Add(GetAddress(message));
                this.SendMessage("250 Requested mail action okay, completed");

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
                this.SendMessage("421 <domain> Service not available, closing transmission channel");

                this.BeginReadMessage();
            }
        }
    }

    public void Close()
    {
        this.OnDisconnect?.Invoke(this);
        if(this.Client != null && this.Client.Connected)
            this.SendMessage("221 <domain> Service closing transmission channel");

        this.Client?.Close();
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

enum SmtpMessageType {
    QUIT = 0,
    HELO = 1,
    EHLO = 2,
    MAIL = 3,
    RCPT = 4,
    DATA = 5,
}

class Mail {
    public Mail(MailboxAddress from)
    {
        this.From = from;
    }

    public MailboxAddress From { get; set; }
    public List<MailboxAddress> To { get; set; } = new List<MailboxAddress>();
    public MimeMessage MimeMessage { get; set; }
}