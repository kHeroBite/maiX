Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WM3 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", EntryPoint="SendInput")]
    public static extern uint Send(uint n, INPUT[] p, int cb);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        INPUT[] inp = new INPUT[2];
        inp[0].type = 0; inp[0].mi.dwFlags = 0x0002;
        inp[1].type = 0; inp[1].mi.dwFlags = 0x0004;
        Send(2, inp, System.Runtime.InteropServices.Marshal.SizeOf(inp[0]));
    }
}
"@

$fg = [WM3]::GetForegroundWindow()
Write-Host "Current FG: $fg"

# API 관리 메뉴 클릭 (Y=160)
[WM3]::Click(120, 160)
Write-Host "Clicked Y=160"
Start-Sleep -Milliseconds 800

$fg2 = [WM3]::GetForegroundWindow()
Write-Host "After click FG: $fg2"
