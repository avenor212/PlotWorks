# Stress test for the "clicking 组合 made the app disappear" report.
#
# Round 5 hit it twice. That run predates the SW_RESTORE fix in polycut-ui.ps1, so the
# old clicks may never have landed on the button at all. This repeats the exact path with
# working input: two shapes -> Ctrl+A -> 组合 -> 分组.
#
#   .\stress-group.ps1 -ProcessId <pid> -Rounds 50

param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$Rounds = 50,
    [string]$OutDir = "$env:TEMP\stress-group"
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class GRUI {
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

# Layout constants for a maximised 2560x1551 window.
$ToolRectX = 43;   $ToolRectY = 380
$ToolEllipseY = 437
$GroupBtnX = 2203; $GroupBtnY = 1510

function Get-Win {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { return $null }
    $p.Refresh()
    if ($p.MainWindowHandle -eq 0) { return $null }
    return $p
}

function Get-Rect($p) {
    $r = New-Object GRUI+RECT
    try {
        $hr = [GRUI]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r))
        if ($hr -eq 0 -and $r.Right -gt $r.Left) { return $r }
    } catch { }
    [GRUI]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    return $r
}

function Focus-Win($p) {
    $h = $p.MainWindowHandle
    if ([GRUI]::IsIconic($h)) { [GRUI]::ShowWindow($h, 9) | Out-Null }
    $fg = [GRUI]::GetForegroundWindow()
    $fgPid = 0
    $fgThread = [GRUI]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $myThread = [GRUI]::GetCurrentThreadId()
    if ($fgThread -ne $myThread -and $fgThread -ne 0) {
        [GRUI]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
        [GRUI]::BringWindowToTop($h) | Out-Null
        [GRUI]::SetForegroundWindow($h) | Out-Null
        [GRUI]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
    } else {
        [GRUI]::SetForegroundWindow($h) | Out-Null
    }
    Start-Sleep -Milliseconds 160
}

function Click-Rel($p, [int]$x, [int]$y) {
    $r = Get-Rect $p
    Focus-Win $p
    [GRUI]::SetCursorPos(($r.Left + $x), ($r.Top + $y)) | Out-Null
    Start-Sleep -Milliseconds 60
    [GRUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [GRUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
}

function Drag-Rel($p, [int]$x1, [int]$y1, [int]$x2, [int]$y2) {
    $r = Get-Rect $p
    $ax1 = $r.Left + $x1; $ay1 = $r.Top + $y1
    $ax2 = $r.Left + $x2; $ay2 = $r.Top + $y2
    Focus-Win $p
    [GRUI]::SetCursorPos($ax1, $ay1) | Out-Null
    Start-Sleep -Milliseconds 80
    [GRUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 8; $i++) {
        [GRUI]::SetCursorPos([int]($ax1 + (($ax2 - $ax1) * $i / 8)), [int]($ay1 + (($ay2 - $ay1) * $i / 8))) | Out-Null
        Start-Sleep -Milliseconds 18
    }
    [GRUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
}

function Send-Keys($p, [string]$keys) {
    Focus-Win $p
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 200
}

function Get-Win-Element($p) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

# The flyout is a popup but its MenuItems still show up under the window's UIA subtree,
# which is the only way to drive it: PrintWindow cannot see separate popup windows.
function Invoke-MenuItem($p, [string]$name) {
    $win = Get-Win-Element $p
    if (-not $win) { return $false }

    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)
    $target = $null
    foreach ($el in $all) {
        try {
            if ($el.Current.Name -eq $name -and
                $el.Current.ControlType.ProgrammaticName -eq 'ControlType.MenuItem') {
                $target = $el
                break
            }
        } catch { }
    }
    if (-not $target) { return $false }

    try {
        $pat = $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pat.Invoke()
        return $true
    } catch { return $false }
}

function Count-Ink($p) {
    $r = Get-Rect $p
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [GRUI]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()

    $x0 = [int]($w * 0.24); $x1 = [int]($w * 0.54)
    $y0 = [int]($h * 0.30); $y1 = [int]($h * 0.78)
    $rect = New-Object System.Drawing.Rectangle($x0, $y0, ($x1 - $x0), ($y1 - $y0))
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $count = 0
    try {
        $bytes = New-Object byte[] ($data.Stride * $data.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        for ($i = 0; $i -lt $bytes.Length; $i += 4) {
            if ($bytes[$i] -gt 150 -and $bytes[$i + 1] -gt 150 -and $bytes[$i + 2] -gt 150) { $count++ }
        }
    } finally {
        $bmp.UnlockBits($data); $bmp.Dispose()
    }
    return $count
}

function Save-Shot($p, [string]$path) {
    $r = Get-Rect $p
    $bmp = New-Object System.Drawing.Bitmap(($r.Right - $r.Left), ($r.Bottom - $r.Top))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [GRUI]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# --- run ------------------------------------------------------------------------

$failures = @()
$ok = 0

Write-Host "target pid=$ProcessId, rounds=$Rounds"

for ($round = 1; $round -le $Rounds; $round++) {
    $p = Get-Win
    if (-not $p) {
        $failures += [pscustomobject]@{ Round = $round; State = 'process-gone-before-round'; Ink = 0 }
        Write-Host ("round {0,2}: PROCESS GONE" -f $round)
        break
    }

    # Fresh canvas.
    Send-Keys $p '{ESC}'
    Send-Keys $p '^a'
    Send-Keys $p '{DELETE}'
    Start-Sleep -Milliseconds 120

    Click-Rel $p $ToolRectX $ToolRectY
    Drag-Rel $p 750 600 900 730
    Drag-Rel $p 1000 780 1150 910
    # Round 5 reported the same failure with an ellipse in the mix, so half the rounds use
    # four shapes of two kinds rather than a symmetrical pair of rectangles.
    Click-Rel $p $ToolRectX $ToolEllipseY
    Drag-Rel $p 780 880 900 990
    Drag-Rel $p 1050 620 1170 730

    $ink = Count-Ink $p
    if ($ink -lt 50) {
        $failures += [pscustomobject]@{ Round = $round; State = 'setup-failed-draw'; Ink = $ink }
        Write-Host ("round {0,2}: nothing drawn (ink={1})" -f $round, $ink)
        continue
    }

    Send-Keys $p '^a'

    # Open the 组合 flyout, then invoke 分组.
    Click-Rel $p $GroupBtnX $GroupBtnY
    Start-Sleep -Milliseconds 350

    $invoked = Invoke-MenuItem $p '组合'
    Start-Sleep -Milliseconds 500

    $p = Get-Win
    if (-not $p) {
        $shot = Join-Path $OutDir ("fail-{0:D2}-crash.png" -f $round)
        # Nothing to screenshot once the process is gone; record what we know instead.
        $failures += [pscustomobject]@{ Round = $round; State = 'PROCESS-GONE-AFTER-GROUP'; Ink = $ink }
        Write-Host ("round {0,2}: *** PROCESS GONE after 组合 (ink before={1})" -f $round, $ink)
        break
    }

    $after = Count-Ink $p
    $state = if ($invoked) { 'ok' } else { 'menu-not-found' }
    if ($state -ne 'ok') { $failures += [pscustomobject]@{ Round = $round; State = $state; Ink = $after } }

    Write-Host ("round {0,2}: ink before={1,5} after={2,5} menu={3}" -f $round, $ink, $after, $invoked)
    if ($state -eq 'ok') { $ok++ }
}

Write-Host ""
Write-Host ("RESULT: {0} ok, {1} problem(s) in {2} rounds" -f $ok, $failures.Count, $Rounds)
if ($failures.Count -gt 0) { $failures | Format-Table -AutoSize }
