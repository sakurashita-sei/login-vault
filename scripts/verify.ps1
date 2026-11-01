<#
.SYNOPSIS
  復元、Release ビルド、書式検証、自動テスト、win-x64 発行を一括実行する。
.DESCRIPTION
  外部コマンドごとに終了コードを確認し、一つでも失敗すれば直ちに非ゼロで終了する。
  成功の固定表示はせず、各ステップの結果をそのまま出力する。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host ("    dotnet " + ($Arguments -join ' '))
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet @Arguments
    $code = $LASTEXITCODE
    $stopwatch.Stop()
    $seconds = [int]$stopwatch.Elapsed.TotalSeconds
    if ($code -ne 0) {
        Write-Host "!!! $Name が失敗しました（終了コード $code、$seconds 秒）" -ForegroundColor Red
        exit $code
    }
    Write-Host "    OK（終了コード 0、$seconds 秒）" -ForegroundColor Green
}

Write-Host "リポジトリ: $repoRoot"
Write-Host ("dotnet SDK: " + (& dotnet --version))

Invoke-Step -Name 'restore' -Arguments @('restore', 'LoginVault.sln')
Invoke-Step -Name 'build (Release)' -Arguments @('build', 'LoginVault.sln', '-c', 'Release', '--no-restore')
Invoke-Step -Name 'format --verify-no-changes' -Arguments @('format', 'LoginVault.sln', '--verify-no-changes', '--no-restore')
Invoke-Step -Name 'test (Release)' -Arguments @(
    'test', 'LoginVault.sln', '-c', 'Release', '--no-build', '--no-restore',
    '--logger', 'trx;LogFilePrefix=test-results', '--results-directory', 'artifacts/test-results')

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    # 署名の設定があるときは、発行物の署名と整合性を検証する（D-040）
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'signing-thumbprint.txt')) {
        Write-Host ''
        Write-Host '==> verify-signature' -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'verify-signature.ps1')
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

Write-Host ''
Write-Host 'verify.ps1: すべてのステップが終了コード 0 で完了しました。' -ForegroundColor Green
exit 0
