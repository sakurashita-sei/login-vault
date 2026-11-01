<#
.SYNOPSIS
  起動中の Login Vault を UI Automation で操作し、「表示」→「画面キャプチャから除外」の効果を撮影で確かめる。
.DESCRIPTION
  既存の架空保管庫（scripts\run-dev.ps1 -DataDir .dev-data\cap などで起動したもの）を解錠して使う。実データには触れない。
  流れ: 解錠→通常の撮影→メニューで除外をオン→撮影（ウィンドウの内容が写らないこと）→メニューを開いてチェックがオンであることと、
  メニューのポップアップも写らないことを確認→除外をオフ→撮影（元に戻ること）→チェックがオフであることを確認。
  撮影は PrintWindow（メイン画面は失敗時に画面コピー、ポップアップは PrintWindow のみで背後を写さない）で、通常時の画像との一致率と黒の割合で判定する。
  副作用: 対象の架空データに設定変更の世代が 2 つ増える。使い捨てのコピーに対して実行すること。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-capture.ps1
#>
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\ui-check\capture'),
    [string]$Password = 'dev-master-password-2026'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ui-check.common.ps1')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 2 枚の画像を 8px 間隔で比較し、同じ色の割合を返す
function Get-Similarity([string]$pathA, [string]$pathB) {
    $a = [System.Drawing.Bitmap]::FromFile($pathA)
    $b = [System.Drawing.Bitmap]::FromFile($pathB)
    try {
        $w = [Math]::Min($a.Width, $b.Width); $h = [Math]::Min($a.Height, $b.Height)
        $same = 0; $total = 0
        for ($y = 0; $y -lt $h; $y += 8) {
            for ($x = 0; $x -lt $w; $x += 8) {
                $total++
                if ($a.GetPixel($x, $y).ToArgb() -eq $b.GetPixel($x, $y).ToArgb()) { $same++ }
            }
        }
        return [Math]::Round($same / [Math]::Max(1, $total), 3)
    }
    finally { $a.Dispose(); $b.Dispose() }
}
# 直前の Save-Shot が書いたファイルのパス
function Last-ShotPath([string]$label) { return (Join-Path $OutDir ("{0:D2}-{1}.png" -f $script:shot, $label)) }
# メニューを開いて項目を押す。直前の撮影などの直後は展開した項目が UIA に見えないことがあるため、Esc で閉じて数回やり直す
# メニュー項目の要素を返す（開けなければやり直す）。チェック状態は TogglePattern で読む（IsCheckable の項目だけが公開する）
function Open-MenuItem($main, [string]$menuName, [string]$itemName, [int]$attempts = 3) {
    for ($i = 1; $i -le $attempts; $i++) {
        Expand-Menu (Find-Child $main $menuName $CT::MenuItem)
        try { return (Find-Child $main $itemName $CT::MenuItem 5) }
        catch {
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            Start-Sleep -Milliseconds 800
            [NativeUi]::SetForegroundWindow($mainHwnd) | Out-Null
        }
    }
    throw "メニュー項目が見つかりません: $menuName → $itemName"
}
function Get-ToggleState($element) { return $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString() }
# 開いているメニューのポップアップ（メイン画面以外のトップレベルウィンドウ）を PrintWindow だけで撮り、黒の割合を返す（背後の画面は写さない）
function Get-PopupBlackRatio([int]$processId, [string]$label) {
    $popup = [IntPtr]::Zero
    foreach ($h in [NativeUi]::TopWindows([uint32]$processId)) { if ($h -ne $mainHwnd) { $popup = $h; break } }
    if ($popup -eq [IntPtr]::Zero) { throw 'メニューのポップアップが見つかりません' }
    Start-Sleep -Milliseconds 600
    $r = New-Object RECT; [NativeUi]::GetWindowRect($popup, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ([Math]::Max(1, $r.Right - $r.Left)), ([Math]::Max(1, $r.Bottom - $r.Top))
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    $ok = [NativeUi]::PrintWindow($popup, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    if (-not $ok) { $bmp.Dispose(); throw 'メニューのポップアップを PrintWindow で撮れませんでした' }
    $path = Join-Path $OutDir ("popup-{0}.png" -f $label); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    try {
        $black = 0; $total = 0
        for ($y = 0; $y -lt $bmp.Height; $y += 4) { for ($x = 0; $x -lt $bmp.Width; $x += 4) { $total++; $c = $bmp.GetPixel($x, $y); if ($c.R -lt 16 -and $c.G -lt 16 -and $c.B -lt 16) { $black++ } } }
        return [Math]::Round($black / [Math]::Max(1, $total), 3)
    }
    finally { $bmp.Dispose() }
}
function Click-MenuItem($main, [string]$menuName, [string]$itemName, [int]$attempts = 3) {
    for ($i = 1; $i -le $attempts; $i++) {
        Expand-Menu (Find-Child $main $menuName $CT::MenuItem)
        try {
            Invoke-Click (Find-Child $main $itemName $CT::MenuItem 5)
            return
        }
        catch {
            "menu: '$itemName' が見つからないためやり直します ($i/$attempts)"
            [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
            Start-Sleep -Milliseconds 800
            [NativeUi]::SetForegroundWindow($mainHwnd) | Out-Null
        }
    }
    throw "メニュー項目が見つかりません: $menuName → $itemName"
}

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
    Save-Shot $mainHwnd 'unlocked'
    $normal = Last-ShotPath 'unlocked'

    # 「表示」→「画面キャプチャから除外」をオン
    Click-MenuItem $main '表示(W)' '画面キャプチャから除外(C)'
    Start-Sleep -Milliseconds 1500
    Save-Shot $mainHwnd 'capture-excluded'
    $excluded = Last-ShotPath 'capture-excluded'

    # メニューにチェックが付き、メニューのポップアップ自体も写らないこと（D-044）
    $item = Open-MenuItem $main '表示(W)' '画面キャプチャから除外(C)'
    $stateOn = Get-ToggleState $item
    $popupOn = Get-PopupBlackRatio $proc.Id 'while-excluded'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
    "capture: menu check while excluded=$stateOn, menu popup black-ratio=$popupOn"
    if ($stateOn -ne 'On') { throw "除外中なのにメニューのチェックが付いていません（$stateOn）" }
    if ($popupOn -lt 0.9) { throw "除外中なのにメニューのポップアップが写っています（black-ratio=$popupOn）" }

    # もう一度選んでオフに戻す
    Click-MenuItem $main '表示(W)' '画面キャプチャから除外(C)'
    Start-Sleep -Milliseconds 1500
    Save-Shot $mainHwnd 'capture-restored'
    $restored = Last-ShotPath 'capture-restored'

    $item = Open-MenuItem $main '表示(W)' '画面キャプチャから除外(C)'
    $stateOff = Get-ToggleState $item
    $popupOff = Get-PopupBlackRatio $proc.Id 'after-restore'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
    "capture: menu check after restore=$stateOff, menu popup black-ratio=$popupOff"
    if ($stateOff -ne 'Off') { throw "解除後なのにメニューのチェックが付いています（$stateOff）" }
    if ($popupOff -gt 0.5) { throw "解除後もメニューのポップアップが写りません（black-ratio=$popupOff）" }

    $simExcluded = Get-Similarity $normal $excluded
    $simRestored = Get-Similarity $normal $restored
    "capture: excluded-vs-normal similarity=$simExcluded, restored-vs-normal similarity=$simRestored"
    if ($simExcluded -gt 0.5) { throw "除外中の撮影が通常時とほぼ同じです（similarity=$simExcluded）。除外が効いていません" }
    if ($simRestored -lt 0.8) { throw "解除後の撮影が通常時と一致しません（similarity=$simRestored）" }
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
