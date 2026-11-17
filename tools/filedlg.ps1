# Fills in the Open or Save dialog shown by a test NoteSpin (tools\run-test.ps1) and presses its OK button.
#   filedlg.ps1 <full path of the file>
# Finds the dialog's controls with UI Automation, then talks to those two windows directly
# (WM_SETTEXT, BM_CLICK), so no keystrokes go anywhere else.
param([string]$Path)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr h, int msg, System.IntPtr w, string l);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr PostMessage(System.IntPtr h, int msg, System.IntPtr w, System.IntPtr l);
'@
$ae = [System.Windows.Automation.AutomationElement]
$pids = (Get-Process NoteSpin).Id
for ($i = 0; $i -lt 40; $i++) {
  $dlg = $ae::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($ae::ClassNameProperty, '#32770'))) |
    Where-Object { $pids -contains $_.Current.ProcessId } | Select-Object -First 1
  if ($dlg) { break }
  Start-Sleep -Milliseconds 250
}
if (-not $dlg) { throw 'No dialog' }
"Dialog: " + $dlg.Current.Name
$edit = $null
for ($i = 0; $i -lt 20 -and -not $edit; $i++) {
  $edit = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.AndCondition(
      (New-Object System.Windows.Automation.OrCondition(
        (New-Object System.Windows.Automation.PropertyCondition($ae::AutomationIdProperty, '1001')),   # file name box
        (New-Object System.Windows.Automation.PropertyCondition($ae::AutomationIdProperty, '1152')))),  # folder box (folder picker)
      (New-Object System.Windows.Automation.PropertyCondition($ae::ClassNameProperty, 'Edit')))))
  if (-not $edit) { Start-Sleep -Milliseconds 250 }
}
if (-not $edit) { throw 'No file name box' }
$ok = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Children,
  (New-Object System.Windows.Automation.PropertyCondition($ae::AutomationIdProperty, '1')))
[void][W.U]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $Path)   # WM_SETTEXT
[void][W.U]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
"Pressed " + $ok.Current.Name
