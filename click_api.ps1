Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinAPI {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", EntryPoint="SendInput")]
    public static extern uint Send(uint n, INPUT[] p, int cb);
    public static void ForceForeground(IntPtr hWnd) {
        IntPtr fgWnd = GetForegroundWindow();
        uint dummy = 0;
        uint fgThread = GetWindowThreadProcessId(fgWnd, out dummy);
        uint myThread = GetCurrentThreadId();
        AttachThreadInput(fgThread, myThread, true);
        ShowWindow(hWnd, 9);
        SetForegroundWindow(hWnd);
        AttachThreadInput(fgThread, myThread, false);
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        INPUT[] inputs = new INPUT[2];
        inputs[0].type = 0; inputs[0].mi.dwFlags = 0x0002;
        inputs[1].type = 0; inputs[1].mi.dwFlags = 0x0004;
        Send(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf(inputs[0]));
    }
}
"@

$proc = Get-Process -Name 'mAIx'
$hwnd = $proc.MainWindowHandle
Write-Host "HWND: $hwnd"

[WinAPI]::ForceForeground($hwnd)
Start-Sleep -Milliseconds 800

$fg = [WinAPI]::GetForegroundWindow()
Write-Host "ForegroundWindow after: $fg (target: $hwnd)"

# API 관리 클릭 (설정 좌측 메뉴 Y=160)
[WinAPI]::Click(120, 160)
Write-Host "클릭1 완료 (Y=160)"
Start-Sleep -Milliseconds 800
