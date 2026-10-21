using Cascade.Core;
using Cascade.Runtime;

return args switch
{
    ["run", var path] => Run(path, ExecutionBackend.Reference),
    ["run", var path, "--backend", var backend] => ParseBackend(path, backend),
    ["sample", var path] => WriteSample(path),
    _ => Usage(),
};

static int ParseBackend(string path, string value) => value.ToLowerInvariant() switch
{
    "reference" => Run(path, ExecutionBackend.Reference),
    "simd" => Run(path, ExecutionBackend.HardwareSimd),
    "auto" => Run(path, ExecutionBackend.Automatic),
    _ => BackendUsage(value),
};

static int Run(string path, ExecutionBackend backend)
{
    try
    {
        using var stream = File.OpenRead(path);
        var program = CascadeProgram.Load(stream);
        var instance = program.Instantiate(backend: backend);
        var result = instance.Execute();
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"trap {result.Trap!.Code} at instruction {result.Trap.InstructionPointer}: {result.Trap.Message}");
            Console.Error.WriteLine($"fuel consumed: {result.FuelConsumed}");
            return 2;
        }

        Console.WriteLine($"halted after {result.FuelConsumed} instructions using {instance.Backend}");
        Console.WriteLine($"v0.i64: {string.Join(", ", Enumerable.Range(0, 8).Select(lane => instance.State.Register(0).ReadUInt64(lane)))}");
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

static int BackendUsage(string value)
{
    Console.Error.WriteLine($"error: unknown backend '{value}'; expected reference, simd, or auto");
    return 1;
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
    Console.WriteLine("  cascade run <file.cvm> --backend <reference|simd|auto>");
    return 1;
}
