# Stress test for the "Ctrl+V emptied the canvas" report.
#
# Draw two rectangles, Ctrl+A / Ctrl+C / Ctrl+V, then measure whether the canvas still
# has content. Fifty rounds, self-contained in one process so the per-step PowerShell
# startup does not dominate the runtime.
#
#   .\stress-copy-paste.ps1 -ProcessId <pid> -Rounds 50

param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$Rounds = 50,
    [string]$OutDir = "$env:TEMP\stress-copy-paste"
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class STUI {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
}
"@

# --- window helpers -------------------------------------------------------------

function Get-Win {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { throw "PolyCut (PID $ProcessId) is gone." }
    $p.Refresh()
    if ($p.MainWindowHandle -eq 0) { throw "PolyCut (PID $ProcessId) has no window." }
    return $p
}

function Get-Rect($p) {
    $r = New-Object STUI+RECT
    try {
        $hr = [STUI]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r))
        if ($hr -eq 0 -and $r.Right -gt $r.Left) { return $r }
    } catch { }
    [STUI]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    return $r
}

function Focus-Win($p) {
    $h = $p.MainWindowHandle
    if ([STUI]::IsIconic($h)) { [STUI]::ShowWindow($h, 9) | Out-Null }
    $fg = [STUI]::GetForegroundWindow()
    $fgPid = 0
    $fgThread = [STUI]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $myThread = [STUI]::GetCurrentThreadId()
    if ($fgThread -ne $myThread -and $fgThread -ne 0) {
        [STUI]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
        [STUI]::BringWindowToTop($h) | Out-Null
        [STUI]::SetForegroundWindow($h) | Out-Null
        [STUI]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
    } else {
        [STUI]::SetForegroundWindow($h) | Out-Null
    }
    Start-Sleep -Milliseconds 180
}

function Click-Rel($p, [int]$x, [int]$y) {
    $r = Get-Rect $p
    Focus-Win $p
    [STUI]::SetCursorPos(($r.Left + $x), ($r.Top + $y)) | Out-Null
    Start-Sleep -Milliseconds 70
    [STUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [STUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 110
}

function Drag-Rel($p, [int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    $r = Get-Rect $p
    $ax1 = $r.Left + $x1; $ay1 = $r.Top + $y1
    $ax2 = $r.Left + $x2; $ay2 = $r.Top + $y2
    Focus-Win $p
    [STUI]::SetCursorPos($ax1, $ay1) | Out-Null
    Start-Sleep -Milliseconds 90
    [STUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    $steps = 10
    for ($i = 1; $i -le $steps; $i++) {
        $mx = [int]($ax1 + (($ax2 - $ax1) * $i / $steps))
        $my = [int]($ay1 + (($ay2 - $ay1) * $i / $steps))
        [STUI]::SetCursorPos($mx, $my) | Out-Null
        Start-Sleep -Milliseconds 20
    }
    [STUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 170
}

function Send-Keys($p, [string]$keys) {
    Focus-Win $p
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 230
}

function Save-Shot($p, [string]$path) {
    $r = Get-Rect $p
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [STUI]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Bright pixels inside the bed area. Geometry is drawn with a near-white stroke on a dark
# grid, so a count of very light pixels is a reliable "is anything on the canvas" probe.
function Count-Ink($p) {
    $r = Get-Rect $p
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [STUI]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()

    # Bed interior only; the toolpath cannot be outside it and the frame is drawn lighter.
    $x0 = [int]($w * 0.24); $x1 = [int]($w * 0.54)
    $y0 = [int]($h * 0.30); $y1 = [int]($h * 0.78)

    $count = 0
    $rect = New-Object System.Drawing.Rectangle($x0, $y0, ($x1 - $x0), ($y1 - $y0))
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $data.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        for ($i = 0; $i -lt $bytes.Length; $i += 4) {
            if ($bytes[$i] -gt 150 -and $bytes[$i + 1] -gt 150 -and $bytes[$i + 2] -gt 150) { $count++ }
        }
    } finally {
        $bmp.UnlockBits($data)
        $bmp.Dispose()
    }
    return $count
}

# --- run ------------------------------------------------------------------------

$toolRectY = 380
$results = @()

$p = Get-Win
Write-Host "target pid=$ProcessId, rounds=$Rounds"

for ($round = 1; $round -le $Rounds; $round++) {
    $p = Get-Win

    # Start from an empty canvas.
    Send-Keys $p '^a'
    Send-Keys $p '{DELETE}'
    Start-Sleep -Milliseconds 150

    # Select the rectangle tool once per round; the shortcuts above do not change it, but a
    # stray click could, and a wrong tool would silently draw nothing.
    Click-Rel $p 43 $toolRectY

    Drag-Rel $p 750 650 900 780
    Drag-Rel $p 1000 800 1150 930
    Start-Sleep -Milliseconds 200

    $before = Count-Ink $p

    Send-Keys $p '^a'
    Send-Keys $p '^c'
    Start-Sleep -Milliseconds 150
    Send-Keys $p '^v'
    Start-Sleep -Milliseconds 350

    $after = Count-Ink $p

    $state = 'ok'
    if ($p.HasExited) { $state = 'process-gone' }
    elseif ($before -lt 50) { $state = 'setup-failed' }        # nothing was drawn to begin with
    elseif ($after -lt ($before * 0.5)) { $state = 'CANVAS-EMPTIED' }

    Write-Host ("round {0,2}: before={1,5} after={2,5} -> {3}" -f $round, $before, $after, $state)

    if ($state -ne 'ok') {
        $shot = Join-Path $OutDir ("fail-{0:D2}-{1}.png" -f $round, $state)
        Save-Shot $p $shot
        $results += [pscustomobject]@{ Round = $round; Before = $before; After = $after; State = $state; Shot = $shot }
    }

    if ($p.HasExited) { Write-Host "PolyCut exited at round $round"; break }
}

# Leave the instance clean.
try {
    $p = Get-Win
    Send-Keys $p '^a'
    Send-Keys $p '{DELETE}'
} catch { }

Write-Host ""
if ($results.Count -eq 0) {
    Write-Host "RESULT: 0 failures in $Rounds rounds"
} else {
    Write-Host "RESULT: $($results.Count) failure(s) in $Rounds rounds"
    $results | Format-Table -AutoSize
}
