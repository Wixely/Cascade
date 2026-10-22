using System.Buffers.Binary;
using System.Text;
using Cascade.Core;

namespace Cascade.Runtime;

/// <summary>Canonical reader and writer for CVM1 1.0 and the 1.1 private-spill extension.</summary>
public static class CvmModuleFormat
{
    private enum SectionType : uint { Code = 1, Constants = 2, Functions = 3, Imports = 4, Spills = 5 }
    private readonly record struct Section(SectionType Type, uint Flags, byte[] Data);
    private readonly record struct DirectoryEntry(uint Type, uint Flags, ulong Offset, ulong Length);

    private static ReadOnlySpan<byte> Magic => "CVM1"u8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int HeaderSize = 32;
    private const int DirectoryEntrySize = 24;
    private const int InstructionSize = 16;
    private const uint RequiredSection = 1;
    private const int MaximumInstructions = 1_000_000;
    private const int MaximumConstants = 65_536;
    private const int MaximumFunctions = 65_536;
    private const int MaximumImports = 1_024;
    private const long MaximumFileSize = 128L * 1024 * 1024;

    public static CascadeModule Read(Stream stream)
    {
        try { return ReadCore(stream); }
        catch (OverflowException exception) { throw new InvalidDataException("CVM1 metadata arithmetic overflowed.", exception); }
    }

    private static CascadeModule ReadCore(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek) throw new ArgumentException("CVM1 input must be seekable.", nameof(stream));
        var remaining = stream.Length - stream.Position;
        if (remaining is < HeaderSize or > MaximumFileSize) throw new InvalidDataException("CVM1 file size is invalid or exceeds the 128 MiB limit.");
        var file = new byte[checked((int)remaining)];
        ReadExactly(stream, file, "file");
        var header = file.AsSpan(0, HeaderSize);

        if (!header[..4].SequenceEqual(Magic)) throw new InvalidDataException("Invalid CVM magic; expected CVM1.");
        if (header[4] != 1 || header[5] > 1) throw new InvalidDataException($"Unsupported CVM version {header[4]}.{header[5]}.");
        var hasSpills = header[5] == 1;
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != HeaderSize) throw new InvalidDataException("Invalid header size.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != 0) throw new InvalidDataException("Header flags are reserved and must be zero.");
        var sectionCount = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        var directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        var entryPoint = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header[28..]) != 0) throw new InvalidDataException("Reserved header bytes must be zero.");
        if (sectionCount is 0 or > 64) throw new InvalidDataException("Section count must be between 1 and 64.");
        if (directoryOffset != HeaderSize || directoryLength != checked(sectionCount * DirectoryEntrySize))
            throw new InvalidDataException("Invalid section directory location or length.");
        var directoryEnd = checked((ulong)directoryOffset + directoryLength);
        if (directoryEnd > (ulong)file.Length) throw new InvalidDataException("Section directory exceeds the file.");

        var entries = new DirectoryEntry[sectionCount];
        uint previousType = 0;
        ulong expectedOffset = directoryEnd;
        var codeCount = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var recordOffset = checked((int)directoryOffset + (index * DirectoryEntrySize));
            var record = file.AsSpan(recordOffset, DirectoryEntrySize);
            var entry = new DirectoryEntry(
                BinaryPrimitives.ReadUInt32LittleEndian(record),
                BinaryPrimitives.ReadUInt32LittleEndian(record[4..]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[8..]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[16..]));
            if (entry.Type <= previousType) throw new InvalidDataException("Sections must have unique, ascending type identifiers.");
            if ((entry.Flags & ~RequiredSection) != 0) throw new InvalidDataException("Section flags contain unsupported bits.");
            if ((entry.Offset & 7) != 0 || (entry.Length & 7) != 0) throw new InvalidDataException("Sections must be 8-byte aligned.");
            if (entry.Offset != expectedOffset) throw new InvalidDataException("Sections must be contiguous and ordered as listed.");
            expectedOffset = checked(entry.Offset + entry.Length);
            if (expectedOffset > (ulong)file.Length) throw new InvalidDataException("Section exceeds the file.");
            if (entry.Type == (uint)SectionType.Code)
            {
                codeCount++;
                if ((entry.Flags & RequiredSection) == 0) throw new InvalidDataException("The code section must be required.");
            }
            else if (!Enum.IsDefined((SectionType)entry.Type) && (entry.Flags & RequiredSection) != 0)
                throw new InvalidDataException($"Unknown required section type {entry.Type}.");
            entries[index] = entry;
            previousType = entry.Type;
        }
        if (expectedOffset != (ulong)file.Length) throw new InvalidDataException("Trailing data is not permitted.");
        if (codeCount != 1) throw new InvalidDataException("Exactly one code section is required.");

        Instruction[] instructions = [];
        VectorConstant[] constants = [];
        FunctionDefinition[] functions = [];
        ImportDefinition[] imports = [];
        var vectorSpillSlots = 0;
        var maskSpillSlots = 0;
        var spillSectionSeen = false;
        foreach (var entry in entries)
        {
            var data = file.AsSpan(checked((int)entry.Offset), checked((int)entry.Length));
            switch ((SectionType)entry.Type)
            {
                case SectionType.Code: instructions = ReadCode(data); break;
                case SectionType.Constants: constants = ReadConstants(data); break;
                case SectionType.Functions: functions = ReadFunctions(data); break;
                case SectionType.Imports: imports = ReadImports(data); break;
                case SectionType.Spills:
                    // Type 5 was an unknown optional extension in 1.0. Preserve
                    // that version's skip rule rather than reinterpret its bytes.
                    if (!hasSpills && entry.Flags == 0) break;
                    if (!hasSpills || entry.Flags != RequiredSection || data.Length != 16 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(data) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0)
                        throw new InvalidDataException("Invalid spill section or version.");
                    var vectors = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
                    var masks = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
                    if (vectors > CascadeModule.MaximumSpillSlots || masks > CascadeModule.MaximumSpillSlots || (vectors == 0 && masks == 0))
                        throw new InvalidDataException("Invalid spill slot counts.");
                    vectorSpillSlots = (int)vectors;
                    maskSpillSlots = (int)masks;
                    spillSectionSeen = true;
                    break;
            }
        }

        if (hasSpills != spillSectionSeen) throw new InvalidDataException("CVM1 1.1 requires a spill section.");
        var module = new CascadeModule(instructions, constants, functions, imports, checked((int)entryPoint), vectorSpillSlots, maskSpillSlots);
        var errors = ModuleValidator.Validate(module);
        if (errors.Count != 0)
            throw new InvalidDataException($"Module validation failed: {string.Join("; ", errors.Select(error => $"[{error.InstructionIndex}] {error.Message}"))}");
        return module;
    }

    public static void Write(Stream stream, CascadeModule module)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(module);
        var errors = ModuleValidator.Validate(module);
        if (errors.Count != 0)
            throw new ArgumentException($"Module validation failed: {string.Join("; ", errors.Select(error => $"[{error.InstructionIndex}] {error.Message}"))}", nameof(module));
        if (module.Instructions.Count > MaximumInstructions || module.Constants.Count > MaximumConstants ||
            module.Functions.Count > MaximumFunctions || module.Imports.Count > MaximumImports)
            throw new ArgumentException("A module collection exceeds the CVM1 implementation limit.", nameof(module));

        var sections = new List<Section> { new(SectionType.Code, RequiredSection, WriteCode(module.Instructions)) };
        if (module.Constants.Count != 0) sections.Add(new(SectionType.Constants, 0, WriteConstants(module.Constants)));
        if (module.Functions.Count != 0) sections.Add(new(SectionType.Functions, 0, WriteFunctions(module.Functions)));
        if (module.Imports.Count != 0) sections.Add(new(SectionType.Imports, 0, WriteImports(module.Imports)));
        if (module.SpillStorageBytes != 0)
        {
            var spillData = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(spillData, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(spillData.AsSpan(8), (uint)module.VectorSpillSlots);
            BinaryPrimitives.WriteUInt32LittleEndian(spillData.AsSpan(12), (uint)module.MaskSpillSlots);
            sections.Add(new(SectionType.Spills, RequiredSection, spillData));
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        header[4] = 1;
        header[5] = module.SpillStorageBytes == 0 ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)sections.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], checked((uint)sections.Count * DirectoryEntrySize));
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], checked((uint)module.EntryPoint));
        stream.Write(header);

        ulong offset = checked((ulong)HeaderSize + ((ulong)sections.Count * DirectoryEntrySize));
        Span<byte> directoryRecord = stackalloc byte[DirectoryEntrySize];
        foreach (var section in sections)
        {
            directoryRecord.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(directoryRecord, (uint)section.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(directoryRecord[4..], section.Flags);
            BinaryPrimitives.WriteUInt64LittleEndian(directoryRecord[8..], offset);
            BinaryPrimitives.WriteUInt64LittleEndian(directoryRecord[16..], (ulong)section.Data.Length);
            stream.Write(directoryRecord);
            offset = checked(offset + (ulong)section.Data.Length);
        }
        foreach (var section in sections) stream.Write(section.Data);
    }

    private static Instruction[] ReadCode(ReadOnlySpan<byte> data)
    {
        var count = ReadCollectionHeader(data, MaximumInstructions, "code");
        if (data.Length != checked(8 + (count * InstructionSize))) throw new InvalidDataException("Code section length is invalid.");
        var instructions = new Instruction[count];
        for (var index = 0; index < count; index++)
        {
            var record = data.Slice(8 + (index * InstructionSize), InstructionSize);
            instructions[index] = new((Opcode)BinaryPrimitives.ReadUInt16LittleEndian(record), record[2], record[3], record[4], record[5],
                BinaryPrimitives.ReadUInt16LittleEndian(record[6..]), BinaryPrimitives.ReadUInt64LittleEndian(record[8..]));
        }
        return instructions;
    }

    private static VectorConstant[] ReadConstants(ReadOnlySpan<byte> data)
    {
        var count = ReadCollectionHeader(data, MaximumConstants, "constants");
        if (data.Length != checked(8 + (count * VectorRegister.ByteCount))) throw new InvalidDataException("Constants section length is invalid.");
        var constants = new VectorConstant[count];
        for (var index = 0; index < count; index++) constants[index] = new(data.Slice(8 + (index * VectorRegister.ByteCount), VectorRegister.ByteCount));
        return constants;
    }

    private static FunctionDefinition[] ReadFunctions(ReadOnlySpan<byte> data)
    {
        const int recordSize = 16;
        var count = ReadCollectionHeader(data, MaximumFunctions, "functions");
        if (data.Length != checked(8 + (count * recordSize))) throw new InvalidDataException("Functions section length is invalid.");
        var functions = new FunctionDefinition[count];
        for (var index = 0; index < count; index++)
        {
            var record = data.Slice(8 + (index * recordSize), recordSize);
            if (BinaryPrimitives.ReadUInt64LittleEndian(record[8..]) != 0) throw new InvalidDataException("Reserved function bytes must be zero.");
            functions[index] = new(BinaryPrimitives.ReadUInt32LittleEndian(record), BinaryPrimitives.ReadUInt32LittleEndian(record[4..]));
        }
        return functions;
    }

    private static ImportDefinition[] ReadImports(ReadOnlySpan<byte> data)
    {
        const int recordHeaderSize = 24;
        var count = ReadCollectionHeader(data, MaximumImports, "imports");
        var imports = new ImportDefinition[count];
        var offset = 8;
        for (var index = 0; index < count; index++)
        {
            if (data.Length - offset < recordHeaderSize) throw new InvalidDataException("Import record is truncated.");
            var record = data[offset..];
            var namespaceLength = BinaryPrimitives.ReadUInt16LittleEndian(record);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[2..]);
            var abiVersion = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(record[20..]) != 0)
                throw new InvalidDataException("Reserved import bytes must be zero.");
            var fuelCost = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            var recordLength = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            var contentLength = checked(recordHeaderSize + namespaceLength + nameLength);
            var expectedLength = Align8(contentLength);
            if (recordLength != expectedLength || recordLength > data.Length - offset) throw new InvalidDataException("Import record length is invalid.");
            if (data.Slice(offset + contentLength, expectedLength - contentLength).ContainsAnyExcept((byte)0))
                throw new InvalidDataException("Import padding must be zero.");
            var text = data.Slice(offset + recordHeaderSize, namespaceLength + nameLength);
            string importNamespace;
            string name;
            try
            {
                importNamespace = StrictUtf8.GetString(text[..namespaceLength]);
                name = StrictUtf8.GetString(text[namespaceLength..]);
            }
            catch (DecoderFallbackException exception) { throw new InvalidDataException("Import text is not valid UTF-8.", exception); }
            imports[index] = new(importNamespace, name, abiVersion, fuelCost);
            offset = checked(offset + expectedLength);
        }
        if (offset != data.Length) throw new InvalidDataException("Imports section contains trailing data.");
        return imports;
    }

    private static int ReadCollectionHeader(ReadOnlySpan<byte> data, int maximum, string name)
    {
        if (data.Length < 8) throw new InvalidDataException($"The {name} section is truncated.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > maximum) throw new InvalidDataException($"The {name} count exceeds the implementation limit.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 0) throw new InvalidDataException($"Reserved {name} bytes must be zero.");
        return checked((int)count);
    }

    private static byte[] WriteCode(IReadOnlyList<Instruction> instructions)
    {
        var data = new byte[checked(8 + (instructions.Count * InstructionSize))];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)instructions.Count);
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            var record = data.AsSpan(8 + (index * InstructionSize), InstructionSize);
            BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)instruction.Opcode);
            record[2] = instruction.Destination;
            record[3] = instruction.SourceA;
            record[4] = instruction.SourceB;
            record[5] = instruction.Mask;
            BinaryPrimitives.WriteUInt16LittleEndian(record[6..], instruction.Flags);
            BinaryPrimitives.WriteUInt64LittleEndian(record[8..], instruction.Immediate);
        }
        return data;
    }

    private static byte[] WriteConstants(IReadOnlyList<VectorConstant> constants)
    {
        var data = new byte[checked(8 + (constants.Count * VectorRegister.ByteCount))];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)constants.Count);
        for (var index = 0; index < constants.Count; index++) constants[index].Bytes.CopyTo(data.AsSpan(8 + (index * VectorRegister.ByteCount)));
        return data;
    }

    private static byte[] WriteFunctions(IReadOnlyList<FunctionDefinition> functions)
    {
        const int recordSize = 16;
        var data = new byte[checked(8 + (functions.Count * recordSize))];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)functions.Count);
        for (var index = 0; index < functions.Count; index++)
        {
            var record = data.AsSpan(8 + (index * recordSize), recordSize);
            BinaryPrimitives.WriteUInt32LittleEndian(record, functions[index].Start);
            BinaryPrimitives.WriteUInt32LittleEndian(record[4..], functions[index].EndExclusive);
        }
        return data;
    }

    private static byte[] WriteImports(IReadOnlyList<ImportDefinition> imports)
    {
        using var output = new MemoryStream();
        Span<byte> collectionHeader = stackalloc byte[8];
        collectionHeader.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(collectionHeader, (uint)imports.Count);
        output.Write(collectionHeader);
        foreach (var import in imports)
        {
            var namespaceBytes = StrictUtf8.GetBytes(import.Namespace);
            var nameBytes = StrictUtf8.GetBytes(import.Name);
            var contentLength = checked(24 + namespaceBytes.Length + nameBytes.Length);
            var recordLength = Align8(contentLength);
            var record = new byte[recordLength];
            BinaryPrimitives.WriteUInt16LittleEndian(record, checked((ushort)namespaceBytes.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(2), checked((ushort)nameBytes.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), import.AbiVersion);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(8), import.FuelCost);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), checked((uint)recordLength));
            namespaceBytes.CopyTo(record, 24);
            nameBytes.CopyTo(record, 24 + namespaceBytes.Length);
            output.Write(record);
        }
        return output.ToArray();
    }

    private static int Align8(int value) => checked((value + 7) & ~7);

    private static void ReadExactly(Stream stream, Span<byte> destination, string description)
    {
        try { stream.ReadExactly(destination); }
        catch (EndOfStreamException exception) { throw new InvalidDataException($"Truncated {description}.", exception); }
    }
}
