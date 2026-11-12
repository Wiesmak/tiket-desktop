# Local evidence only; does not change Windows theme, DPI, sign-in, or backend state.
param([ValidateSet('Cashier','Administration','Settings')][string]$Destination = 'Cashier',
      [ValidateSet('System','Light','Dark')][string]$Theme = 'System',
      [switch]$KeyboardSmoke,
      [int]$Width = 1366, [int]$Height = 768)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowEvidence {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int z,bool repaint);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@
$process = Get-Process -Name 'Tiket Desktop' | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (!$process) { throw 'Launch the app first.' }
$handle = $process.MainWindowHandle
[WindowEvidence]::MoveWindow($handle,0,0,$Width,$Height,$true) | Out-Null
[WindowEvidence]::SetForegroundWindow($handle) | Out-Null
$window = [Windows.Automation.AutomationElement]::FromHandle($handle)
if ($Theme -ne 'System') {
    $settings = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Settings'))
    $settings.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $appearance = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Appearance'))
    $appearance.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $choice = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Theme))
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $appearance.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 500
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
}
$condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Destination)
$item = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
if (!$item) { throw "Missing navigation item: $Destination" }
$pattern = $null
if ($item.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)) {
    $pattern.Select()
} elseif ($item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)) {
    $pattern.Invoke()
} else { throw 'Navigation does not expose an automation pattern.' }
Start-Sleep -Milliseconds 700
if ($KeyboardSmoke -and $Destination -eq 'Cashier') {
    $player = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'DemoPlayer'))
    $player.SetFocus()
    $keys = New-Object -ComObject WScript.Shell
    foreach ($key in @('{TAB}','{TAB}','{TAB}','{TAB}')) {
        if ([WindowEvidence]::GetForegroundWindow() -ne $handle) { throw 'App lost foreground; refusing to send keys to another application.' }
        $keys.SendKeys($key)
        Start-Sleep -Milliseconds 150
        Write-Output "Keyboard focus: $([Windows.Automation.AutomationElement]::FocusedElement.Current.Name)"
    }
}
$elements = $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
$elements | ForEach-Object { if ($_.Current.Name) { "$($_.Current.ControlType.ProgrammaticName): $($_.Current.Name) [enabled=$($_.Current.IsEnabled)]" } }
Write-Output "DPI: $([WindowEvidence]::GetDpiForWindow($handle)); requested window: $Width x $Height"
$bounds = $window.Current.BoundingRectangle
$bitmap = [Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $dc = $graphics.GetHdc()
    try {
        if (![WindowEvidence]::PrintWindow($handle,$dc,2)) { throw 'Window capture failed.' }
    } finally { $graphics.ReleaseHdc($dc) }
    $output = Join-Path (Split-Path $PSScriptRoot -Parent) ".awb/$Destination-$Theme-$Width-$Height.png"
    $bitmap.Save($output,[Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Screenshot: $output"
} finally { $graphics.Dispose(); $bitmap.Dispose() }
