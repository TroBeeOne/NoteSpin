# Starts NoteSpin for testing: a throwaway data folder, developer tools on, and the DevTools protocol on
# port 9333 so tools\cdp.mjs can drive it. Closes any NoteSpin that's already running first.
#   .\tools\run-test.ps1 [-Data <folder>] [-Fresh] [-Exe <path>] [-Size 1400x900] [-Open <file>] [-Embedded]
param(
  [string]$Data = (Join-Path $PSScriptRoot 'out\data'),
  [switch]$Fresh,
  [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Debug\net48\win-x64\NoteSpin.exe'),
  [string]$Size = '1400x900',
  [string]$Open = '',
  [switch]$Embedded,  # use the page compiled into the exe (as people get it), not the web folder
  [string]$Notebooks = ''   # stands in for Documents\NoteSpin (else the notebooks stay in the data folder)
)
$ErrorActionPreference = 'Stop'
Get-Process NoteSpin -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
if ($Fresh -and (Test-Path $Data)) { Remove-Item $Data -Recurse -Force }
New-Item -ItemType Directory -Force $Data | Out-Null

# window size and place for repeatable screenshots
$w, $h = $Size -split 'x'
$env:NOTESPIN_DATA = $Data
$env:NOTESPIN_DEVTOOLS = '1'
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9333'
$env:NOTESPIN_WINDOW = "40,40,$w,$h"
$env:NOTESPIN_NOTEBOOKS = $Notebooks
$env:NOTESPIN_WEB = if ($Embedded) { '' } else { Join-Path (Split-Path -Parent $PSScriptRoot) 'web' }   # page served from disk: reload to see changes
if ($Open) { Start-Process -FilePath $Exe -ArgumentList "`"$Open`"" } else { Start-Process -FilePath $Exe }
"Started $Exe (data: $Data)"
