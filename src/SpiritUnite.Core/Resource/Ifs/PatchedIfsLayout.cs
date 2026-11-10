namespace SpiritUnite.Resource.Ifs;

public sealed class PatchedIfsLayout(string originalPath, byte[] header, long originalPrefixLength, byte[] tail)
{
    private readonly string originalPath = originalPath;
    private readonly byte[] header = header;
    private readonly long originalPrefixLength = originalPrefixLength;
    private readonly byte[] tail = tail;

    public long Length => originalPrefixLength + tail.Length;

    public string Digest()
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(BitConverter.GetBytes(originalPrefixLength));
        hash.AppendData(tail);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public Stream OpenRead() => new OverlayStream(this);

    private sealed class OverlayStream : Stream
    {
        private readonly PatchedIfsLayout layout;
        private readonly FileStream original;
        private long position;

        public OverlayStream(PatchedIfsLayout layout)
        {
            this.layout = layout;
            original = new FileStream(layout.originalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.RandomAccess);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => layout.Length;

        public override long Position
        {
            get => position;
            set => position = value is >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Fill(buffer, out var originalOffset, out var originalCount);
            var read = 0;
            while (read < originalCount)
            {
                var got = RandomAccess.Read(original.SafeFileHandle, buffer.Slice(originalOffset + read, originalCount - read), position + originalOffset + read);
                if (got == 0)
                {
                    throw new EndOfStreamException("The original archive is shorter than the patched layout expects");
                }

                read += got;
            }

            position += count;
            return count;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Fill(buffer.Span, out var originalOffset, out var originalCount);
            if (originalCount > 0)
            {
                var read = 0;
                while (read < originalCount)
                {
                    var got = await RandomAccess.ReadAsync(original.SafeFileHandle, buffer.Slice(originalOffset + read, originalCount - read), position + originalOffset + read, cancellationToken);
                    if (got == 0)
                    {
                        throw new EndOfStreamException("The original archive is shorter than the patched layout expects");
                    }

                    read += got;
                }
            }

            position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                original.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Fill(Span<byte> buffer, out int originalOffset, out int originalCount)
        {
            originalOffset = originalCount = 0;
            var count = (int)Math.Clamp(Length - position, 0, buffer.Length);
            var written = 0;
            while (written < count)
            {
                var at = position + written;
                var remaining = count - written;
                if (at < layout.header.Length)
                {
                    var length = (int)Math.Min(remaining, layout.header.Length - at);
                    layout.header.AsSpan((int)at, length).CopyTo(buffer[written..]);
                    written += length;
                }
                else if (at < layout.originalPrefixLength)
                {
                    var length = (int)Math.Min(remaining, layout.originalPrefixLength - at);
                    originalOffset = written;
                    originalCount = length;
                    written += length;
                }
                else
                {
                    var tailOffset = (int)(at - layout.originalPrefixLength);
                    layout.tail.AsSpan(tailOffset, remaining).CopyTo(buffer[written..]);
                    written += remaining;
                }
            }

            return count;
        }
    }
}
