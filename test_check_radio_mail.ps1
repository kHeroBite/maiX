Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MouseHelper2 {
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
        System.Threading.Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
        System.Threading.Thread.Sleep(500);
    }
}
"@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

function Check-RadioButtons($label) {
    $rbCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::RadioButton)
    $rbs = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rbCond)
    Write-Host "=== $label ==="
    foreach ($rb in $rbs) {
        $rect = $rb.Current.BoundingRectangle
        if ($rect.X -gt 430 -and -not $rb.Current.IsOffscreen) {
            try {
                $selPat = $rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                $isSelected = $selPat.Current.IsSelected
                $name = $rb.Current.Name
                Write-Host "RB|$name|Selected=$isSelected|X=$($rect.X) Y=$($rect.Y)"
            } catch {
                Write-Host "RB_ERR|$($rb.Current.Name)"
            }
        }
    }
}

# Check current state (Mail tab - already open)
Check-RadioButtons "MAIL_TAB"

# Click Calendar tab (X=447, Y=392)
Write-Host "Clicking Calendar tab..."
[MouseHelper2]::Click(468, 401)
Start-Sleep -Milliseconds 800

Check-RadioButtons "CALENDAR_TAB"

# Click Chat tab (X=447, Y=427)
Write-Host "Clicking Chat tab..."
[MouseHelper2]::Click(468, 436)
Start-Sleep -Milliseconds 800

Check-RadioButtons "CHAT_TAB"

Write-Host "DONE"
