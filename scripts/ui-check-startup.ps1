<#
.SYNOPSIS
  起動直後の Login Vault のスプラッシュと最初の画面（ロック画面または初回設定）を撮影して閉じる。
.DESCRIPTION
  保存済みテーマ（ui-settings.json）が起動時に反映されることの確認などに使う。先にこのスクリプトを起動して待機させ、そのあとアプリを起動する。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-startup.ps1 -OutDir artifacts\ui-check\startup
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check\startup')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-check.common.ps1')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# スプラッシュ（約 2 秒だけ表示）が見えていれば撮影する。ShowInTaskbar=False のため Process.MainWindowHandle には現れない
try {
    $early = Wait-DevProcess
    if ($early) {
        $splash = Wait-Window $early.Id 'Login Vault を起動しています' 6
        Save-Shot (Hwnd $splash) 'splash'
    }
}
catch { "splash: not captured ($($_.Exception.Message))" }
$proc = Wait-Process-Window
"process: pid=$($proc.Id) title=[$($proc.MainWindowTitle)]"
Start-Sleep -Seconds 2
$main = Wait-MainWindow $proc.Id 30
$mainHwnd = Hwnd $main
try {
    Save-Shot $mainHwnd 'first-screen'
    "flow: completed"
}
finally {
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(8000)) { Stop-Process -Id $proc.Id -Force; "process: force-stopped" } else { "process: closed gracefully exit=$($proc.ExitCode)" }
    }
}
