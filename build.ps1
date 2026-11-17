# Builds the portable NoteSpin folder in .\dist\NoteSpin
# Needs the .NET SDK (any recent version) on the build PC only; the people using NoteSpin need nothing installed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'dist\NoteSpin'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $here 'NoteSpin.csproj') -c Release -o $out --nologo
if ($LASTEXITCODE) { throw 'Build failed' }

# Keep only what the app uses: it hosts WebView2 directly (no WinForms/WPF control) and needs no docs or symbols
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path @(
  (Join-Path $out 'Microsoft.Web.WebView2.WinForms.dll'),
  (Join-Path $out 'Microsoft.Web.WebView2.Wpf.dll'),
  (Join-Path $out 'runtimes')
)
Get-ChildItem $out -Recurse -Include *.xml, *.pdb | Remove-Item -Force

Get-ChildItem $out | Select-Object Name, @{ n = 'KB'; e = { [math]::Round($_.Length / 1KB) } } | Format-Table -AutoSize | Out-String
'Total: {0:N1} MB  ->  {1}' -f ((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), $out
