using System.Security.Cryptography;

namespace ObjeX.Core.Utilities;

/// <summary>
/// A read-only stream wrapper that computes an MD5 hash over all bytes read from the inner stream.
/// Call <see cref="GetETag"/> after the stream is fully consumed to retrieve the hex-encoded hash.
/// </summary>
public sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    public HashingStream(Stream inner) => _inner = inner;

    public string GetETag()
    {
        var bytes = _hash.GetCurrentHash();
        return Convert.ToHexString(bytes).ToLower();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        if (bytesRead > 0) _hash.AppendData(buffer, offset, bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        if (bytesRead > 0) _hash.AppendData(buffer, offset, bytesRead);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0) _hash.AppendData(buffer.Span[..bytesRead]);
        return bytesRead;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }
}
