Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WM {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", EntryPoint="SendInput")]
    public static extern uint Send(uint n, INPUT[] p, int cb);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(300);
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = 0; inputs[0].mi.dwFlags = 0x0002;
        inputs[1].type = 0; inputs[1].mi.dwFlags = 0x0004;
        Send(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf(inputs[0]));
    }
}
"@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name 'mAIx'
$pidCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$hwnd = $proc.MainWindowHandle

# 앱 포커스
[WM]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400

# 제목바 클릭으로 앱 활성화
[WM]::Click(700, 10)
Start-Sleep -Milliseconds 400

# NavSettingsButton SelectionItem 선택
$settingsCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'NavSettingsButton')
$settingsBtn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $settingsCond)
$selPattern = $settingsBtn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selPattern.Select()
Write-Host "Settings tab selected"
Start-Sleep -Milliseconds 800

# 설정 탭에서 모든 텍스트 요소 좌표 재확인
$txtCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
$texts = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
Write-Host "Text count: $($texts.Count)"

$apiEl = $null
foreach ($t in $texts) {
    $n = $t.Current.Name
    if ($n.Length -gt 0 -and $n.Length -lt 20) {
        $r = $t.Current.BoundingRectangle
        Write-Host "TEXT: '$n' Y=$($r.Y) X=$($r.X)"
        if ($n -match 'API|api') { $apiEl = $t }
    }
}

if ($apiEl) {
    $rect = $apiEl.Current.BoundingRectangle
    $cx = [int]($rect.X + $rect.Width / 2)
    $cy = [int]($rect.Y + $rect.Height / 2)
    Write-Host "API element at X=$cx Y=$cy"
    [WM]::Click($cx, $cy)
    Write-Host "Clicked API element"
}
Start-Sleep -Milliseconds 1000
