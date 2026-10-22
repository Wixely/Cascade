using Cascade.Core;

namespace Cascade.Compiler;

internal readonly record struct VirtualInstruction(Opcode Opcode, int Destination = 0, int SourceA = 0,
    int SourceB = 0, int Mask = 0, ushort Flags = 0, ulong Immediate = 0)
{
    public Instruction Encode() => new(Opcode, checked((byte)Destination), checked((byte)SourceA),
        checked((byte)SourceB), checked((byte)Mask), Flags, Immediate);
}

internal sealed class VirtualRegisterPool(int initialCount, int maximum)
{
    private readonly SortedSet<int> _free = new(Enumerable.Range(0, initialCount));
    public int Count { get; private set; } = initialCount;
    public int ActiveCount { get; private set; }

    public bool this[int index]
    {
        set
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (value) { if (_free.Remove(index)) ActiveCount++; }
            else if (_free.Add(index)) ActiveCount--;
        }
    }

    public int Allocate()
    {
        if (_free.Count != 0)
        {
            var index = _free.Min;
            this[index] = true;
            return index;
        }
        if (Count >= maximum) return -1;
        ActiveCount++;
        return Count++;
    }

    public int AllocatePair()
    {
        var candidate = -1;
        foreach (var index in _free)
            if ((index & 1) == 0 && _free.Contains(index + 1)) { candidate = index; break; }
        if (candidate >= 0)
        {
            this[candidate] = true;
            this[candidate + 1] = true;
            return candidate;
        }
        if ((Count & 1) != 0 && Count < maximum && _free.Contains(Count - 1))
        {
            var startOfPair = Count - 1;
            this[startOfPair] = true;
            Count++;
            ActiveCount++;
            return startOfPair;
        }
        var start = (Count + 1) & ~1;
        if (start + 2 > maximum) return -1;
        if (start != Count) _free.Add(Count);
        Count = start + 2;
        ActiveCount += 2;
        return start;
    }
}
