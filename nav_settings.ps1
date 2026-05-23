Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WM2 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", EntryPoint="SendInput")]
    public static extern uint Send(uint n, INPUT[] p, int cb);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(250);
        INPUT[] inp = new INPUT[2];
        inp[0].type = 0; inp[0].mi.dwFlags = 0x0002;
        inp[1].type = 0; inp[1].mi.dwFlags = 0x0004;
        Send(2, inp, System.Runtime.InteropServices.Marshal.SizeOf(inp[0]));
    }
}
"@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name 'mAIx'
$pidCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$hwnd = $proc.MainWindowHandle

[WM2]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 600
Write-Host "FG: $([WM2]::GetForegroundWindow()) Target: $hwnd"

# NavSettingsButton SelectionItem Select
$settingsCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'NavSettingsButton')
$settingsBtn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $settingsCond)
$selP = $settingsBtn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selP.Select()
Write-Host "Settings tab selected"
Start-Sleep -Milliseconds 1000

# 설정 탭의 좌측 메뉴 항목들 좌표 탐색
$panes = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Pane))
Write-Host "Panes: $($panes.Count)"
foreach ($p in $panes) {
    $r = $p.Current.BoundingRectangle
    if ($r.X -ge 50 -and $r.X -le 200 -and $r.Y -ge 100 -and $r.Y -le 300 -and $r.Width -lt 200) {
        Write-Host "Pane: X=$($r.X) Y=$($r.Y) W=$($r.Width) H=$($r.Height) Id='$($p.Current.AutomationId)'"
    }
}
