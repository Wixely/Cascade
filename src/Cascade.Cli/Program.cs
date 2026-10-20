using Cascade.Core;
using Cascade.Runtime;

return args switch
{
    ["run", var path] => Run(path),
    ["sample", var path] => WriteSample(path),
    _ => Usage(),
};

static int Run(string path)
{
    try
    {
        using var stream = File.OpenRead(path);
        var module = CvmModuleFormat.Read(stream);
        var interpreter = new ReferenceInterpreter(module);
        var result = interpreter.Execute();
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"trap {result.Trap!.Code} at instruction {result.Trap.InstructionPointer}: {result.Trap.Message}");
            Console.Error.WriteLine($"fuel consumed: {result.FuelConsumed}");
            return 2;
        }

        Console.WriteLine($"halted after {result.FuelConsumed} instructions");
        Console.WriteLine($"v0.i64: {string.Join(", ", Enumerable.Range(0, 8).Select(lane => interpreter.State.Register(0).ReadUInt64(lane)))}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

static int WriteSample(string path)
{
    try
    {
        var module = new CascadeModule([
            new(Opcode.SplatI64, Destination: 0, Immediate: 40),
            new(Opcode.SplatI64, Destination: 1, Immediate: 2),
            new(Opcode.AddI64, Destination: 0, SourceA: 0, SourceB: 1),
            new(Opcode.Halt),
        ]);
        using var stream = File.Create(path);
        CvmModuleFormat.Write(stream, module);
        Console.WriteLine($"wrote {path}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

static int Usage()
{
    Console.WriteLine("Cascade VM MVP");
    Console.WriteLine("usage:");
    Console.WriteLine("  cascade sample <file.cvm>  Write a sample module");
    Console.WriteLine("  cascade run <file.cvm>     Validate and execute a module");
    return 1;
}
