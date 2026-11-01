<#
.SYNOPSIS
  発行フォルダー内の Login Vault 自身の exe／dll に Authenticode 署名を付ける（publish.ps1 から呼ばれる）。
.DESCRIPTION
  証明書は -Thumbprint か scripts\signing-thumbprint.txt（new-signing-cert.ps1 が書く）で指定し、Cert:\CurrentUser\My から探す。
  拇印がなければ署名せずに終了コード 0 で終わる（署名は任意）。対象は LoginVault*.exe／LoginVault*.dll だけで、
  同梱の .NET ランタイムやライブラリ（Microsoft の署名済み）には触れない。タイムスタンプを付け、付けられなければ警告してタイムスタンプなしで署名する。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\sign-publish.ps1
#>
[CmdletBinding()]
param(
    [string]$PublishDir = 'artifacts/publish/win-x64',
    [string]$Thumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not $Thumbprint) {
    $file = Join-Path $PSScriptRoot 'signing-thumbprint.txt'
    if (Test-Path -LiteralPath $file) { $Thumbprint = (Get-Content -LiteralPath $file -Raw).Trim() }
}
if (-not $Thumbprint) {
    Write-Host '    sign: 証明書の拇印がないため署名しません（scripts\new-signing-cert.ps1 で作成できます）' -ForegroundColor Yellow
    exit 0
}
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Thumbprint
if (-not $cert) {
    Write-Host "!!! 拇印 $Thumbprint の証明書が Cert:\CurrentUser\My にありません（scripts\new-signing-cert.ps1 で作り直すか、signing-thumbprint.txt を消してください）" -ForegroundColor Red
    exit 1
}
if (-not $cert.HasPrivateKey) {
    Write-Host "!!! 証明書 $Thumbprint に秘密鍵がないため署名できません" -ForegroundColor Red
    exit 1
}

$targets = @(Get-ChildItem -LiteralPath $PublishDir -File | Where-Object { $_.Name -like 'LoginVault*' -and ($_.Extension -eq '.exe' -or $_.Extension -eq '.dll') } | Sort-Object Name)
if ($targets.Count -eq 0) {
    Write-Host "!!! 署名対象（LoginVault*.exe／.dll）が $PublishDir にありません" -ForegroundColor Red
    exit 1
}
foreach ($target in $targets) {
    $signature = $null
    try {
        $signature = Set-AuthenticodeSignature -FilePath $target.FullName -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    }
    catch {
        Write-Host ("    sign: タイムスタンプ付きの署名に失敗したため、タイムスタンプなしで署名します（{0}）" -f $_.Exception.Message) -ForegroundColor Yellow
        $signature = Set-AuthenticodeSignature -FilePath $target.FullName -Certificate $cert -HashAlgorithm SHA256
    }
    $signer = $signature.SignerCertificate
    if (-not $signer -or $signer.Thumbprint -ne $Thumbprint -or $signature.Status -eq 'HashMismatch' -or $signature.Status -eq 'NotSigned') {
        Write-Host ("!!! 署名に失敗しました: {0} → {1} {2}" -f $target.Name, $signature.Status, $signature.StatusMessage) -ForegroundColor Red
        exit 1
    }
    $timestamp = if ($signature.TimeStamperCertificate) { 'timestamp あり' } else { 'timestamp なし' }
    # 自己署名の証明書を信頼ストアに登録していない場合、Status は UnknownError（ルート未信頼）になる。署名自体は付いている
    $trust = if ($signature.Status -eq 'Valid') { '発行元 信頼済み' } else { '発行元 未信頼（整合性の検証は可能）' }
    Write-Host ("    signed: {0}（{1}、{2}）" -f $target.Name, $trust, $timestamp) -ForegroundColor Green
}
exit 0
