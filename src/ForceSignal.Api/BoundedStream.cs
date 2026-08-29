namespace ForceSignal.Api;

/// <summary>
/// Wraps a request body so it cannot deliver more than the caller said it would. Content-Length is
/// a claim the client makes, not a fact, so an upload that keeps going past the ceiling is cut off
/// here rather than being read to its end.
/// </summary>
internal sealed class BoundedStream(Stream inner, long maxBytes) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Track(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Track(int count)
    {
        _read += count;
        if (_read > maxBytes)
        {
            throw new InvalidOperationException(
                $"That snapshot is larger than the {maxBytes / (1024 * 1024)}MB a match file can be.");
        }

        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
