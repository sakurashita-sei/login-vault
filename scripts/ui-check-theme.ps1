<#
.SYNOPSIS
  起動中の Login Vault を UI Automation で操作し、「表示」メニューによる実行中のテーマ切替と、無操作ロック時の通知表示を撮影する（-SkipIdle で後者を省く）。
.DESCRIPTION
  既存の架空保管庫（scripts\run-dev.ps1 -Theme sf -DataDir .dev-data\sf などで起動したもの）を解錠して使う。実データには触れない。
  流れ: 解錠→「表示」メニューでライト→ダーク→SF と切替→設定で無操作ロックを 1 分にして保存→約 70 秒待ってロック画面の通知を撮影→誤ったパスワードで失敗表示を撮影。
  副作用: 対象の架空データは「無操作ロック 1 分」「テーマ SF（ui-settings.json）」のまま残る。使い捨てのコピー（例: .dev-data\sf）に対して実行すること。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-theme.ps1
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check\sf\theme'),
    [string]$Password = 'dev-master-password-2026',
    [switch]$SkipIdle
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-check.common.ps1')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# スプラッシュ（約 2 秒だけ表示）が見えていれば撮影する。ShowInTaskbar=False のため Process.MainWindowHandle には現れないので、
# 開発用プロセスが見つかり次第タイトル一致で探す。間に合わなければ飛ばす
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
$mainHwnd = Hwnd $main  # Process.MainWindowHandle は最初に取得した値を保持し続けるため、タイトル一致で得た要素から取る
[NativeUi]::SetForegroundWindow($mainHwnd) | Out-Null

try {
    # 解錠
    Set-Text (Find-Child $main 'マスターパスワード' $CT::Edit) $Password
    Invoke-Click (Find-Child $main '解錠(U)' $CT::Button)
    $null = Find-Child $main '＋サービス(N)' $CT::Button 40
    Save-Shot $mainHwnd 'unlocked'

    # 「表示」→「テーマ」で実行中にテーマを切り替える（ライト→ダーク→SF。D-047）
    Expand-Menu (Find-Child $main '表示(W)' $CT::MenuItem)
    Expand-Menu (Find-Child $main 'テーマ(T)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'ライト(L)' $CT::MenuItem)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'theme-light-runtime'
    Expand-Menu (Find-Child $main '表示(W)' $CT::MenuItem)
    Expand-Menu (Find-Child $main 'テーマ(T)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'ダーク(D)' $CT::MenuItem)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'theme-dark-runtime'
    Expand-Menu (Find-Child $main '表示(W)' $CT::MenuItem)
    Expand-Menu (Find-Child $main 'テーマ(T)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'SF(F)' $CT::MenuItem)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'theme-sf-runtime'

    if ($SkipIdle) {
        "idle check: skipped (-SkipIdle)"
    }
    else {
        # 設定: 無操作ロックを 1 分にして保存
        Expand-Menu (Find-Child $main 'ファイル(F)' $CT::MenuItem)
        Invoke-Click (Find-Child $main '設定(S)...' $CT::MenuItem)
        $dlg = Wait-Window $proc.Id '設定' 15
        Set-Text (Find-Child $dlg '無操作でロックするまでの分数' $CT::Edit) '1'
        Invoke-Click (Find-Child $dlg '保存(S)' $CT::Button)
        Start-Sleep -Milliseconds 800
        Save-Shot $mainHwnd 'idle-1min-saved'

        # 無操作のまま待ち、自動ロック後の通知を撮影する
        "waiting: 70 seconds without input"
        Start-Sleep -Seconds 70
        $null = Find-Child $main '解錠(U)' $CT::Button 20
        Save-Shot $mainHwnd 'locked-by-idle-notice'

        # 誤ったパスワードで失敗表示（赤字）を撮影し、通知が消えることを確認する
        Set-Text (Find-Child $main 'マスターパスワード' $CT::Edit) 'wrong-password'
        Invoke-Click (Find-Child $main '解錠(U)' $CT::Button)
        Start-Sleep -Seconds 2
        Save-Shot $mainHwnd 'unlock-wrong-password'
    }
    "flow: completed"
}
catch {
    "flow: FAILED: $($_.Exception.Message)"
    try { Save-Shot $mainHwnd 'failure-main' } catch { }
    foreach ($h in [NativeUi]::TopWindows([uint32]$proc.Id)) { try { Save-Shot $h ('failure-' + ([NativeUi]::Title($h) -replace '[^\w]', '_')) } catch { } }
    exit 1
}
finally {
    Start-Sleep -Milliseconds 500
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(8000)) { Stop-Process -Id $proc.Id -Force; "process: force-stopped" } else { "process: closed gracefully exit=$($proc.ExitCode)" }
    }
}
