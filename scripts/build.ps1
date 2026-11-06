param(
  [string]$Version = "0.1.0",
  [switch]$SkipTests,          # CI runs the tests as a separate step
  [switch]$RequireInstallers   # releases must contain both installers; fail if Inno Setup is missing
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out | Out-Null

if (-not $SkipTests) {
  dotnet test "$root" -c Release --nologo
  if ($LASTEXITCODE) { throw "Tests failed" }
}

$apps = @(
  @{ Name = "System-Guardian";       Proj = "src/ui/AutoPortal.Guardian.App.csproj";                  Dir = "publish";              Iss = "guardian.iss" },
  @{ Name = "Connectivity-Manager";  Proj = "src/connectivity/App/AutoPortal.Connectivity.App.csproj"; Dir = "publish-connectivity"; Iss = "connectivity.iss" }
)
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($RequireInstallers -and -not $iscc) { throw "Inno Setup (iscc) is required but was not found." }

foreach ($a in $apps) {
  $pub = Join-Path $out $a.Dir
  dotnet publish "$root/$($a.Proj)" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $pub
  if ($LASTEXITCODE) { throw "Publish failed: $($a.Name)" }
  Copy-Item "$root/LICENSE","$root/THIRD-PARTY-NOTICES.md" $pub
  Compress-Archive "$pub/*" "$out/AutoPortal-$($a.Name)-Portable-$Version.zip"
  if ($iscc) {
    & iscc "/DAppVersion=$Version" "/DSourceDir=$pub" "/O$out" "$root/installer/$($a.Iss)"
    if ($LASTEXITCODE) { throw "Installer build failed: $($a.Name)" }
  }
  else { Write-Warning "Inno Setup (iscc) not found - installer for $($a.Name) skipped." }
}

Get-ChildItem $out -File | Get-FileHash -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content "$out/SHA256SUMS.txt"

# Verify our own output so a broken package fails the build instead of reaching users.
$verifyArgs = @{ ArtifactsDir = $out; Version = $Version }
if ($RequireInstallers) { $verifyArgs.RequireInstallers = $true }
& "$PSScriptRoot/verify-artifacts.ps1" @verifyArgs
if ($LASTEXITCODE) { throw "Artifact verification failed" }
Get-Content "$out/SHA256SUMS.txt"
