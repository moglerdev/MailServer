using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Text;

namespace MailServer.Common.IO {
    public class MailStream : Stream {
        protected const Int32 BufferSize = 4096;

        protected Stream sourceStream { get; set; }
        protected Encoding encoding { get; set; }

        public SslProtocols SslProtocols { get; protected set; }

        public override Boolean CanRead => this.sourceStream.CanRead;

        public override Boolean CanSeek => this.sourceStream.CanSeek;

        public override Boolean CanWrite => this.sourceStream.CanWrite;

        public override Int64 Length => this.sourceStream.Length;

        public override Int64 Position { get => this.sourceStream.Position; set => this.sourceStream.Position = value; }

        public MailStream(Stream source, SslProtocols sslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13)
        {
            this.sourceStream = source;
            this.SslProtocols = sslProtocols;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count)
        {
            throw new NotImplementedException();
        }
        public override void Write(Byte[] buffer, Int32 offset, Int32 count)
        {
            throw new NotImplementedException();
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(Int64 value)
        {
            throw new NotImplementedException();
        }

    }
}
