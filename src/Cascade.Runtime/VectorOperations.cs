using System.Numerics;
using System.Runtime.InteropServices;
using Cascade.Core;

namespace Cascade.Runtime;

internal interface IVectorOperations
{
    void SplatI32(VectorRegister destination, uint value);
    void SplatI64(VectorRegister destination, ulong value);
    void BinaryI32(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right);
    void BinaryI64(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right);
    void Bitwise(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right);
    void Not(VectorRegister destination, VectorRegister source);
    ulong CompareI32(Opcode opcode, VectorRegister left, VectorRegister right);
    ulong CompareI64(Opcode opcode, VectorRegister left, VectorRegister right);
    void SelectI32(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask);
    void SelectI64(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask);
}

internal sealed class ScalarVectorOperations : IVectorOperations
{
    internal static ScalarVectorOperations Instance { get; } = new();

    public void SplatI32(VectorRegister destination, uint value)
    {
        for (var lane = 0; lane < 16; lane++) destination.WriteUInt32(lane, value);
    }

    public void SplatI64(VectorRegister destination, ulong value)
    {
        for (var lane = 0; lane < 8; lane++) destination.WriteUInt64(lane, value);
    }

    public void BinaryI32(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<uint> values = stackalloc uint[16];
        for (var lane = 0; lane < values.Length; lane++)
        {
            var a = left.ReadUInt32(lane);
            var b = right.ReadUInt32(lane);
            values[lane] = opcode switch
            {
                Opcode.AddI32 => unchecked(a + b),
                Opcode.SubI32 => unchecked(a - b),
                Opcode.MulI32 => unchecked(a * b),
                _ => throw Unexpected(opcode),
            };
        }
        for (var lane = 0; lane < values.Length; lane++) destination.WriteUInt32(lane, values[lane]);
    }

    public void BinaryI64(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<ulong> values = stackalloc ulong[8];
        for (var lane = 0; lane < values.Length; lane++)
        {
            var a = left.ReadUInt64(lane);
            var b = right.ReadUInt64(lane);
            values[lane] = opcode switch
            {
                Opcode.AddI64 => unchecked(a + b),
                Opcode.SubI64 => unchecked(a - b),
                _ => throw Unexpected(opcode),
            };
        }
        for (var lane = 0; lane < values.Length; lane++) destination.WriteUInt64(lane, values[lane]);
    }

    public void Bitwise(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var index = 0; index < values.Length; index++)
        {
            var a = left.ReadByte(index);
            var b = right.ReadByte(index);
            values[index] = opcode switch
            {
                Opcode.And => (byte)(a & b),
                Opcode.Or => (byte)(a | b),
                Opcode.Xor => (byte)(a ^ b),
                _ => throw Unexpected(opcode),
            };
        }
        destination.ReadFrom(values);
    }

    public void Not(VectorRegister destination, VectorRegister source)
    {
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var index = 0; index < values.Length; index++) values[index] = (byte)~source.ReadByte(index);
        destination.ReadFrom(values);
    }

    public ulong CompareI32(Opcode opcode, VectorRegister left, VectorRegister right)
    {
        ulong bits = 0;
        for (var lane = 0; lane < 16; lane++)
        {
            var matches = opcode switch
            {
                Opcode.CompareEqualI32 => left.ReadInt32(lane) == right.ReadInt32(lane),
                Opcode.CompareLessThanI32 => left.ReadInt32(lane) < right.ReadInt32(lane),
                _ => throw Unexpected(opcode),
            };
            if (matches) bits |= 1UL << lane;
        }
        return bits;
    }

    public ulong CompareI64(Opcode opcode, VectorRegister left, VectorRegister right)
    {
        ulong bits = 0;
        for (var lane = 0; lane < 8; lane++)
        {
            var matches = opcode switch
            {
                Opcode.CompareEqualI64 => left.ReadInt64(lane) == right.ReadInt64(lane),
                Opcode.CompareLessThanI64 => left.ReadInt64(lane) < right.ReadInt64(lane),
                _ => throw Unexpected(opcode),
            };
            if (matches) bits |= 1UL << lane;
        }
        return bits;
    }

    public void SelectI32(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask) =>
        Select(destination, whenTrue, whenFalse, mask, 16, 4);

    public void SelectI64(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask) =>
        Select(destination, whenTrue, whenFalse, mask, 8, 8);

    private static void Select(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask, int lanes, int laneBytes)
    {
        Span<byte> values = stackalloc byte[VectorRegister.ByteCount];
        for (var lane = 0; lane < lanes; lane++)
        {
            var source = (mask & (1UL << lane)) != 0 ? whenTrue : whenFalse;
            source.Bytes.Slice(lane * laneBytes, laneBytes).CopyTo(values.Slice(lane * laneBytes, laneBytes));
        }
        destination.ReadFrom(values);
    }

    private static InvalidOperationException Unexpected(Opcode opcode) => new($"Opcode {opcode} is not valid for this vector operation.");
}

internal sealed class HardwareSimdVectorOperations : IVectorOperations
{
    internal static HardwareSimdVectorOperations Instance { get; } = new();

    public void SplatI32(VectorRegister destination, uint value) => StoreRepeated(destination, new Vector<int>(unchecked((int)value)));
    public void SplatI64(VectorRegister destination, ulong value) => StoreRepeated(destination, new Vector<long>(unchecked((long)value)));

    public void BinaryI32(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<int> output = stackalloc int[16];
        var a = MemoryMarshal.Cast<byte, int>(left.Bytes);
        var b = MemoryMarshal.Cast<byte, int>(right.Bytes);
        for (var offset = 0; offset < output.Length; offset += Vector<int>.Count)
        {
            var leftVector = new Vector<int>(a.Slice(offset, Vector<int>.Count));
            var rightVector = new Vector<int>(b.Slice(offset, Vector<int>.Count));
            var result = opcode switch
            {
                Opcode.AddI32 => leftVector + rightVector,
                Opcode.SubI32 => leftVector - rightVector,
                Opcode.MulI32 => leftVector * rightVector,
                _ => throw Unexpected(opcode),
            };
            result.CopyTo(output.Slice(offset, Vector<int>.Count));
        }
        destination.ReadFrom(MemoryMarshal.AsBytes(output));
    }

    public void BinaryI64(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<long> output = stackalloc long[8];
        var a = MemoryMarshal.Cast<byte, long>(left.Bytes);
        var b = MemoryMarshal.Cast<byte, long>(right.Bytes);
        for (var offset = 0; offset < output.Length; offset += Vector<long>.Count)
        {
            var leftVector = new Vector<long>(a.Slice(offset, Vector<long>.Count));
            var rightVector = new Vector<long>(b.Slice(offset, Vector<long>.Count));
            var result = opcode switch
            {
                Opcode.AddI64 => leftVector + rightVector,
                Opcode.SubI64 => leftVector - rightVector,
                _ => throw Unexpected(opcode),
            };
            result.CopyTo(output.Slice(offset, Vector<long>.Count));
        }
        destination.ReadFrom(MemoryMarshal.AsBytes(output));
    }

    public void Bitwise(Opcode opcode, VectorRegister destination, VectorRegister left, VectorRegister right)
    {
        Span<byte> output = stackalloc byte[VectorRegister.ByteCount];
        for (var offset = 0; offset < output.Length; offset += Vector<byte>.Count)
        {
            var a = new Vector<byte>(left.Bytes.Slice(offset, Vector<byte>.Count));
            var b = new Vector<byte>(right.Bytes.Slice(offset, Vector<byte>.Count));
            var result = opcode switch
            {
                Opcode.And => a & b,
                Opcode.Or => a | b,
                Opcode.Xor => a ^ b,
                _ => throw Unexpected(opcode),
            };
            result.CopyTo(output.Slice(offset, Vector<byte>.Count));
        }
        destination.ReadFrom(output);
    }

    public void Not(VectorRegister destination, VectorRegister source)
    {
        Span<byte> output = stackalloc byte[VectorRegister.ByteCount];
        for (var offset = 0; offset < output.Length; offset += Vector<byte>.Count)
        {
            var result = ~new Vector<byte>(source.Bytes.Slice(offset, Vector<byte>.Count));
            result.CopyTo(output.Slice(offset, Vector<byte>.Count));
        }
        destination.ReadFrom(output);
    }

    public ulong CompareI32(Opcode opcode, VectorRegister left, VectorRegister right)
    {
        Span<int> matches = stackalloc int[16];
        var a = MemoryMarshal.Cast<byte, int>(left.Bytes);
        var b = MemoryMarshal.Cast<byte, int>(right.Bytes);
        for (var offset = 0; offset < matches.Length; offset += Vector<int>.Count)
        {
            var leftVector = new Vector<int>(a.Slice(offset, Vector<int>.Count));
            var rightVector = new Vector<int>(b.Slice(offset, Vector<int>.Count));
            var result = opcode switch
            {
                Opcode.CompareEqualI32 => Vector.Equals(leftVector, rightVector),
                Opcode.CompareLessThanI32 => Vector.LessThan(leftVector, rightVector),
                _ => throw Unexpected(opcode),
            };
            result.CopyTo(matches.Slice(offset, Vector<int>.Count));
        }
        return PackMask(matches);
    }

    public ulong CompareI64(Opcode opcode, VectorRegister left, VectorRegister right)
    {
        Span<long> matches = stackalloc long[8];
        var a = MemoryMarshal.Cast<byte, long>(left.Bytes);
        var b = MemoryMarshal.Cast<byte, long>(right.Bytes);
        for (var offset = 0; offset < matches.Length; offset += Vector<long>.Count)
        {
            var leftVector = new Vector<long>(a.Slice(offset, Vector<long>.Count));
            var rightVector = new Vector<long>(b.Slice(offset, Vector<long>.Count));
            var result = opcode switch
            {
                Opcode.CompareEqualI64 => Vector.Equals(leftVector, rightVector),
                Opcode.CompareLessThanI64 => Vector.LessThan(leftVector, rightVector),
                _ => throw Unexpected(opcode),
            };
            result.CopyTo(matches.Slice(offset, Vector<long>.Count));
        }
        return PackMask(matches);
    }

    public void SelectI32(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask)
    {
        Span<int> output = stackalloc int[16];
        Span<int> maskValues = stackalloc int[16];
        for (var lane = 0; lane < maskValues.Length; lane++) maskValues[lane] = (mask & (1UL << lane)) != 0 ? -1 : 0;
        Select(destination, whenTrue, whenFalse, output, maskValues);
    }

    public void SelectI64(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, ulong mask)
    {
        Span<long> output = stackalloc long[8];
        Span<long> maskValues = stackalloc long[8];
        for (var lane = 0; lane < maskValues.Length; lane++) maskValues[lane] = (mask & (1UL << lane)) != 0 ? -1L : 0L;
        Select(destination, whenTrue, whenFalse, output, maskValues);
    }

    private static void StoreRepeated<T>(VectorRegister destination, Vector<T> value) where T : unmanaged
    {
        Span<T> output = stackalloc T[VectorRegister.ByteCount / Marshal.SizeOf<T>()];
        for (var offset = 0; offset < output.Length; offset += Vector<T>.Count)
            value.CopyTo(output.Slice(offset, Vector<T>.Count));
        destination.ReadFrom(MemoryMarshal.AsBytes(output));
    }

    private static void Select<T>(VectorRegister destination, VectorRegister whenTrue, VectorRegister whenFalse, Span<T> output, ReadOnlySpan<T> maskValues)
        where T : unmanaged
    {
        var trueValues = MemoryMarshal.Cast<byte, T>(whenTrue.Bytes);
        var falseValues = MemoryMarshal.Cast<byte, T>(whenFalse.Bytes);
        for (var offset = 0; offset < output.Length; offset += Vector<T>.Count)
        {
            var mask = new Vector<T>(maskValues.Slice(offset, Vector<T>.Count));
            var a = new Vector<T>(trueValues.Slice(offset, Vector<T>.Count));
            var b = new Vector<T>(falseValues.Slice(offset, Vector<T>.Count));
            Vector.ConditionalSelect(mask, a, b).CopyTo(output.Slice(offset, Vector<T>.Count));
        }
        destination.ReadFrom(MemoryMarshal.AsBytes(output));
    }

    private static ulong PackMask<T>(ReadOnlySpan<T> values) where T : unmanaged, INumber<T>
    {
        ulong bits = 0;
        for (var lane = 0; lane < values.Length; lane++)
            if (values[lane] != T.Zero) bits |= 1UL << lane;
        return bits;
    }

    private static InvalidOperationException Unexpected(Opcode opcode) => new($"Opcode {opcode} is not valid for this vector operation.");
}
