[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$testRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ('package-smoke-' + [Guid]::NewGuid().ToString('N'))))
$feedRoot = Join-Path $testRoot 'feed'
$consumerRoot = Join-Path $testRoot 'consumer'
$packagesRoot = Join-Path $testRoot 'packages'

if (-not $testRoot.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Temporary package test path escaped the artifacts directory.'
}

try {
    New-Item -ItemType Directory -Path $feedRoot, $consumerRoot -Force | Out-Null
    & dotnet pack (Join-Path $repositoryRoot 'src\Cascade.Core\Cascade.Core.csproj') -c Release -o $feedRoot
    if ($LASTEXITCODE -ne 0) { throw 'Core package creation failed.' }
    & dotnet pack (Join-Path $repositoryRoot 'src\Cascade.Runtime\Cascade.Runtime.csproj') -c Release -o $feedRoot
    if ($LASTEXITCODE -ne 0) { throw 'Runtime package creation failed.' }
    & dotnet pack (Join-Path $repositoryRoot 'src\Cascade.Compiler\Cascade.Compiler.csproj') -c Release -o $feedRoot
    if ($LASTEXITCODE -ne 0) { throw 'Compiler package creation failed.' }

    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Wixely.Cascade.Runtime" Version="0.1.0" />
    <PackageReference Include="Wixely.Cascade.Compiler" Version="0.1.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'PackageSmoke.csproj') -Encoding UTF8

    @'
using Cascade.Core;
using Cascade.Compiler;
using Cascade.Runtime;

var module = new CascadeModule([
    new(Opcode.SplatI64, Destination: 0, Immediate: 42),
    new(Opcode.Halt),
]);
var program = CascadeProgram.FromModule(module);
using var encoded = new MemoryStream();
program.Save(encoded);
encoded.Position = 0;
var instance = CascadeProgram.Load(encoded).Instantiate(backend: ExecutionBackend.Automatic);
var result = instance.Execute();
instance.State.Register(0).WriteUInt64(0, 0);
instance.Restart();
var restartedResult = instance.Execute();
var batchOutputs = new byte[128];
var batchResult = instance.ExecuteBatch([], batchOutputs, 0);
var batchCorrect = batchResult.Succeeded && batchResult.CompletedCount == 2 && batchResult.FuelConsumed == 4 &&
    System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(batchOutputs.AsSpan(64)) == 42;
var expectedBackend = SimdInterpreter.IsSupported ? ExecutionBackend.HardwareSimd : ExecutionBackend.Reference;
var spillProgram = CascadeProgram.FromModule(new CascadeModule([
    new(Opcode.SplatI64, Destination: 0, Immediate: 99),
    new(Opcode.SpillStoreVector, SourceA: 0),
    new(Opcode.Zero, Destination: 0),
    new(Opcode.SpillLoadVector, Destination: 0),
    new(Opcode.Halt),
], vectorSpillSlots: 1));
using var spillBytes = new MemoryStream();
spillProgram.Save(spillBytes);
spillBytes.Position = 0;
var spillInstance = CascadeProgram.Load(spillBytes).Instantiate(new VmOptions { MaximumSpillBytes = 64 });
var spillCorrect = spillInstance.Execute().Succeeded && spillInstance.State.Register(0).ReadUInt64(0) == 99;
var compiled = CascadeCompiler.Compile("kernel adjust(vector<i32> input) -> vector<i32> { return input * 2 + 1; }");
if (!compiled.Succeeded) return 1;
var kernel = CascadeProgram.FromModule(compiled.Module!).Instantiate(new VmOptions { MemorySize = 0, Fuel = 100 });
for (var lane = 0; lane < 16; lane++) kernel.State.Register(0).WriteUInt32(lane, (uint)lane);
var compiledCorrect = kernel.Execute().Succeeded;
for (var lane = 0; lane < 16; lane++) compiledCorrect &= kernel.State.Register(0).ReadInt32(lane) == lane * 2 + 1;
return result.Succeeded && restartedResult.Succeeded && batchCorrect && spillCorrect && compiledCorrect && instance.Backend == expectedBackend && instance.State.Register(0).ReadUInt64(0) == 42 ? 0 : 1;
'@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'Program.cs') -Encoding UTF8

    & dotnet restore (Join-Path $consumerRoot 'PackageSmoke.csproj') --source $feedRoot --packages $packagesRoot --no-cache --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed.' }
    & dotnet run --project (Join-Path $consumerRoot 'PackageSmoke.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer execution failed.' }
    Write-Output 'Package smoke test passed from an isolated local feed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
