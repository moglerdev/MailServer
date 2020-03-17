using MailServer.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace MailServer.Common.Base {
    public delegate void ClientDisconnect(IClientHandler client);

    public abstract class ClientHandlerBase : IClientHandler, IDisposable {
        public const int BufferSize = 4069;

        public String ClassName { get; set; } // TODO

        public TcpClient Client { get; protected set; }
        public Stream Stream { get; protected set; }
        public Boolean IsConnected { get => this.Client != null && this.Client.Connected; }
        public IPAddress ClientAddress { get => ( this._clientSocket?.RemoteEndPoint as IPEndPoint ).Address; }
        public Boolean IsAuthenticated { get; protected set; }
        public SslProtocols SslProtocol { get; protected set; } = SslProtocols.None;

        public Encoding Encoding { get; protected set; } = Encoding.UTF8;

        private readonly Socket _clientSocket;
        private readonly Timer _timer = new Timer(30000);
        public event ClientDisconnect OnDisconnect;

        public ClientHandlerBase(TcpClient client, SslProtocols sslProtocols = SslProtocols.None)
        {
            this.Client = client;
            this._clientSocket = client.Client;

            if (sslProtocols != SslProtocols.None)
            {
                this.InitEncryptedStream(sslProtocols);
            }

            this.Stream = client.GetStream();

            this._timer.Elapsed += this.TimeoutConnection;
            this._timer.AutoReset = false;
        }

        public virtual void Start()
        {
            this.SendWelcomeMessage();
            this.ListenForData();
        }

        public virtual async Task StartAsync()
        {
            await Task.Run(this.Start);
        }

        protected void Send(String message)
        {
            Log.WriteLine(LogType.Debug, "SmtpClientHanlder", "SendMessage", "<Server>:{0}", message);
            this.Stream.Write(this.Encoding.GetBytes(message + "\r\n"));
        }

        protected Int32 Read(Byte[] buffer, Int32 offset, Int32 length)
        {
            this._timer.Start();
            try
            {
                return this.Stream.Read(buffer, offset, length);
            }
            catch (ObjectDisposedException e)
            {
                Log.WriteLine(LogType.Debug, "ClientHandlerBase", "Read", e.ToString());
            }
            catch (Exception e)
            {

            }
            finally
            {
                this._timer.Stop();
            }
            return 0;
        }

        protected abstract void ListenForData();

        protected virtual Boolean IsEndOfLine(byte[] buffer, Int32 pos)
        {
            if (pos < 1)
                return false;
            return buffer[pos - 1] == (byte)'\r' && buffer[pos] == (byte)'\n';
        }

        protected virtual Boolean IsEndOfData(byte[] buffer, Int32 pos)
        {
            if (pos < 4)
                return false;

            return buffer[pos - 4] == (byte)'\r' && buffer[pos - 3] == (byte)'\n' &&
                                buffer[pos - 2] == (byte)'.' &&
                                buffer[pos - 1] == (byte)'\r' && buffer[pos] == (byte)'\n';
        }

        public abstract void SendWelcomeMessage();

        protected virtual void InitEncryptedStream(SslProtocols protocols = SslProtocols.Tls12 | SslProtocols.Tls13)
        {
            SslStream encryptedStream = new SslStream(this.Client.GetStream(), false, new RemoteCertificateValidationCallback((sender, cert, chain, ssl) => true));
            encryptedStream.AuthenticateAsServer(Config.Current.Certificate.Certificate, false, protocols, false); // TODO: Über Config die erlaubten Verschlüsselungen einstellen
            this.SslProtocol = encryptedStream.SslProtocol;
            this.Stream = encryptedStream;
        }

        protected virtual void TimeoutConnection(object sender, EventArgs eventArgs)
        {
            this._timer.Stop();
        }

        protected void Disconnected()
        {
            this.OnDisconnect?.Invoke(this);
        }

        public void Close(String message)
        {
            if (this.IsConnected)
            {
                try
                {
                    this.Send(message);
                    this.Client?.Close();
                }
                catch (IOException e)
                {

                }
            }
            this.OnDisconnect?.Invoke(this);
        }

        protected virtual void Clear()
        {
            this.OnDisconnect = null;

            if (this.IsConnected)
                this.Client?.Close();

            this.IsAuthenticated = false;
            this.SslProtocol = SslProtocols.None;

            this.Stream?.Dispose();

            this.Client?.Dispose();
            this.Client = null;
        }

        public void Dispose()
        {
            this.Clear();
        }
    }
}
