[CmdletBinding()]
param([switch]$SkipFormat)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Invoke-Checked {
    param([scriptblock]$Command, [string]$Description)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

Push-Location $repositoryRoot
try {
    Invoke-Checked { dotnet build Cascade.sln -c Release } 'Release build'
    if (-not $SkipFormat) {
        Invoke-Checked { dotnet format Cascade.sln --verify-no-changes --no-restore } 'Formatting'
    }
    foreach ($project in @('tests/Cascade.Compiler.Tests', 'tests/Cascade.Runtime.Conformance', 'tests/Cascade.Runtime.Differential', 'examples/Cascade.Demos')) {
        Invoke-Checked { dotnet run --project $project -c Release --no-build } $project
    }
    Invoke-Checked { dotnet run --project examples/Cascade.GameOfLife -c Release --no-build -- --benchmark } 'Game of Life oracle'
    Invoke-Checked { dotnet run --project tools/Cascade.Benchmarks -c Release --no-build -- --batch --quick } 'Batch benchmark smoke'

    $smokeDirectory = Join-Path $repositoryRoot ('artifacts/cli-smoke-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $smokeDirectory | Out-Null
    $sourcePath = Join-Path $smokeDirectory 'kernel.cascade'
    $modulePath = Join-Path $smokeDirectory 'kernel.cvm'
    Copy-Item -LiteralPath 'examples/Cascade.Demos/gather-transform.cascade' -Destination $sourcePath
    $cliPath = Join-Path $repositoryRoot 'src/Cascade.Cli/bin/Release/net10.0/cascade.dll'
    Invoke-Checked { dotnet $cliPath compile $sourcePath -o $modulePath } 'Optimized CLI compilation'
    Invoke-Checked { dotnet $cliPath run $modulePath } 'Compiled module execution'
    Invoke-Checked { dotnet $cliPath compile $sourcePath --no-optimize -o $modulePath } 'Unoptimized CLI compilation'
    Invoke-Checked { dotnet $cliPath run $modulePath } 'Unoptimized module execution'

    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    & dotnet $cliPath compile $sourcePath -o $sourcePath
    if ($LASTEXITCODE -ne 1) { throw 'CLI did not reject overwriting its source.' }
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Source changed during rejected compilation.' }
    $moduleHash = (Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash
    & dotnet $cliPath compile 'tests/Cascade.Compiler.Tests/Fixtures/invalid-return.cascade' -o $modulePath
    if ($LASTEXITCODE -ne 1) { throw 'CLI did not reject invalid source.' }
    if ((Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash -ne $moduleHash) { throw 'Failed compilation changed existing output.' }
    if (@(Get-ChildItem -LiteralPath $smokeDirectory -Filter '*.tmp').Count -ne 0) { throw 'CLI left temporary output files.' }
    Invoke-Checked { & ./eng/verify-packages.ps1 } 'Package smoke'
    Write-Output 'Managed verification, compiler oracles, batch checks, and CLI preservation checks passed.'
}
finally {
    Pop-Location
}
