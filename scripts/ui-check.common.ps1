<#
.SYNOPSIS
  UI Automation による画面確認スクリプトの共通部（Win32 ウィンドウ列挙、要素検索、スクリーンショット）。
  scripts\ui-check.ps1 と scripts\ui-check-flows.ps1 からドットソースで読み込む。
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public static class NativeUi {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
  public static System.Collections.Generic.List<IntPtr> TopWindows(uint pid) {
    var list = new System.Collections.Generic.List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { list.Add(h); } return true; }, IntPtr.Zero);
    return list;
  }
  public static string Title(IntPtr h) { var sb = new System.Text.StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
}
"@
[NativeUi]::SetProcessDPIAware() | Out-Null
$script:shot = 0
$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]

function Save-Shot([IntPtr]$hwnd, [string]$label) {
    $script:shot++
    Start-Sleep -Milliseconds 700
    $r = New-Object RECT
    [NativeUi]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = [Math]::Max(1, $r.Right - $r.Left); $h = [Math]::Max(1, $r.Bottom - $r.Top)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [NativeUi]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    if (-not $ok) { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size) }
    $g.Dispose()
    $path = Join-Path $OutDir ("{0:D2}-{1}.png" -f $script:shot, $label)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "shot: $path ($w x $h)"
}
# メインウィンドウ（タイトル「Login Vault v<版>」。スプラッシュの「Login Vault を起動しています」は一致しない）が現れるまで待つ
function Wait-MainWindow([int]$processId, [int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        foreach ($h in [NativeUi]::TopWindows([uint32]$processId)) {
            if ([NativeUi]::Title($h) -match '^Login Vault( v[0-9][0-9A-Za-z.\-]*)?$') { return $AE::FromHandle($h) }
        }
        Start-Sleep -Milliseconds 300
    }
    $titles = ([NativeUi]::TopWindows([uint32]$processId) | ForEach-Object { [NativeUi]::Title($_) }) -join ' | '
    throw "main window not found (visible windows: $titles)"
}
function Wait-Window([int]$processId, [string]$name, [int]$timeoutSec = 20) {
    # Win32 でプロセスの可視トップレベルウィンドウを列挙し、タイトル一致のものを UIA 要素にする
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        foreach ($h in [NativeUi]::TopWindows([uint32]$processId)) {
            if ([NativeUi]::Title($h) -eq $name) { return $AE::FromHandle($h) }
        }
        Start-Sleep -Milliseconds 300
    }
    $titles = ([NativeUi]::TopWindows([uint32]$processId) | ForEach-Object { [NativeUi]::Title($_) }) -join ' | '
    throw "window not found: $name (visible windows: $titles)"
}
function Find-Child($root, [string]$name, $controlType = $null, [int]$timeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $conds = @([System.Windows.Automation.PropertyCondition]::new($AE::NameProperty, $name))
        if ($null -ne $controlType) { $conds += [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $controlType) }
        $cond = if ($conds.Count -gt 1) { [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]]$conds) } else { $conds[0] }
        $e = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($null -ne $e) { return $e }
        Start-Sleep -Milliseconds 250
    }
    throw "element not found: $name"
}
function Set-Text($element, [string]$value) { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value) }
function Invoke-Click($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
function Select-Element($element) { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300 }
function Toggle-Element() { .GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 300 }
function Hwnd($element) { return [IntPtr]$element.Current.NativeWindowHandle }


# 開発用インスタンス（scripts\run-dev.ps1 が起動した、src\LoginVault.App\bin 配下の実行ファイルで --data-dir 付き）だけを対象にする。
# 日常利用のアプリ（発行物、--data-dir なし＝通常の保管庫）は同じプロセス名でも決して掴まない
function Get-DevProcess {
    $candidates = @(Get-CimInstance Win32_Process -Filter "Name = 'LoginVault.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -like '*--data-dir*' -and $_.ExecutablePath -like '*\src\LoginVault.App\bin\*'
    })
    if ($candidates.Count -gt 1) { throw "開発用の LoginVault が複数起動しています（pid: $($candidates.ProcessId -join ', ')）。1 つだけにしてください" }
    if ($candidates.Count -eq 0) { return $null }
    return Get-Process -Id $candidates[0].ProcessId -ErrorAction SilentlyContinue
}

# 開発用インスタンスのプロセスが現れるまで待つ（ウィンドウの有無は問わない。スプラッシュの撮影に使う）
function Wait-DevProcess([int]$timeoutSec = 150) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $p = Get-DevProcess
        if ($p) { return $p }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

# 開発用インスタンスのメインウィンドウが現れるまで待つ
function Wait-Process-Window([int]$timeoutSec = 150) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        $p = Get-DevProcess
        if ($p) {
            $p.Refresh()
            if ($p.MainWindowHandle -ne [IntPtr]::Zero) { return $p }
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw '開発用 LoginVault のウィンドウが見つかりません（起動待ちタイムアウト。scripts\run-dev.ps1 で起動したインスタンスだけが対象）'
}
# アカウント編集画面の「詳細設定」（表示名・メールアカウント・ID の指定方法・二段階認証・メモ。既定は閉じる）を開く（D-045）
function Open-Details($dialog) {
    $toggle = Find-Child $dialog '詳細設定' $CT::Button
    $pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
        $pattern.Toggle()
        Start-Sleep -Milliseconds 500
    }
}
function Expand-Menu($element) { $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400 }

# SendKeys 用に特殊文字をエスケープする
function Escape-SendKeys([string]$text) { return ($text -replace '([+^%~(){}\[\]])', '{$1}') }
# Win32 の共通ダイアログへキーボードで入力できる ASCII のパスを返す（8.3 短い名前。使えなければ TEMP 配下）
function Get-TypablePath([string]$path) {
    $dir = Split-Path -Parent $path
    $name = Split-Path -Leaf $path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $fso = New-Object -ComObject Scripting.FileSystemObject
    $candidate = Join-Path $fso.GetFolder($dir).ShortPath $name
    if ($candidate -match '^[\x20-\x7E]+$') { return $candidate }
    $fallbackDir = Join-Path $env:TEMP 'LoginVaultUiCheck'
    New-Item -ItemType Directory -Force -Path $fallbackDir | Out-Null
    return (Join-Path $fallbackDir $name)
}
