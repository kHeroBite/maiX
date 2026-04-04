Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class MouseHelper {
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);
    public const int MOUSEEVENTF_LEFTDOWN = 0x02;
    public const int MOUSEEVENTF_LEFTUP = 0x04;
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
        System.Threading.Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
        System.Threading.Thread.Sleep(500);
    }
}
"@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Output "APP_NOT_RUNNING"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Output "WINDOW_NOT_FOUND"; exit 1 }

# Find NavSettings button
$btnCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NavSettingsButton")
$navBtn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if (-not $navBtn) { Write-Output "NAV_BTN_NOT_FOUND"; exit 1 }

# Click settings navigation
$invokePattern = $navBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invokePattern.Invoke()
Start-Sleep -Milliseconds 800

# Find sync settings sub-item
$syncCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, "syncSettings")
$syncItem = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $syncCond)
if ($syncItem) {
    $invokeSync = $syncItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invokeSync.Invoke()
    Start-Sleep -Milliseconds 800
}

Write-Output "=== STEP1: Navigated to Settings ==="

# Click AI Analysis tab - coordinates from previous exploration (Y~471)
Write-Output "Clicking AI Analysis tab at (468, 471)"
[MouseHelper]::Click(468, 471)
Start-Sleep -Milliseconds 1000

# Try different Y coordinates if needed
Write-Output "=== STEP2: Checking RadioButtons after AI tab click ==="

$radioCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
$radioButtons = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)

Write-Output "RadioButton count: $($radioButtons.Count)"
foreach ($rb in $radioButtons) {
    try {
        $selPattern = $rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSelected = $selPattern.Current.IsSelected
        $name = $rb.Current.Name
        $aid = $rb.Current.AutomationId
        $rect = $rb.Current.BoundingRectangle
        Write-Output "RB: Name=[$name] AID=[$aid] Selected=[$isSelected] X=$([int]$rect.X) Y=$([int]$rect.Y)"
    } catch {
        $name = $rb.Current.Name
        Write-Output "RB_NO_PATTERN: Name=[$name]"
    }
}

# Also dump all Text controls in content area to identify AI tab
Write-Output "=== STEP3: Text controls in content area ==="
$textCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
$texts = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
foreach ($t in $texts) {
    $rect = $t.Current.BoundingRectangle
    if ($rect.X -gt 430) {
        $name = $t.Current.Name
        Write-Output "TEXT: [$name] X=$([int]$rect.X) Y=$([int]$rect.Y)"
    }
}
