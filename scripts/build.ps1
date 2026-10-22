param([switch]$Package, [string]$OutputDirectory = 'artifacts/MacroStudio')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$dotnet = if (Test-Path "$root/.dotnet/dotnet.exe") { "$root/.dotnet/dotnet.exe" } else { 'dotnet' }
$env:DOTNET_CLI_HOME = "$root/work/dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Package) {
    $publishCheck = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + '\'
    $usingPackage = Get-Process -Name MacroStudio,MacroStudio.TestLab -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($publishCheck, [StringComparison]::OrdinalIgnoreCase) }
    if ($usingPackage) { throw 'Close the app and lab in the chosen output folder before packaging, or choose a different -OutputDirectory.' }
}
function Run-Dotnet { & $dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" } }
Run-Dotnet build src/MacroStudio/MacroStudio.csproj -c Release
Run-Dotnet build src/MacroStudio.TestLab/MacroStudio.TestLab.csproj -c Release
Run-Dotnet run --project tests/MacroStudio.Tests -c Release
Run-Dotnet run --project tests/MacroStudio.EditorTests -c Release
$labDest = "$root/src/MacroStudio/bin/Release/net10.0-windows/Lab"
New-Item -ItemType Directory -Force $labDest | Out-Null
Copy-Item "$root/src/MacroStudio.TestLab/bin/Release/net10.0-windows/*" $labDest -Recurse -Force
if ($Package) {
    $publishDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    Run-Dotnet publish src/MacroStudio/MacroStudio.csproj -c Release -r win-x64 --self-contained true -o $publishDirectory
    Run-Dotnet publish src/MacroStudio.TestLab/MacroStudio.TestLab.csproj -c Release -r win-x64 --self-contained true -o "$publishDirectory/Lab"
    Copy-Item samples, docs $publishDirectory -Recurse -Force
    Copy-Item README.md $publishDirectory -Force
    Compress-Archive -Path $publishDirectory -DestinationPath "$publishDirectory-win-x64.zip" -Force
}
