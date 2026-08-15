# Brings the open InfinityExample Unity Editor to the foreground and screenshots its window.
param(
    [string]$ProjectPath = "D:\Projects\Unity\InfinityExample",
    [string]$OutPath = "D:\Projects\Unity\InfinityExample\Logs\unity_capture.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$instancePath = Join-Path $ProjectPath "Library\EditorInstance.json"
$instance = Get-Content -LiteralPath $instancePath -Raw | ConvertFrom-Json
$unity = Get-Process -Id $instance.process_id

Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public static class WinCap {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint id);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    public static void Focus(IntPtr hwnd) {
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9);
        IntPtr fg = GetForegroundWindow();
        uint pid;
        uint fgTid = GetWindowThreadProcessId(fg, out pid);
        uint cur = GetCurrentThreadId();
        if (fgTid != 0 && fgTid != cur) AttachThreadInput(cur, fgTid, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (fgTid != 0 && fgTid != cur) AttachThreadInput(cur, fgTid, false);
    }
}
"@

$hwnd = $unity.MainWindowHandle
[WinCap]::Focus($hwnd)
Start-Sleep -Milliseconds 1200

$rect = New-Object RECT
[WinCap]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Output ("WINDOW={0},{1} {2}x{3}" -f $rect.Left, $rect.Top, $width, $height)

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$gfx.Dispose()
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Output ("SAVED={0}" -f $OutPath)
Write-Output ("BYTES={0}" -f (Get-Item -LiteralPath $OutPath).Length)
