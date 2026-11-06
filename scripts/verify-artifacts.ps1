<#
  Verifies release artifacts. Exits non-zero (and prints why) if anything is wrong, so CI fails.
  Checks: expected files exist, SHA256SUMS lists every file with the correct hash and nothing extra,
  each portable zip contains its exe + LICENSE + third-party notices, and (with -RequireInstallers) both installers exist.
#>
param(
  [Parameter(Mandatory)][string]$ArtifactsDir,
  [Parameter(Mandatory)][string]$Version,
  [switch]$RequireInstallers
)
$ErrorActionPreference = "Stop"
$errors = New-Object System.Collections.Generic.List[string]
function Fail($m) { $script:errors.Add($m) }

$zips = @{
  "AutoPortal-System-Guardian-Portable-$Version.zip"      = "AutoPortalSystemGuardian.exe"
  "AutoPortal-Connectivity-Manager-Portable-$Version.zip" = "AutoPortalConnectivityManager.exe"
}
$expected = @($zips.Keys)
if ($RequireInstallers) {
  $expected += "AutoPortal-System-Guardian-Setup-$Version.exe", "AutoPortal-Connectivity-Manager-Setup-$Version.exe"
}

foreach ($f in $expected) {
  if (-not (Test-Path (Join-Path $ArtifactsDir $f))) { Fail "Missing artifact: $f" }
}

$sums = Join-Path $ArtifactsDir "SHA256SUMS.txt"
if (-not (Test-Path $sums)) { Fail "Missing SHA256SUMS.txt" }
else {
  $listed = @{}
  foreach ($line in Get-Content $sums) {
    if ($line -match '^([0-9a-f]{64})  (.+)$') { $listed[$Matches[2]] = $Matches[1] }
    elseif ($line.Trim()) { Fail "Malformed checksum line: $line" }
  }
  foreach ($f in $expected) {
    $p = Join-Path $ArtifactsDir $f
    if (-not (Test-Path $p)) { continue }
    if (-not $listed.ContainsKey($f)) { Fail "Not listed in SHA256SUMS.txt: $f"; continue }
    $actual = (Get-FileHash $p -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $listed[$f]) { Fail "Checksum mismatch for ${f}: expected $($listed[$f]) but file is $actual" }
  }
  foreach ($name in $listed.Keys) {
    if (-not (Test-Path (Join-Path $ArtifactsDir $name))) { Fail "SHA256SUMS.txt lists a file that does not exist: $name" }
  }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($z in $zips.Keys) {
  $p = Join-Path $ArtifactsDir $z
  if (-not (Test-Path $p)) { continue }
  try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($p)
    $names = $zip.Entries | ForEach-Object { $_.Name }
    $zip.Dispose()
    foreach ($need in @($zips[$z], "LICENSE", "THIRD-PARTY-NOTICES.md")) {
      if ($names -notcontains $need) { Fail "$z does not contain $need" }
    }
  } catch { Fail "$z is not a valid zip: $($_.Exception.Message)" }
}

if ($errors.Count) { $errors | ForEach-Object { [Console]::Error.WriteLine("ERROR: $_") }; exit 1 }
Write-Host "Artifacts verified: $($expected.Count) file(s), checksums OK."
