<#
.SYNOPSIS
  起動中の Login Vault を UI Automation で操作し、アカウント追加画面の「生成...」からパスワード生成ダイアログを開いて撮影する。
.DESCRIPTION
  既存の架空保管庫（scripts\run-dev.ps1 -DataDir .dev-data\gen などで起動したもの）を解錠して使う。実データには触れない。
  流れ: 解錠→「アカウント」→「追加...」→「生成...」→生成ダイアログを撮影→記号なし・自由入力 20 文字に変えて撮影→「この値を使う」→
  追加画面（パスワード欄に値が入った状態）を撮影→キャンセルして閉じる。保管庫には何も保存しない。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-generator.ps1
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check\generator'),
    [string]$Password = 'dev-master-password-2026'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-check.common.ps1')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Toggle-Element($element) { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 400 }
function Get-Value($element) { return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }

$proc = Wait-Process-Window
"process: pid=$($proc.Id) title=[$($proc.MainWindowTitle)]"
Start-Sleep -Seconds 2
$main = Wait-MainWindow $proc.Id 30
$mainHwnd = Hwnd $main
[NativeUi]::SetForegroundWindow($mainHwnd) | Out-Null

try {
    # 解錠
    Set-Text (Find-Child $main 'マスターパスワード' $CT::Edit) $Password
    Invoke-Click (Find-Child $main '解錠(U)' $CT::Button)
    $null = Find-Child $main '＋サービス(N)' $CT::Button 40

    # アカウントの追加画面を開く
    Expand-Menu (Find-Child $main 'アカウント(A)' $CT::MenuItem)
    Invoke-Click (Find-Child $main '追加(A)...' $CT::MenuItem)
    $editor = Wait-Window $proc.Id 'アカウントの追加' 15
    Save-Shot (Hwnd $editor) 'editor-before'

    # 「生成...」で生成ダイアログを開く
    Invoke-Click (Find-Child $editor '生成(G)...' $CT::Button)
    $gen = Wait-Window $proc.Id 'パスワードの生成' 15
    $genHwnd = Hwnd $gen
    $candidate1 = Get-Value (Find-Child $gen '生成したパスワード' $CT::Edit)
    "candidate(default): length=$($candidate1.Length)"
    Save-Shot $genHwnd 'generator-default'

    # 記号なし・自由入力 20 文字
    Toggle-Element (Find-Child $gen '記号' $CT::CheckBox)
    Select-Element (Find-Child $gen '自由入力' $CT::RadioButton)
    Set-Text (Find-Child $gen '文字数（自由入力）' $CT::Edit) '20'
    Start-Sleep -Milliseconds 500
    $candidate2 = Get-Value (Find-Child $gen '生成したパスワード' $CT::Edit)
    "candidate(20, no symbols): length=$($candidate2.Length) symbols=$(($candidate2.ToCharArray() | Where-Object { '-_/*+.,!#$%&()~|@^='.Contains($_) }).Count)"
    Save-Shot $genHwnd 'generator-custom'
    if ($candidate2.Length -ne 20) { throw "自由入力 20 文字の候補が 20 文字ではありません（$($candidate2.Length)）" }

    # 「この値を使う」で編集画面へ戻る（パスワード欄は伏せ字のまま）
    Invoke-Click (Find-Child $gen 'この値を使う(U)' $CT::Button)
    Start-Sleep -Milliseconds 800
    Save-Shot (Hwnd $editor) 'editor-after-generate'

    # 何も保存せずに閉じる
    Invoke-Click (Find-Child $editor 'キャンセル' $CT::Button)
    Start-Sleep -Milliseconds 500
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
        if (-not $proc.WaitForExit(8000)) { Stop-Process -Id $proc.Id -Force; "process: force-stopped" } else { "process: closed gracefully" }
    }
}
