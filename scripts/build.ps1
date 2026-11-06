param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out | Out-Null

dotnet test "$root" -c Release --nologo
if ($LASTEXITCODE) { throw "Tests failed" }

$pub = Join-Path $out "publish"
dotnet publish "$root/src/ui/AutoPortal.Guardian.App.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $pub
if ($LASTEXITCODE) { throw "Publish failed" }

Copy-Item "$root/LICENSE","$root/THIRD-PARTY-NOTICES.md" $pub
Compress-Archive "$pub/*" "$out/AutoPortal-System-Guardian-Portable-$Version.zip"

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) { & iscc "/DAppVersion=$Version" "/DSourceDir=$pub" "/O$out" "$root/installer/guardian.iss" }
else { Write-Warning "Inno Setup (iscc) not found - installer skipped." }

Get-ChildItem $out -File | Get-FileHash -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content "$out/SHA256SUMS.txt"
Get-Content "$out/SHA256SUMS.txt"
