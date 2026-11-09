# PlotWorks UI automation helper used by trial-run subagents.
# Coordinates for click/drag are relative to the top-left of the window image
# produced by "shot" (same size as GetWindowRect).
param(
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$X1, [string]$Y1, [string]$X2, [string]$Y2,
    [string]$Out = "$env:TEMP\polycut-shot.png",
    # Target a specific instance. Needed when more than one PlotWorks is running (for
    # example a Release build used for stress testing alongside the user's own window):
    # without it every action would fall through to whichever window was found first.
    [int]$ProcessId = 0,
    # Override the executable, e.g. to drive a Release build for stress testing while the
    # user's own Debug instance keeps running.
    [string]$ExePath = ""
)

$ErrorActionPreference = 'Stop'
$Exe = if ([string]::IsNullOrWhiteSpace($ExePath)) {
    Join-Path $PSScriptRoot '..\PolyCut\bin\Debug\net10.0-windows\PlotWorks.exe'
} else {
    $ExePath
}
$LogFile = "$env:LOCALAPPDATA\PlotWorks\a1.log"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
# Make this process per-monitor DPI aware so GetWindowRect / SetCursorPos use
# physical pixels (the same coordinate space as PrintWindow screenshots).
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class PCDpi {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
}
"@
try { [PCDpi]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null } catch { }
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class PCUI {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
}
"@

function Get-Window {
    if ($script:ProcessId -gt 0) {
        $p = Get-Process -Id $script:ProcessId -ErrorAction SilentlyContinue
        if (-not $p) { throw "PolyCut (PID $($script:ProcessId)) is not running." }
        $p.Refresh()
        if ($p.MainWindowHandle -eq 0) { throw "PolyCut (PID $($script:ProcessId)) has no window yet." }
        return $p
    }
    $p = Get-Process -Name PlotWorks -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $p) { throw 'PolyCut is not running. Use: polycut-ui.ps1 start' }
    $p.Refresh()
    return $p
}

function Get-Rect($p) {
    # DWMWA_EXTENDED_FRAME_BOUNDS = 9: real window bounds without the invisible DWM shadow
    $r = New-Object PCUI+RECT
    try {
        $hr = [PCUI]::DwmGetWindowAttribute($p.MainWindowHandle, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r))
        if ($hr -eq 0 -and $r.Right -gt $r.Left -and $r.Bottom -gt $r.Top) { return $r }
    } catch { }
    [PCUI]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
    return $r
}

function Focus-Window($p) {
    $h = $p.MainWindowHandle
    # Only un-minimise. ShowWindow(SW_RESTORE) on an already-visible window silently
    # un-maximises it, which moved the window between the screenshot and the click and is
    # why automated clicks kept landing 40+ px away from where they were aimed.
    if ([PCUI]::IsIconic($h)) { [PCUI]::ShowWindow($h, 9) | Out-Null }
    $fg = [PCUI]::GetForegroundWindow()
    $fgPid = 0
    $fgThread = [PCUI]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $myThread = [PCUI]::GetCurrentThreadId()
    if ($fgThread -ne $myThread -and $fgThread -ne 0) {
        [PCUI]::AttachThreadInput($myThread, $fgThread, $true) | Out-Null
        [PCUI]::BringWindowToTop($h) | Out-Null
        [PCUI]::SetForegroundWindow($h) | Out-Null
        [PCUI]::AttachThreadInput($myThread, $fgThread, $false) | Out-Null
    } else {
        [PCUI]::BringWindowToTop($h) | Out-Null
        [PCUI]::SetForegroundWindow($h) | Out-Null
    }
    Start-Sleep -Milliseconds 250
}

function Click-Abs([int]$ax, [int]$ay) {
    [PCUI]::SetCursorPos($ax, $ay) | Out-Null
    Start-Sleep -Milliseconds 80
    [PCUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [PCUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}

switch ($Action) {
    'start' {
        $forced = -not [string]::IsNullOrWhiteSpace($ExePath)
        if (-not $forced -and (Get-Process -Name PlotWorks -ErrorAction SilentlyContinue)) { 'already running' }
        else {
            $proc = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -PassThru
            Start-Sleep -Seconds 5
            "started pid=$($proc.Id)"
        }
    }
    'stop' {
        if ($script:ProcessId -gt 0) {
            Stop-Process -Id $script:ProcessId -Force -ErrorAction SilentlyContinue
            "stopped pid=$($script:ProcessId)"
        }
        else {
            Get-Process -Name PlotWorks -ErrorAction SilentlyContinue | Stop-Process -Force
            'stopped'
        }
    }
    'maximize' {
        $p = Get-Window
        [PCUI]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
        Start-Sleep -Milliseconds 500
        'maximized'
    }
    'restore' {
        $p = Get-Window
        [PCUI]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
        Start-Sleep -Milliseconds 500
        'restored'
    }
    'rect' {
        $p = Get-Window; $r = Get-Rect $p
        '{0},{1},{2},{3}' -f $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    }
    'shot' {
        $p = Get-Window; $r = Get-Rect $p
        $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
        $bmp = New-Object System.Drawing.Bitmap($w, $h)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $g.GetHdc()
        [PCUI]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $g.Dispose()
        $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        "saved $Out ($w x $h)"
    }
    'click' {
        $p = Get-Window; $r = Get-Rect $p
        Focus-Window $p
        Click-Abs ($r.Left + [int]$X1) ($r.Top + [int]$Y1)
        "clicked ($X1,$Y1)"
    }
    'rclick' {
        $p = Get-Window; $r = Get-Rect $p
        Focus-Window $p
        [PCUI]::SetCursorPos(($r.Left + [int]$X1), ($r.Top + [int]$Y1)) | Out-Null
        Start-Sleep -Milliseconds 100
        [PCUI]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
        [PCUI]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 400
        "right-clicked ($X1,$Y1)"
    }
    'drag' {
        $p = Get-Window; $r = Get-Rect $p
        Focus-Window $p
        $ax1 = $r.Left + [int]$X1; $ay1 = $r.Top + [int]$Y1
        $ax2 = $r.Left + [int]$X2; $ay2 = $r.Top + [int]$Y2
        [PCUI]::SetCursorPos($ax1, $ay1) | Out-Null
        Start-Sleep -Milliseconds 100
        [PCUI]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        $steps = 12
        for ($i = 1; $i -le $steps; $i++) {
            $mx = [int]($ax1 + (($ax2 - $ax1) * $i / $steps))
            $my = [int]($ay1 + (($ay2 - $ay1) * $i / $steps))
            [PCUI]::SetCursorPos($mx, $my) | Out-Null
            Start-Sleep -Milliseconds 25
        }
        [PCUI]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 200
        "dragged ($X1,$Y1)->($X2,$Y2)"
    }
    'names' {
        $p = Get-Window
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if (-not $win) { throw 'window element not found' }
        $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($el in $all) {
            try {
                $n = $el.Current.Name
                if ($n) {
                    $r = $el.Current.BoundingRectangle
                    if ($r.Width -gt 0 -and $r.Height -gt 0) {
                        '{0} | {1} | {2},{3} {4}x{5}' -f $el.Current.ControlType.ProgrammaticName, $n, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
                    }
                }
            } catch { }
        }
    }
    'invoke' {
        $p = Get-Window
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if (-not $win) { throw 'window element not found' }
        $name = $X1
        $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $candidates = @()
        foreach ($el in $all) {
            try { if ($el.Current.Name -eq $name) { $candidates += $el } } catch { }
        }
        $interactive = $candidates | Where-Object {
            try { $ct = $_.Current.ControlType.ProgrammaticName
                $ct -eq 'ControlType.DataItem' -or $ct -eq 'ControlType.Button' -or $ct -eq 'ControlType.TabItem' -or $ct -eq 'ControlType.MenuItem'
            } catch { $false }
        } | Select-Object -First 1
        $target = if ($interactive) { $interactive } else { $candidates | Select-Object -First 1 }
        if (-not $target) { throw "element not found: $name" }
        $patterns = @(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [System.Windows.Automation.SelectionItemPattern]::Pattern
        )
        foreach ($pat in $patterns) {
            try {
                $patt = $target.GetCurrentPattern($pat)
                if ($pat -eq [System.Windows.Automation.InvokePattern]::Pattern) { $patt.Invoke() }
                else { $patt.Select() }
                Start-Sleep -Milliseconds 500
                "invoked '$name' via $($pat.ProgrammaticName)"
                return
            } catch { }
        }
        $r = $target.Current.BoundingRectangle
        if ($r.Width -gt 0) {
            Focus-Window $p
            Click-Abs ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
            "clicked '$name' at $([int]$r.X),$([int]$r.Y)"
        } else { throw "no usable pattern and no bounds for '$name'" }
    }
    'hover' {
        # Move the cursor without clicking (used to open submenus / tooltips).
        $p = Get-Window; $r = Get-Rect $p
        Focus-Window $p
        [PCUI]::SetCursorPos(($r.Left + [int]$X1), ($r.Top + [int]$Y1)) | Out-Null
        Start-Sleep -Milliseconds 900
        "hovered ($X1,$Y1)"
    }
    'log' {
        if (Test-Path $LogFile) { Get-Content $LogFile -Tail 40 } else { 'no log file yet' }
    }
    'keys' {
        # Send a key chord to the PolyCut window, e.g. "^{a}" (Ctrl+A), "^{v}", "{ENTER}".
        $p = Get-Window
        Focus-Window $p
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait($X1)
        Start-Sleep -Milliseconds 250
        "sent keys: $X1"
    }
    'rawkeys' {
        # Send a chord WITHOUT touching window focus. The normal 'keys' action calls
        # Focus-Window first, which moves focus off the text box being edited and makes
        # Enter/Esc land on the window instead of the editor.
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait($X1)
        Start-Sleep -Milliseconds 300
        "sent raw: $X1"
    }
    'type' {
        # Type literal text into the focused control (SendKeys syntax, so escape +^%~(){}[]).
        $p = Get-Window
        Focus-Window $p
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.SendKeys]::SendWait($X1)
        Start-Sleep -Milliseconds 250
        "typed: $X1"
    }
    'clip' {
        # Copy current clipboard text to a UTF-8 file (default: %TEMP%\polycut-clip.txt).
        if (-not $X1) { $X1 = "$env:TEMP\polycut-clip.txt" }
        $txt = Get-Clipboard -Raw
        [System.IO.File]::WriteAllText($X1, [string]$txt, [System.Text.UTF8Encoding]::new($false))
        "clipboard saved to $X1 ($($txt.Length) chars)"
    }
    'setclip' {
        # Put a file's content (X1) on the clipboard; used to paste text into the app.
        $txt = [System.IO.File]::ReadAllText($X1)
        Set-Clipboard -Value $txt
        "clipboard set from $X1 ($($txt.Length) chars)"
    }
    default { "unknown action: $Action" }
}
