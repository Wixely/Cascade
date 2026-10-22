# Cascade library quickstart

Cascade 0.1.0 targets .NET 10 and is licensed under MIT. This is an early API
release, not a promise of native-code performance or an audited security sandbox.

## Install from the GitHub release

Download all three `.nupkg` files from
[v0.1.0](https://github.com/Wixely/Cascade/releases/tag/v0.1.0) into a local
`packages` directory. The release packages form a local NuGet feed; a NuGet.org
listing is not required or assumed.

```powershell
dotnet new console -n CascadeConsumer -f net10.0
cd CascadeConsumer
dotnet add package Wixely.Cascade.Runtime --version 0.1.0 --source ../packages
dotnet add package Wixely.Cascade.Compiler --version 0.1.0 --source ../packages
```

Both packages depend on `Wixely.Cascade.Core`, which must also be present in that
directory. Runtime is sufficient when loading precompiled `.cvm` files; add
Compiler when compiling `.cascade` source inside your application.

Replace `Program.cs` with:

```csharp
using Cascade.Compiler;
using Cascade.Runtime;

var compilation = CascadeCompiler.Compile("""
    kernel adjust(vector<i32> input) -> vector<i32> {
        return input * 2 + 1;
    }
    """);
if (!compilation.Succeeded)
{
    foreach (var diagnostic in compilation.Diagnostics)
        Console.Error.WriteLine(diagnostic);
    return 1;
}

var program = CascadeProgram.FromModule(compilation.Module!);
var instance = program.Instantiate(
    new VmOptions { MemorySize = 0, Fuel = 100 },
    backend: ExecutionBackend.Automatic);
for (var lane = 0; lane < 16; lane++)
    instance.State.Register(0).WriteUInt32(lane, (uint)lane);

var result = instance.Execute();
if (!result.Succeeded)
{
    Console.Error.WriteLine(result.Trap);
    return 1;
}
for (var lane = 0; lane < 16; lane++)
    Console.WriteLine(instance.State.Register(0).ReadInt32(lane));
return 0;
```

Run `dotnet run`; output is the odd integers 1 through 31. No file access,
network access, or host imports are granted to this kernel. `Automatic` selects
the supported SIMD backend or falls back to reference execution.

For another invocation, call `instance.Restart()` and overwrite the input lanes.
Registers and linear memory persist across restart; control state and private
spill storage reset. Instances are not thread-safe. Share immutable programs,
not mutable instances, between concurrent workers.

Save compiled programs with `program.Save(stream)` and load them with
`CascadeProgram.Load(stream)`. Loading validates bytecode before instantiation.
See the [embedding guide](https://github.com/Wixely/Cascade/blob/main/docs/embedding.md)
for batching, host capabilities, traps, budgets, and compatibility, and the
[language guide](https://github.com/Wixely/Cascade/blob/main/docs/language.md)
for source semantics.
