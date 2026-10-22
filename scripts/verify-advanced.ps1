param([string]$PackageDirectory = '', [switch]$Credentials)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$dotnet = if (Test-Path "$root/.dotnet/dotnet.exe") { "$root/.dotnet/dotnet.exe" } else { (Get-Command dotnet).Source }
$env:DOTNET_CLI_HOME = "$root/work/dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($PackageDirectory) {
    $env:MACROSTUDIO_PACKAGE_DIRECTORY = [IO.Path]::GetFullPath($PackageDirectory)
    $lab = Join-Path ([IO.Path]::GetFullPath($PackageDirectory)) 'Lab/MacroStudio.TestLab.exe'
    if (!(Test-Path -LiteralPath $lab)) { throw "Packaged lab missing: $lab" }
} else {
    Remove-Item Env:MACROSTUDIO_PACKAGE_DIRECTORY -ErrorAction SilentlyContinue
    $labDirectory = "$root/artifacts/AdvancedLab"
    & $dotnet publish src/MacroStudio.TestLab -c Release -r win-x64 --self-contained true -o $labDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Visual Lab publish failed.' }
    $lab = "$labDirectory/MacroStudio.TestLab.exe"
}
$env:MACROSTUDIO_LAB_EXE = $lab
& $dotnet build tests/MacroStudio.AdvancedTests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Advanced test build failed.' }
$testArguments = @("$root/tests/MacroStudio.AdvancedTests/bin/Release/net10.0-windows10.0.19041.0/MacroStudio.AdvancedTests.dll", '--live')
if ($Credentials.IsPresent) { $testArguments += '--credentials' }
Write-Host 'Activate Visual Lab and leave input idle. Escape cancels; input aborts if the lab loses focus.'
& $dotnet @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Advanced verification failed; see console and work/advanced-verification.' }
