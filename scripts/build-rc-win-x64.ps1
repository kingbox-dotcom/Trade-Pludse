param(
    [string]$Version = "1.0.0-rc1"
)
$ErrorActionPreference = "Stop"

Write-Host "=== 1. .NET 10 Restore, Build & Test (Release x64) ==="
dotnet restore TradePulse.sln
dotnet build TradePulse.sln -c Release -p:Platform=x64 --no-restore
dotnet test TradePulse.sln -c Release -p:Platform=x64 --no-build --verbosity normal

Write-Host "`n=== 2. Python Compile Check & Pytest ==="
python -m compileall -q src\sidecar\tradepulse_sidecar
$env:PYTHONPATH = (Resolve-Path "src\sidecar").Path
python -m pytest src\sidecar\tests -v

Write-Host "`n=== 3. Publish Self-Contained Windows x64 Portable ==="
$outDir = "artifacts\rc\TradePulse-$Version-win-x64"
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
dotnet publish src\TradePulse.App\TradePulse.App.csproj -c Release -r win-x64 --self-contained true -o $outDir
Copy-Item -Recurse -Force src\sidecar $outDir\sidecar

$zipPath = "artifacts\rc\TradePulse-$Version-win-x64-portable.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
"$hash  $(Split-Path $zipPath -Leaf)" | Out-File -Encoding ascii "artifacts\rc\SHA256SUMS.txt"
Write-Host "`nDONE: $zipPath"
Write-Host "SHA256: $hash"
