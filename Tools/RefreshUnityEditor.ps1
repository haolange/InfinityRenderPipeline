# Brings the open InfinityExample Unity Editor to the foreground and triggers Assets > Refresh (Ctrl+R).
# Does not launch a second Unity / -batchmode instance.
param(
    [string]$ProjectPath = "D:\Projects\Unity\InfinityExample",
    [int]$StableMs = 4000,
    [int]$TimeoutMs = 180000
)

$ErrorActionPreference = "Stop"
$logPath = Join-Path $ProjectPath "Logs\Editor.log"
$instancePath = Join-Path $ProjectPath "Library\EditorInstance.json"

if (-not (Test-Path -LiteralPath $instancePath)) {
    throw "Unity EditorInstance.json not found. Is the InfinityExample editor open?"
}

$instance = Get-Content -LiteralPath $instancePath -Raw | ConvertFrom-Json
$unity = Get-Process -Id $instance.process_id -ErrorAction SilentlyContinue
if (-not $unity) {
    throw "Unity process $($instance.process_id) is not running."
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UnityFocus {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetFocus(IntPtr hWnd);
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
    public const byte VK_CONTROL = 0x11;
    public const byte VK_R = 0x52;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public static bool Foreground(IntPtr hwnd) {
        if (hwnd == IntPtr.Zero) return false;
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
        else ShowWindowAsync(hwnd, SW_SHOW);
        IntPtr fg = GetForegroundWindow();
        uint fgPid;
        uint fgTid = GetWindowThreadProcessId(fg, out fgPid);
        uint curTid = GetCurrentThreadId();
        if (fgTid != 0 && fgTid != curTid) AttachThreadInput(curTid, fgTid, true);
        BringWindowToTop(hwnd);
        bool ok = SetForegroundWindow(hwnd);
        SetFocus(hwnd);
        if (fgTid != 0 && fgTid != curTid) AttachThreadInput(curTid, fgTid, false);
        return ok || GetForegroundWindow() == hwnd;
    }

    public static void SendCtrlR() {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(30);
        keybd_event(VK_R, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(30);
        keybd_event(VK_R, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        System.Threading.Thread.Sleep(30);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
"@

$log = Get-Item -LiteralPath $logPath
$markSize = $log.Length
$markTime = $log.LastWriteTimeUtc
Write-Output ("UNITY_PID={0}" -f $unity.Id)
Write-Output ("UNITY_TITLE={0}" -f $unity.MainWindowTitle)
Write-Output ("LOG_MARK_SIZE={0}" -f $markSize)
Write-Output ("LOG_MARK_TIME={0}" -f $markTime)

$hwnd = $unity.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) {
    throw "Unity main window handle is zero."
}

$focused = [UnityFocus]::Foreground($hwnd)
Start-Sleep -Milliseconds 400
$focused = [UnityFocus]::Foreground($hwnd) -or $focused
Write-Output ("FOREGROUND_OK={0}" -f $focused)
Write-Output ("FOREGROUND_HWND={0}" -f [UnityFocus]::GetForegroundWindow())

[UnityFocus]::SendCtrlR()
Write-Output "SENT_CTRL_R=1"

$deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
$sawActivity = $false
$lastSize = $markSize
$lastWrite = $markTime
$stableStart = $null

while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $log.Refresh()
    if ($log.Length -gt $markSize -or $log.LastWriteTimeUtc -gt $markTime) {
        $sawActivity = $true
    }
    if ($log.Length -ne $lastSize -or $log.LastWriteTimeUtc -ne $lastWrite) {
        $lastSize = $log.Length
        $lastWrite = $log.LastWriteTimeUtc
        $stableStart = [DateTime]::UtcNow
        continue
    }
    if ($sawActivity -and $stableStart -ne $null -and (([DateTime]::UtcNow - $stableStart).TotalMilliseconds -ge $StableMs)) {
        Write-Output "REFRESH_STABLE=1"
        break
    }
}

if (-not $sawActivity) {
    Write-Output "REFRESH_STABLE=0"
    Write-Output "REFRESH_NOTE=log did not grow; Unity may not have received Ctrl+R"
}

Write-Output ("LOG_NEW_SIZE={0}" -f $log.Length)
Write-Output ("LOG_NEW_TIME={0}" -f $log.LastWriteTimeUtc)
Write-Output ("LOG_DELTA_BYTES={0}" -f ($log.Length - $markSize))
