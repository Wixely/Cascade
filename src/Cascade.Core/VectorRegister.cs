using System.Buffers.Binary;

namespace Cascade.Core;

/// <summary>Hardware-independent storage for one architectural 512-bit register.</summary>
public sealed class VectorRegister
{
    public const int ByteCount = 64;
    private readonly byte[] _bytes = new byte[ByteCount];

    public ReadOnlySpan<byte> Bytes => _bytes;

    public void Clear() => Array.Clear(_bytes);

    public void CopyFrom(VectorRegister source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source._bytes.CopyTo(_bytes, 0);
    }

    public uint ReadUInt32(int lane) => BinaryPrimitives.ReadUInt32LittleEndian(Lane(lane, 4, 16));
    public int ReadInt32(int lane) => unchecked((int)ReadUInt32(lane));
    public ulong ReadUInt64(int lane) => BinaryPrimitives.ReadUInt64LittleEndian(Lane(lane, 8, 8));
    public long ReadInt64(int lane) => unchecked((long)ReadUInt64(lane));

    public void WriteUInt32(int lane, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(MutableLane(lane, 4, 16), value);
    public void WriteUInt64(int lane, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(MutableLane(lane, 8, 8), value);

    public void ReadFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length != ByteCount) throw new ArgumentException("A vector register is exactly 64 bytes.", nameof(source));
        source.CopyTo(_bytes);
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length != ByteCount) throw new ArgumentException("A vector register is exactly 64 bytes.", nameof(destination));
        _bytes.CopyTo(destination);
    }

    public byte ReadByte(int index) => _bytes[index];
    public void WriteByte(int index, byte value) => _bytes[index] = value;

    private ReadOnlySpan<byte> Lane(int lane, int width, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lane);
        if (lane >= count) throw new ArgumentOutOfRangeException(nameof(lane));
        return _bytes.AsSpan(lane * width, width);
    }

    private Span<byte> MutableLane(int lane, int width, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lane);
        if (lane >= count) throw new ArgumentOutOfRangeException(nameof(lane));
        return _bytes.AsSpan(lane * width, width);
    }
}
