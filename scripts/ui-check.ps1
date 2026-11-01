<#
.SYNOPSIS
  起動中の Login Vault（scripts\run-dev.ps1 で開発起動したもの）を UI Automation で操作し、各画面のスクリーンショットを保存する。
.DESCRIPTION
  初回設定（空の .dev-data が前提）→サービス追加→アカウント追加（メールアカウント）→追加画面で新しいサービス（Amazon）とメール参照アカウントをまとめて登録→表示切替→検索→ロックの順に操作する。
  架空データだけを扱い、実データには触れない。出力は artifacts\ui-check（Git 管理外）。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check.ps1
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check'),
    [string]$Password = 'dev-master-password-2026'
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
"process: pid=$($proc.Id) title=[$($proc.MainWindowTitle)] path=$($proc.Path)"
Start-Sleep -Seconds 2
$main = Wait-MainWindow $proc.Id 30
$mainHwnd = Hwnd $main  # Process.MainWindowHandle は最初に取得した値を保持し続けるため、タイトル一致で得た要素から取る
[NativeUi]::SetForegroundWindow($mainHwnd) | Out-Null

try {
    Save-Shot $mainHwnd 'setup'

    # 初回設定
    Set-Text (Find-Child $main 'マスターパスワード' $CT::Edit) $Password
    Set-Text (Find-Child $main 'マスターパスワード（確認）' $CT::Edit) $Password
    Invoke-Click (Find-Child $main '保管庫を作成(S)' $CT::Button)
    $dlg = Wait-Window $proc.Id '保管庫の作成' 15
    Save-Shot (Hwnd $dlg) 'setup-confirm'
    Invoke-Click (Find-Child $dlg '作成する' $CT::Button)
    $null = Find-Child $main '＋サービス(N)' $CT::Button 40
    Save-Shot $mainHwnd 'vault-empty'

    # サービス追加
    Invoke-Click (Find-Child $main '＋サービス(N)' $CT::Button)
    $dlg = Wait-Window $proc.Id 'サービスの追加' 15
    Set-Text (Find-Child $dlg '入力' $CT::Edit) 'Google'
    Save-Shot (Hwnd $dlg) 'service-prompt'
    Invoke-Click (Find-Child $dlg 'OK' $CT::Button)
    Start-Sleep -Milliseconds 800
    Save-Shot $mainHwnd 'service-added'

    # アカウント追加（メールアカウント）
    Invoke-Click (Find-Child $main '＋追加' $CT::Button)
    $dlg = Wait-Window $proc.Id 'アカウントの追加' 15
    # 既定は詳細設定を閉じた状態（サービス・ID・パスワードだけ）。撮影してから開く（D-045）
    Save-Shot (Hwnd $dlg) 'account-editor-collapsed'
    Open-Details $dlg
    Set-Text (Find-Child $dlg 'アカウント表示名' $CT::Edit) 'メイン'
    Select-Element (Find-Child $dlg 'はい（Gmail などメールサービス自体のログイン情報）' $CT::RadioButton)
    Set-Text (Find-Child $dlg 'ログインID' $CT::Edit) 'main@example.com'
    Set-Text (Find-Child $dlg '新しいパスワード' $CT::Edit) 'dummy-pw-1234'
    Select-Element (Find-Child $dlg '設定あり' $CT::RadioButton)
    $sms = Find-Child $dlg 'SMS' $CT::CheckBox
    $sms.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Set-Text (Find-Child $dlg 'SMS の補足' $CT::Edit) '末尾1234'
    Save-Shot (Hwnd $dlg) 'account-editor'
    Invoke-Click (Find-Child $dlg '保存(S)' $CT::Button)
    Start-Sleep -Milliseconds 1000
    Save-Shot $mainHwnd 'account-added'

    # 2つ目のサービスはアカウントの追加画面でまとめて登録し、メール参照アカウントにする（D-048）
    Invoke-Click (Find-Child $main '＋追加' $CT::Button)
    $dlg = Wait-Window $proc.Id 'アカウントの追加' 15
    $svc = Find-Child $dlg 'サービス名' $CT::ComboBox
    $svc.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $deadline = (Get-Date).AddSeconds(6)
    do {
        Start-Sleep -Milliseconds 300
        $svcItems = @($svc.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem)))
        $newEntry = $svcItems | Where-Object { $_.Current.Name -eq '＋ 新しいサービスを追加…' } | Select-Object -First 1
    } while (-not $newEntry -and (Get-Date) -lt $deadline)
    "service items: " + (($svcItems | ForEach-Object { '[' + $_.Current.Name + ']' }) -join ' ')
    if (-not $newEntry) { throw '「＋ 新しいサービスを追加…」が見つかりません' }
    Select-Element $newEntry
    $svc.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Set-Text (Find-Child $dlg '新しいサービス名' $CT::Edit) 'Amazon'
    Save-Shot (Hwnd $dlg) 'account-editor-new-service'
    Open-Details $dlg
    Set-Text (Find-Child $dlg 'アカウント表示名' $CT::Edit) '個人用'
    Select-Element (Find-Child $dlg '登録済みメールから選択' $CT::RadioButton)
    $combo = Find-Child $dlg '参照するメールアカウント' $CT::ComboBox
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    # 候補の実体化を待つ（ポップアップの初回表示はテンプレート展開に時間がかかるため、固定待ちではなく最長 6 秒まで再確認する）
    $deadline = (Get-Date).AddSeconds(6)
    do {
        Start-Sleep -Milliseconds 300
        $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new($AE::ControlTypeProperty, $CT::ListItem))
    } while ($items.Count -lt 1 -and (Get-Date) -lt $deadline)
    "combo items: " + (($items | ForEach-Object { '[' + $_.Current.Name + ']' }) -join ' ')
    if ($items.Count -lt 1) { throw 'メール候補の項目が見つかりません' }
    Select-Element $items[0]
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Set-Text (Find-Child $dlg '新しいパスワード' $CT::Edit) 'dummy-amazon-pw'
    Set-Text (Find-Child $dlg 'メモ' $CT::Edit) '架空のメモ'
    Save-Shot (Hwnd $dlg) 'account-editor-reference'
    Invoke-Click (Find-Child $dlg '保存(S)' $CT::Button)
    Start-Sleep -Milliseconds 1000
    Save-Shot $mainHwnd 'reference-account'

    # すべて表示、表示切替、検索
    Select-Element (Find-Child $main 'すべて（2件）' $CT::ListItem)
    Start-Sleep -Milliseconds 500
    Invoke-Click (Find-Child $main 'パスワードを表示' $CT::Button)
    Save-Shot $mainHwnd 'all-revealed'
    Set-Text (Find-Child $main '検索' $CT::Edit) 'amazon'
    Start-Sleep -Milliseconds 600
    Save-Shot $mainHwnd 'search'
    Set-Text (Find-Child $main '検索' $CT::Edit) ''
    Start-Sleep -Milliseconds 400

    # ロック
    Invoke-Click (Find-Child $main 'ロック(L)' $CT::Button)
    $null = Find-Child $main '解錠(U)' $CT::Button 15
    Save-Shot $mainHwnd 'locked'
    "flow: completed"
}
catch {
    "flow: FAILED: $($_.Exception.Message)"
    try { Save-Shot $mainHwnd 'failure-main' } catch { }
    exit 1
}
finally {
    Start-Sleep -Milliseconds 500
    if (-not $proc.HasExited) {
        [void]$proc.CloseMainWindow()
        if (-not $proc.WaitForExit(8000)) { Stop-Process -Id $proc.Id -Force; "process: force-stopped" } else { "process: closed gracefully exit=$($proc.ExitCode)" }
    }
}
