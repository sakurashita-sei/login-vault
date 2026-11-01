<#
.SYNOPSIS
  解錠後の「設定」ダイアログ、バックアップ作成（保存ダイアログ）、復元の流れを UI Automation で操作し、各画面を撮影する。
.DESCRIPTION
  既存の架空保管庫（scripts\run-dev.ps1 で起動、.dev-data）を解錠して使う。実データには触れない。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-flows.ps1
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check\flows'),
    [string]$Password = 'dev-master-password-2026',
    [string]$BackupDir = (Join-Path (Split-Path -Parent $PSScriptRoot) '.dev-data\manual')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-check.common.ps1')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$backupPath = Join-Path (Resolve-Path $BackupDir).Path 'backup-ui.dat'
if (Test-Path $backupPath) { Remove-Item $backupPath -Force }

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
    Save-Shot $mainHwnd 'unlock'
    Set-Text (Find-Child $main 'マスターパスワード' $CT::Edit) $Password
    Invoke-Click (Find-Child $main '解錠(U)' $CT::Button)
    $null = Find-Child $main '＋サービス(N)' $CT::Button 40
    Save-Shot $mainHwnd 'unlocked'

    # 設定ダイアログ: 表示→無操作ロックを 10 分に変更して保存
    Invoke-Click (Find-Child $main '設定(T)' $CT::Button)
    $dlg = Wait-Window $proc.Id '設定' 15
    Save-Shot (Hwnd $dlg) 'settings'
    Set-Text (Find-Child $dlg '無操作でロックするまでの分数' $CT::Edit) '10'
    Invoke-Click (Find-Child $dlg '保存(S)' $CT::Button)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'settings-saved'
    # 設定ダイアログ: 不正値の検証
    Invoke-Click (Find-Child $main '設定(T)' $CT::Button)
    $dlg = Wait-Window $proc.Id '設定' 15
    Set-Text (Find-Child $dlg 'パスワード表示時間の秒数' $CT::Edit) 'abc'
    Invoke-Click (Find-Child $dlg '保存(S)' $CT::Button)
    Start-Sleep -Milliseconds 500
    Save-Shot (Hwnd $dlg) 'settings-invalid'
    Invoke-Click (Find-Child $dlg 'キャンセル' $CT::Button)

    # バックアップ作成: メニュー → 保存ダイアログ → 完了メッセージ
    Expand-Menu (Find-Child $main 'ファイル(F)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'バックアップを作成(B)...' $CT::MenuItem)
    $save = Wait-Window $proc.Id 'バックアップの保存先' 15
    $saveHwnd = Hwnd $save
    Save-Shot $saveHwnd 'backup-savedialog'
    # 共通ダイアログは UIA ツリーを探索せず、既定でフォーカスされているファイル名欄へキーボードで ASCII パスを入力する
    $backupPath = Get-TypablePath $backupPath
    # 前回の残骸があると上書き確認ダイアログが出て止まるため先に消す
    if (Test-Path $backupPath) { Remove-Item $backupPath -Force }
    "backup target: $backupPath"
    [NativeUi]::SetForegroundWindow($saveHwnd) | Out-Null
    Start-Sleep -Milliseconds 400
    # キー入力は前面のウィンドウへ送られるため、保存ダイアログが前面であることを確認してから送る
    if ([NativeUi]::GetForegroundWindow() -ne $saveHwnd) { throw '保存ダイアログを前面にできなかったため、キー入力を中止しました' }
    [System.Windows.Forms.SendKeys]::SendWait((Escape-SendKeys $backupPath) + '{ENTER}')
    $done = Wait-Window $proc.Id 'バックアップを作成しました' 20
    Save-Shot (Hwnd $done) 'backup-done'
    Invoke-Click (Find-Child $done 'OK' $CT::Button)
    "backup file exists: " + (Test-Path $backupPath)

    # 復元前に一時サービスを追加して差分を作る
    Invoke-Click (Find-Child $main '＋サービス(N)' $CT::Button)
    $dlg = Wait-Window $proc.Id 'サービスの追加' 15
    Set-Text (Find-Child $dlg '入力' $CT::Edit) '一時サービス'
    Invoke-Click (Find-Child $dlg 'OK' $CT::Button)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'before-restore'

    # 復元: メニュー → 復元ダイアログ → 確認 → 完了
    Expand-Menu (Find-Child $main 'ファイル(F)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'バックアップから復元(R)...' $CT::MenuItem)
    $dlg = Wait-Window $proc.Id 'バックアップから復元' 15
    Set-Text (Find-Child $dlg 'バックアップファイルのパス' $CT::Edit) $backupPath
    Set-Text (Find-Child $dlg 'バックアップのマスターパスワード' $CT::Edit) $Password
    Save-Shot (Hwnd $dlg) 'restore-dialog'
    Invoke-Click (Find-Child $dlg '次へ(N)' $CT::Button)
    $confirm = Wait-Window $proc.Id 'バックアップからの復元' 15
    Save-Shot (Hwnd $confirm) 'restore-confirm'
    Invoke-Click (Find-Child $confirm '復元する' $CT::Button)
    $done = Wait-Window $proc.Id '復元しました' 30
    Save-Shot (Hwnd $done) 'restore-done'
    Invoke-Click (Find-Child $done 'OK' $CT::Button)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'after-restore'
    "pre-restore copies: " + ((Get-ChildItem -Path (Join-Path (Split-Path -Parent $PSScriptRoot) '.dev-data\backups') -Filter 'pre-restore-*.dat' -ErrorAction SilentlyContinue | Measure-Object).Count)

    # マスターパスワード変更ダイアログの表示だけ確認してキャンセル
    Expand-Menu (Find-Child $main 'ファイル(F)' $CT::MenuItem)
    Invoke-Click (Find-Child $main 'マスターパスワードの変更(M)...' $CT::MenuItem)
    $dlg = Wait-Window $proc.Id 'マスターパスワードの変更' 15
    Save-Shot (Hwnd $dlg) 'change-master'
    Invoke-Click (Find-Child $dlg 'キャンセル' $CT::Button)
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
    # %TEMP%\LoginVaultUiCheck に作った架空データのバックアップは実行後に消す
    $tempDir = Join-Path $env:TEMP 'LoginVaultUiCheck'
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force; "cleanup: removed $tempDir" }
}
