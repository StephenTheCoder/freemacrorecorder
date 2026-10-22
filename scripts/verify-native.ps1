param([string]$EvidenceDirectory = 'work/native-verification')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$dotnet = if (Test-Path "$root/.dotnet/dotnet.exe") { "$root/.dotnet/dotnet.exe" } else { (Get-Command dotnet).Source }
$env:DOTNET_CLI_HOME = "$root/work/dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish src/MacroStudio.TestLab -c Release -r win-x64 --self-contained true -o artifacts/NativeLab
if ($LASTEXITCODE -ne 0) { throw 'Lab publish failed. Close a previous native-test lab before retrying.' }
& $dotnet build tests/MacroStudio.NativeTests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Native test build failed.' }
Write-Host 'Activate the freshly launched Interaction Lab. The test sends input only while that lab owns foreground focus.'
& $dotnet tests/MacroStudio.NativeTests/bin/Release/net10.0-windows10.0.19041.0/MacroStudio.NativeTests.dll $dotnet "$root/artifacts/NativeLab/MacroStudio.TestLab.exe" ([IO.Path]::GetFullPath($EvidenceDirectory))
if ($LASTEXITCODE -ne 0) { throw 'Native integration failed; see the evidence directory.' }
