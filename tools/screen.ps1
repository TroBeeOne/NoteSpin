# Captures the NoteSpin window as it appears on screen (what the person sees, not just the page):  screen.ps1 <out.png>
param([string]$Out)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name R -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
'@
$h = (Get-Process NoteSpin | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1).MainWindowHandle
[void][W.R]::SetForegroundWindow($h)
$r = New-Object W.R+RECT
[void][W.R]::GetWindowRect($h, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save($Out); $g.Dispose(); $bmp.Dispose()
