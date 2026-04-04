Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MouseHelper {
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
        System.Threading.Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
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

# Get app window bounds
$appRect = $app.Current.BoundingRectangle
Write-Host "APP_RECT|X=$($appRect.X) Y=$($appRect.Y) W=$($appRect.Width) H=$($appRect.Height)"

# Find all elements with bounding rectangles in submenu area
$allCond = [System.Windows.Automation.Condition]::TrueCondition
$all = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $allCond)

Write-Host "ELEMENT_POSITIONS:"
foreach ($e in $all) {
    $name = $e.Current.Name
    $rect = $e.Current.BoundingRectangle
    $offscreen = $e.Current.IsOffscreen
    $ctType = $e.Current.ControlType.ProgrammaticName
    # Find elements in the submenu column (left side, below settings header)
    if (-not $offscreen -and $rect.Width -gt 0 -and $rect.Height -gt 0 -and $rect.Width -lt 300 -and $rect.X -gt 150 -and $rect.X -lt 400) {
        $automId = $e.Current.AutomationId
        Write-Host "POS|$ctType|$automId|$name|X=$($rect.X) Y=$($rect.Y) W=$($rect.Width) H=$($rect.Height)"
    }
}
Write-Host "DONE"
