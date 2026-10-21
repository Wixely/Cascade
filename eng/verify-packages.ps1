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
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'PackageSmoke.csproj') -Encoding UTF8

    @'
using Cascade.Core;
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
var expectedBackend = SimdInterpreter.IsSupported ? ExecutionBackend.HardwareSimd : ExecutionBackend.Reference;
return result.Succeeded && instance.Backend == expectedBackend && instance.State.Register(0).ReadUInt64(0) == 42 ? 0 : 1;
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
