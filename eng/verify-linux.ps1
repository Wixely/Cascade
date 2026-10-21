[CmdletBinding()]
param(
    [string]$Distribution,
    [switch]$RequireNativeAot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishRoot = Join-Path $repositoryRoot 'artifacts\linux-verification'
$wslDistributionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Distribution)) {
    $wslDistributionArgs = @('-d', $Distribution)
}

function Invoke-Checked {
    param([scriptblock]$Command, [string]$Description)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
Invoke-Checked { dotnet publish (Join-Path $repositoryRoot 'src\Cascade.Cli\Cascade.Cli.csproj') -c Release -r linux-x64 --self-contained true -p:PublishAot=false -o (Join-Path $publishRoot 'cli') } 'Linux CLI publish'
Invoke-Checked { dotnet publish (Join-Path $repositoryRoot 'tests\Cascade.Runtime.Conformance\Cascade.Runtime.Conformance.csproj') -c Release -r linux-x64 --self-contained true -o (Join-Path $publishRoot 'conformance') } 'Linux conformance publish'
Invoke-Checked { dotnet publish (Join-Path $repositoryRoot 'tests\Cascade.Runtime.Differential\Cascade.Runtime.Differential.csproj') -c Release -r linux-x64 --self-contained true -o (Join-Path $publishRoot 'differential') } 'Linux differential publish'
Invoke-Checked { dotnet publish (Join-Path $repositoryRoot 'tests\Cascade.Compiler.Tests\Cascade.Compiler.Tests.csproj') -c Release -r linux-x64 --self-contained true -o (Join-Path $publishRoot 'compiler-tests') } 'Linux compiler tests publish'

$publishPathForWsl = $publishRoot.Replace('\', '/')
$linuxPublishRoot = (& wsl.exe @wslDistributionArgs -- wslpath -a $publishPathForWsl)
if ($null -ne $linuxPublishRoot) { $linuxPublishRoot = $linuxPublishRoot.Trim() }
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($linuxPublishRoot)) { throw 'Could not translate the Linux publish path.' }
$repositoryPathForWsl = $repositoryRoot.Replace('\', '/')
$linuxRepositoryRoot = (& wsl.exe @wslDistributionArgs -- wslpath -a $repositoryPathForWsl).Trim()
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/conformance/Cascade.Runtime.Conformance') } 'Linux conformance execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/differential/Cascade.Runtime.Differential') } 'Linux differential execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/compiler-tests/Cascade.Compiler.Tests') } 'Linux compiler tests execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/cli/cascade') sample /tmp/cascade-verification.cvm } 'Linux CLI sample creation'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/cli/cascade') run /tmp/cascade-verification.cvm } 'Linux CLI execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/cli/cascade') compile ($linuxRepositoryRoot + '/examples/Cascade.GameOfLife/life-rule.cascade') -o /tmp/life-rule.cvm } 'Linux compiler CLI execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxPublishRoot + '/cli/cascade') run /tmp/life-rule.cvm } 'Linux compiled module execution'

wsl.exe @wslDistributionArgs -- which dotnet 2>$null | Out-Null
$hasLinuxSdk = $LASTEXITCODE -eq 0
wsl.exe @wslDistributionArgs -- which clang 2>$null | Out-Null
$hasCompiler = $LASTEXITCODE -eq 0
if (-not $hasCompiler) {
    wsl.exe @wslDistributionArgs -- which gcc 2>$null | Out-Null
    $hasCompiler = $LASTEXITCODE -eq 0
}

if (-not ($hasLinuxSdk -and $hasCompiler)) {
    $message = 'Managed Linux verification passed; NativeAOT requires a Linux .NET SDK and C/C++ compiler in the selected distribution.'
    if ($RequireNativeAot) { throw $message }
    Write-Warning $message
    return
}

$linuxNativeOutput = $linuxPublishRoot + '/native-aot'
Invoke-Checked { wsl.exe @wslDistributionArgs -- dotnet publish ($linuxRepositoryRoot + '/src/Cascade.Cli/Cascade.Cli.csproj') -c Release -r linux-x64 --self-contained true -p:PublishAot=true -o $linuxNativeOutput } 'Linux NativeAOT publish'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxNativeOutput + '/cascade') run /tmp/cascade-verification.cvm } 'Linux NativeAOT execution'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxNativeOutput + '/cascade') compile ($linuxRepositoryRoot + '/examples/Cascade.GameOfLife/life-rule.cascade') -o /tmp/life-rule-native.cvm } 'Linux NativeAOT compiler execution'
$linuxNativeDemoOutput = $linuxPublishRoot + '/native-aot-game-of-life'
Invoke-Checked { wsl.exe @wslDistributionArgs -- dotnet publish ($linuxRepositoryRoot + '/examples/Cascade.GameOfLife/Cascade.GameOfLife.csproj') -c Release -r linux-x64 --self-contained true -p:PublishAot=true -o $linuxNativeDemoOutput } 'Linux SIMD demo NativeAOT publish'
Invoke-Checked { wsl.exe @wslDistributionArgs -- ($linuxNativeDemoOutput + '/Cascade.GameOfLife') --benchmark } 'Linux SIMD demo NativeAOT execution'
Write-Output 'Managed and NativeAOT Linux verification passed.'
