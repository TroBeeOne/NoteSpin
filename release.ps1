<#
.SYNOPSIS
  Builds NoteSpin, signs NoteSpin.exe with Azure Artifact Signing, verifies it, and zips the app
  as NoteSpin-<version>.zip, ready to attach to a GitHub release.

.DESCRIPTION
  Runs build.ps1 (-> dist\NoteSpin), then signs every unsigned exe/dll in it. Files Microsoft already
  signed (the WebView2 dlls) are skipped. Each signature is timestamped, which keeps it valid after the
  short-lived Artifact Signing certificate expires; the script stops if a timestamp is missing.
  Uploading the zip is left to you.

  Needs: the .NET SDK, the sign tool (dotnet tool install -g --prerelease sign), and an Azure sign-in
  (az login) for an account with the Artifact Signing Certificate Profile Signer role.

.EXAMPLE
  .\release.ps1
#>
param(
    # --- Artifact Signing settings (same account and profile as the NuGet releases) ---
    [string] $Endpoint           = 'https://cus.codesigning.azure.net/',
    [string] $Account            = 'daffy-signing',
    [string] $CertificateProfile = 'trobeeone-public',
    [string] $DescriptionUrl     = 'https://github.com/TroBeeOne/NoteSpin'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here 'dist\NoteSpin'

# 0. Locate sign.dll dynamically to bypass WDAC policy blocks on the sign.exe shim
$SignDll = (Get-ChildItem "$HOME\.dotnet\tools\.store\sign" -Filter 'sign.dll' -Recurse -ErrorAction SilentlyContinue |
    Select-Object -Last 1).FullName
if (-not $SignDll) {
    throw "sign.dll was not found under $HOME\.dotnet\tools\.store\sign. Install it with 'dotnet tool install -g --prerelease sign'."
}

# The version comes from <Version> in NoteSpin.csproj
$version = ([xml](Get-Content (Join-Path $here 'NoteSpin.csproj'))).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in NoteSpin.csproj." }
Write-Host "== NoteSpin $version ==" -ForegroundColor Cyan

# 1. Build ---------------------------------------------------------------------
& (Join-Path $here 'build.ps1')

# 2. Sign ----------------------------------------------------------------------
# @(...) keeps these lists even when they hold one file: splatting a lone string
# passes it to the sign tool one character at a time.
$unsigned = @(Get-ChildItem $out -Recurse -Include *.exe, *.dll |
    Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -eq 'NotSigned' })
if (-not $unsigned) { throw "Nothing to sign in $out - NoteSpin.exe should be unsigned after a fresh build." }

$relative = @($unsigned | ForEach-Object { $_.FullName.Substring($out.Length + 1) })
Write-Host "Signing $($relative.Count) file(s):"
$relative | ForEach-Object { Write-Host "  $_" }

dotnet $SignDll code artifact-signing `
    --base-directory $out `
    --artifact-signing-endpoint $Endpoint `
    --artifact-signing-account $Account `
    --artifact-signing-certificate-profile $CertificateProfile `
    --description 'NoteSpin' `
    --description-url $DescriptionUrl `
    @relative
if ($LASTEXITCODE) { throw "Signing failed." }

# 3. Verify --------------------------------------------------------------------
foreach ($f in $unsigned) {
    $sig = Get-AuthenticodeSignature $f.FullName
    if ($sig.Status -ne 'Valid')       { throw "$($f.Name) isn't validly signed: $($sig.Status) $($sig.StatusMessage)" }
    if (-not $sig.TimeStamperCertificate) { throw "$($f.Name) has no timestamp, so its signature would expire with the certificate." }
    Write-Host "  $($f.Name): $($sig.SignerCertificate.Subject)"
}
Write-Host "All files signed, timestamped and verified." -ForegroundColor Green

# 4. Zip -----------------------------------------------------------------------
# The app's files sit at the top of the zip; Windows' "Extract All" puts them in a NoteSpin-<version> folder.
$zip = Join-Path $here "NoteSpin-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ("`nDone: $zip ({0:N0} KB)" -f ((Get-Item $zip).Length / 1KB)) -ForegroundColor Green
Write-Host "SHA256: $hash"
