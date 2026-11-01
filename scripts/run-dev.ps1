<#
.SYNOPSIS
  架空データ専用のディレクトリを明示して Login Vault を開発起動する。
.DESCRIPTION
  既定ではリポジトリ内の .dev-data（.gitignore 済み）を --data-dir に渡す。
  通常の保管庫（%LOCALAPPDATA%\LoginVault）には触れない。パスワードはコマンドラインに渡さない。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\run-dev.ps1
  ダークモードで確認: ... -File scripts\run-dev.ps1 -Theme dark
  SF テーマで確認: ... -File scripts\run-dev.ps1 -Theme sf
#>
[CmdletBinding()]
param(
    [string]$DataDir = (Join-Path (Split-Path -Parent $PSScriptRoot) '.dev-data'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('system', 'light', 'dark', 'sf')][string]$Theme = 'system',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
$resolved = (Resolve-Path $DataDir).Path
# 通常の保管庫（%LOCALAPPDATA%\LoginVault）を開発起動で開かない（架空データ専用）
$realVault = (Join-Path $env:LOCALAPPDATA 'LoginVault').TrimEnd('\')
if ($resolved.TrimEnd('\') -ieq $realVault -or $resolved.StartsWith($realVault + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "開発起動では通常の保管庫のディレクトリ（$realVault）を指定できません。架空データ用のディレクトリを使ってください"
}
Write-Host "開発用データディレクトリ: $resolved（架空データだけを登録してください）" -ForegroundColor Cyan

$dotnetArgs = @('run', '--project', 'src/LoginVault.App/LoginVault.App.csproj', '-c', $Configuration)
if ($NoBuild) {
    $dotnetArgs += '--no-build'
}
$dotnetArgs += @('--', '--data-dir', $resolved)
if ($PSBoundParameters.ContainsKey('Theme')) {
    # -Theme を指定したときだけ渡す。未指定なら ui-settings.json に保存したテーマ（なければ Windows の設定）に従う
    $dotnetArgs += @('--theme', $Theme)
}
& dotnet @dotnetArgs
exit $LASTEXITCODE
