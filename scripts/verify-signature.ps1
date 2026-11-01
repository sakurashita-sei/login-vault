<#
.SYNOPSIS
  発行フォルダー内の Login Vault 自身の exe／dll が、想定した証明書で署名され改ざんされていないことを確かめる。
.DESCRIPTION
  想定する拇印は -Thumbprint か scripts\signing-thumbprint.txt。各ファイルについて、署名者の拇印が一致し、
  状態が Valid（発行元を信頼済み）か UnknownError（自己署名でルート未信頼。整合性は確認済み）であれば合格。
  HashMismatch（改ざん）、NotSigned（未署名）、別の署名者は不合格として終了コード 1 を返す。
  同梱のランタイム・ライブラリ（.dll／.exe）は署名状態の件数だけ表示する。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify-signature.ps1
#>
[CmdletBinding()]
param(
    [string]$PublishDir = 'artifacts/publish/win-x64',
    [string]$Thumbprint
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $Thumbprint) {
    $file = Join-Path $PSScriptRoot 'signing-thumbprint.txt'
    if (Test-Path -LiteralPath $file) { $Thumbprint = (Get-Content -LiteralPath $file -Raw).Trim() }
}
if (-not $Thumbprint) {
    Write-Host '!!! 想定する証明書の拇印がありません（-Thumbprint か scripts\signing-thumbprint.txt）' -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $PublishDir)) {
    Write-Host "!!! 発行フォルダーがありません: $PublishDir" -ForegroundColor Red
    exit 1
}

$failed = 0
$own = @(Get-ChildItem -LiteralPath $PublishDir -File | Where-Object { $_.Name -like 'LoginVault*' -and ($_.Extension -eq '.exe' -or $_.Extension -eq '.dll') } | Sort-Object Name)
if ($own.Count -eq 0) {
    Write-Host "!!! 検証対象（LoginVault*.exe／.dll）が $PublishDir にありません" -ForegroundColor Red
    exit 1
}
foreach ($file in $own) {
    $signature = Get-AuthenticodeSignature -FilePath $file.FullName
    $signer = $signature.SignerCertificate
    $timestamp = if ($signature.TimeStamperCertificate) { 'timestamp あり' } else { 'timestamp なし' }
    if ($signature.Status -eq 'Valid' -and $signer -and $signer.Thumbprint -eq $Thumbprint) {
        Write-Host ("    OK  {0}（発行元 信頼済み、{1}）" -f $file.Name, $timestamp) -ForegroundColor Green
    }
    elseif ($signature.Status -eq 'UnknownError' -and $signer -and $signer.Thumbprint -eq $Thumbprint) {
        Write-Host ("    OK  {0}（整合性 確認済み、発行元 未信頼、{1}）" -f $file.Name, $timestamp) -ForegroundColor Green
    }
    else {
        $who = if ($signer) { $signer.Thumbprint } else { '（署名なし）' }
        Write-Host ("!!! NG  {0}: 状態 {1}、署名者 {2}。{3}" -f $file.Name, $signature.Status, $who, $signature.StatusMessage) -ForegroundColor Red
        $failed++
    }
}

# 同梱のランタイム・ライブラリは状態の内訳だけ示す（Microsoft の署名が Valid、ネイティブの一部は NotSigned のことがある）
$others = @(Get-ChildItem -LiteralPath $PublishDir -File -Recurse | Where-Object { $_.Name -notlike 'LoginVault*' -and ($_.Extension -eq '.exe' -or $_.Extension -eq '.dll') })
$summary = $others | ForEach-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
Write-Host ("    同梱ファイル（{0} 個）の署名状態: {1}" -f $others.Count, ($summary -join ', '))

if ($failed -gt 0) {
    Write-Host "!!! 署名の検証に失敗しました（$failed 件）。発行物が改ざんされているか、別の証明書で署名されています" -ForegroundColor Red
    exit 1
}
Write-Host ("    署名の検証に合格しました（{0} 件、拇印 {1}）" -f $own.Count, $Thumbprint) -ForegroundColor Green
exit 0
