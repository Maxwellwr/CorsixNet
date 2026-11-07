namespace CorsixNet.Sim.Serialization;

/// <summary>
/// A stream that computes a 64-bit FNV-1a hash of the bytes written to it and stores nothing.
/// Used to hash serialized simulation state for desync detection.
/// </summary>
public sealed class StateHash : Stream
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private long _length;

    public ulong Value { get; private set; } = OffsetBasis;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var hash = Value;
        foreach (var b in buffer)
        {
            hash = unchecked((hash ^ b) * Prime);
        }

        Value = hash;
        _length += buffer.Length;
    }

    public override void WriteByte(byte value) => Write([value]);

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
