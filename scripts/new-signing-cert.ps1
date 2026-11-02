<#
.SYNOPSIS
  発行物に署名するための自己署名コード署名証明書を、現在のユーザーの個人ストア（Cert:\CurrentUser\My）に作る。
.DESCRIPTION
  同じ件名で有効期限が 30 日以上残る証明書が既にあれば作り直さず、その拇印を使う。
  拇印は scripts\signing-thumbprint.txt に書き、publish.ps1（署名）と verify-signature.ps1（検証）が参照する。
  秘密鍵はエクスポート不可で作る（鍵ファイルとして持ち出せない。ただし同じユーザー権限で動くプログラムは署名に使える）。
  -Trust を付けると、証明書（公開鍵）を現在のユーザーの「信頼されたルート証明機関」と「信頼された発行元」にも登録する
  （Windows の確認ダイアログが出る。登録すると署名の状態が Valid になり、ファイルのプロパティで発行元が確認済みになる）。
  登録しなくても verify-signature.ps1 による改ざん検知はできる。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\new-signing-cert.ps1
          powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\new-signing-cert.ps1 -Trust
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=sakurashita-sei',
    [int]$Years = 5,
    [switch]$Trust
)
$ErrorActionPreference = 'Stop'
$thumbprintFile = Join-Path $PSScriptRoot 'signing-thumbprint.txt'

$existing = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending)
if ($existing.Count -gt 0) {
    $cert = $existing[0]
    Write-Host ("既存の証明書を使います: {0}（有効期限 {1:yyyy-MM-dd}）" -f $cert.Thumbprint, $cert.NotAfter)
}
else {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears($Years) -CertStoreLocation Cert:\CurrentUser\My
    Write-Host ("証明書を作成しました: {0}（有効期限 {1:yyyy-MM-dd}、件名 {2}）" -f $cert.Thumbprint, $cert.NotAfter, $cert.Subject)
}
Set-Content -LiteralPath $thumbprintFile -Value $cert.Thumbprint -Encoding ASCII
Write-Host "拇印を書き込みました: $thumbprintFile"

if ($Trust) {
    foreach ($store in 'Root', 'TrustedPublisher') {
        $path = "Cert:\CurrentUser\$store"
        if (Get-ChildItem $path | Where-Object Thumbprint -eq $cert.Thumbprint) {
            Write-Host "  ${store}: 登録済み"
            continue
        }
        $temp = Join-Path $env:TEMP ("loginvault-signing-" + $cert.Thumbprint + ".cer")
        Export-Certificate -Cert $cert -FilePath $temp | Out-Null
        try {
            Import-Certificate -FilePath $temp -CertStoreLocation $path | Out-Null
            Write-Host "  ${store}: 登録しました"
        }
        finally {
            Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
        }
    }
}
exit 0
