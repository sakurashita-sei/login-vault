<#
.SYNOPSIS
  Windows x64 向けに自己完結型の Release 発行を行い、artifacts/publish/win-x64 に配置する。
.DESCRIPTION
  WPF で未評価のトリミングや単一ファイル化は行わない。発行後、scripts\signing-thumbprint.txt があれば LoginVault の exe／dll に署名し（sign-publish.ps1、-NoSign で省略）、
  リポジトリ直下に起動用ショートカット（Login Vault.lnk）を作る。
  実行例: powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\publish.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDir = 'artifacts/publish/win-x64',
    [switch]$NoSign
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$project = 'src/LoginVault.App/LoginVault.App.csproj'
# 発行先から起動中のアプリ（日常利用の Login Vault）があると上書きできないため、先に確認する
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)).TrimEnd('\')
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'LoginVault.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($publishRoot + '\', [System.StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    Write-Host "!!! 発行先の LoginVault.exe が起動中です（pid: $($running.ProcessId -join ', ')）。アプリを終了してから発行してください" -ForegroundColor Red
    exit 1
}
# 前回の発行物（別バージョンのランタイムの DLL など）が残ると起動に失敗することがあるため、出力先を空にしてから発行する。
# 誤って別のフォルダーを消さないよう、LoginVault.exe がある（＝自分の発行先）ときだけ中身を削除する
if (Test-Path -LiteralPath $publishRoot) {
    if (Test-Path -LiteralPath (Join-Path $publishRoot 'LoginVault.exe')) {
        Get-ChildItem -LiteralPath $publishRoot -Force | Remove-Item -Recurse -Force
        Write-Host "    cleaned: $publishRoot" -ForegroundColor DarkGray
    }
    elseif (@(Get-ChildItem -LiteralPath $publishRoot -Force).Count -gt 0) {
        Write-Host "!!! 出力先 $publishRoot に Login Vault 以外のファイルがあります。空にするか、別の -OutputDir を指定してください" -ForegroundColor Red
        exit 1
    }
}
Write-Host ''
Write-Host "==> publish ($project -> $OutputDir)" -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o $OutputDir
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Host "!!! publish が失敗しました（終了コード $code）" -ForegroundColor Red
    exit $code
}

$exe = Join-Path $OutputDir 'LoginVault.exe'
if (-not (Test-Path $exe)) {
    Write-Host "!!! 発行物 $exe が見つかりません" -ForegroundColor Red
    exit 1
}

$item = Get-Item $exe
$count = (Get-ChildItem -Path $OutputDir -Recurse -File | Measure-Object).Count
Write-Host ("    OK: {0} ({1:N0} bytes, 更新 {2}, ファイル数 {3})" -f $item.FullName, $item.Length, $item.LastWriteTime, $count) -ForegroundColor Green

# 署名（自己署名証明書。拇印ファイルがなければ何もしない。D-040）
if (-not $NoSign) {
    & (Join-Path $PSScriptRoot 'sign-publish.ps1') -PublishDir $OutputDir
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# 発行フォルダーは階層が深いため、リポジトリ直下に起動用ショートカットを 1 つ置く（D-032。テーマは「表示」メニューの選択が保存されるので起動引数は付けない）
$shell = New-Object -ComObject WScript.Shell
$path = Join-Path $repoRoot 'Login Vault.lnk'
$lnk = $shell.CreateShortcut($path)
$lnk.TargetPath = $item.FullName
$lnk.Arguments = ''
$lnk.WorkingDirectory = $item.DirectoryName
$lnk.IconLocation = $item.FullName + ',0'
$lnk.Description = 'Login Vault（artifacts\publish\win-x64 の自己完結型発行物を起動）'
$lnk.Save()
Write-Host ("    shortcut: {0}" -f $path) -ForegroundColor Green
exit 0
