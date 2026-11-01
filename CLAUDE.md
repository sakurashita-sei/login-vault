# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要

Login Vault（ログイン・ヴォールト）は個人用・ローカル専用の Windows デスクトップ向けログイン情報管理アプリ。C#／WPF／MVVM（CommunityToolkit.Mvvm）＋軽量レイヤード構成で、暗号化 JSON の単一ファイル（`vault.dat`）に保存する。要件は `docs/login-vault-task.md`、設計案は `docs/login-vault-design.md`（`docs/Login Vault 基本設計書.md` は同内容の異版）、判断の記録は `docs/decisions.md`、検証記録は `docs/verification.md`。

## コマンド（Windows の .NET SDK で実行する）

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify.ps1   # 復元→Release ビルド→書式検証→テスト→win-x64 発行を一括実行
dotnet build LoginVault.sln -c Release
dotnet test LoginVault.sln -c Release
dotnet test tests\LoginVault.Core.Tests -c Release --filter "FullyQualifiedName~VaultCipherTests"   # 単一クラス／--filter "Name=テスト名" で単一テスト
dotnet format LoginVault.sln --verify-no-changes   # 書式検証。修正は --verify-no-changes なしで実行
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\publish.ps1   # 出力先を空にして artifacts\publish\win-x64\LoginVault.exe（自己完結、トリミングなし）を発行し、scripts\signing-thumbprint.txt があれば署名（-NoSign で省略）＋リポジトリ直下に Login Vault.lnk
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\new-signing-cert.ps1   # 自己署名のコード署名証明書を Cert:\CurrentUser\My に作り拇印ファイルを書く（-Trust で信頼ストアにも登録。D-040）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify-signature.ps1   # 発行物の LoginVault*.exe／dll の署名と整合性を検証（verify.ps1 が拇印ファイルのあるときに実行）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\run-dev.ps1   # 架空データ用 .dev-data を --data-dir に渡して起動（-Theme dark／sf で配色を固定）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check.ps1   # 起動中のアプリを UIA で操作し artifacts\ui-check に撮影（空の .dev-data が前提）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-theme.ps1   # 既存の架空保管庫を解錠し、表示メニューのテーマ切替と無操作ロック後の通知を撮影（約 2 分。-SkipIdle で後者を省略）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-generator.ps1   # 既存の架空保管庫を解錠し、アカウント追加画面の「生成...」からパスワード生成ダイアログを操作・撮影（保存はしない）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-capture.ps1   # 既存の架空保管庫を解錠し、「表示」→「画面キャプチャから除外」の効果を撮影で確かめる（使い捨てコピーに対して実行）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check-startup.ps1   # 起動直後のスプラッシュと最初の画面を撮影して閉じる（撮影スクリプトを先に起動してからアプリを起動する）
```

- WSL から実行する場合は `dotnet.exe` / `powershell.exe` を使う。リポジトリのパスに日本語と空白を含む環境では必ず引用符で囲む。
- 導入済み SDK は 8.0.427 と 10.0.403。`global.json` で 10.x（`rollForward: latestFeature`）に固定し、TFM は `Directory.Build.props` の `LoginVaultCoreTargetFramework` / `LoginVaultWindowsTargetFramework`（`net10.0` / `net10.0-windows`）に集約してある（D-001、D-039）。.NET 10 SDK はフレームワーク同梱パッケージの明示参照を NU1510 でエラーにするため、`System.Net.Http` などを PackageReference に書かない。
- PowerShell は 5.1 のみ（`pwsh` なし）。スクリプトは 5.1 互換で書き、`-ExecutionPolicy RemoteSigned` はプロセス単位でのみ使う。実行ポリシー設定は変更しない。
- `TreatWarningsAsErrors` と `EnforceCodeStyleInBuild` が有効。未使用 using（IDE0005）、波かっこ省略、命名規則違反はビルドエラーになる。

## 構成

```text
src/LoginVault.Core/            モデル、VaultValidator、VaultOperations（純粋関数、全体検証後に新しい VaultDocument を返す）、VaultQueries、SearchEngine
src/LoginVault.Infrastructure/  VaultCipher（形式 v1、AES-256-GCM、PBKDF2 60万回）、VaultSerializer、VaultFileStore（一時ファイル→File.Replace→世代）、VaultLifecycle
src/LoginVault.App/             ShellViewModel（NeedsSetup/Locked/Unlocked）、VaultViewModel、SplashViewModel、ClipboardGuard＋Win32ClipboardDevice、LockController、Views（SplashWindow 含む）、Assets（LoginVault.ico／icon-256.png）、App.xaml.cs（唯一のコンポジションルート。StartAsync がスプラッシュ表示中に起動手順を進める）
tests/LoginVault.Core.Tests/    Core と Infrastructure（実ファイル・実暗号を通す。データは bin 配下の testdata）
tests/LoginVault.App.Tests/     ViewModel・クリップボード・ロック・起動引数（FakeTime / FakeClipboardDevice / FakeDialogService）
```

依存方向は App → Infrastructure → Core のみ。Core は WPF と Infrastructure を参照しない。DI コンテナーはなく、`App.xaml.cs` で手組みする。

## 安全上の制約（必ず守る）

- 開発・テストは架空データだけを使う。`%LOCALAPPDATA%\LoginVault` の実保管庫を読まない・起動時に指定しない。起動は `scripts/run-dev.ps1` か `--data-dir` 明示で行う。
- テストの削除・Skip・アサーション弱体化、KDF 反復回数やタグ長などの本番設定のテスト用緩和はしない（`VaultCipherTests.DefaultParameters_MeetMinimums` が守る）。
- 平文の一時ファイル・ログ・検索索引・デバッグ出力を追加しない。モデルの `ToString` は値を出さない上書きを維持する。
- Git の commit / push / リモート作成、SDK インストール、OS 設定変更は利用者の指示なしに行わない。

## 見落としやすい判断

- `VaultLifecycle` はすべての操作を単一ゲートで直列化し、保存はセッショントークン付き。ロック後・再解錠後の古いトークンは `StaleSession` で拒否される。UI 側は `StaleSession` を黙って無視する（D-014）。
- 複数ステップの変更（参照の直接入力への変換＋削除など）は `VaultOperations.ApplyAll` で1回の保存にまとめる。失敗時は元の文書を返す。
- 世代バックアップ名は「UTC 時刻＋プロセス内連番＋乱数」。名前の辞書順が時系列順で、`pre-restore-*` は世代整理の対象外（D-009）。
- WPF プロジェクト（App／App.Tests）では `System.IO` が暗黙 using に含まれない。`Path`・`File` を使うファイルは `using System.IO;` を明示する。
- `PasswordBox` はバインドせず、保存時にコードビハインドで読み取って ViewModel に渡す（D-018）。
- `scripts/*.ps1` は UTF-8 BOM 付き・CRLF で保存する。BOM がないと PowerShell 5.1 が CP932 として読み、日本語を含む行が構文エラーになる（D-020）。スクリプトで書き換えるときは改行コードを保つ（D-044 で LF に崩れたものを戻した）。
- 発行は必ず `scripts/publish.ps1`（または `verify.ps1`）で行う。出力先に `LoginVault.exe` があれば中身を空にしてから `dotnet publish` する。手で `dotnet publish -o` を重ねると前回のランタイム DLL が残り、発行物が起動に失敗する（D-039）。
- アカウント編集画面は基本欄（サービス・ログインID・パスワード。ログインID直下に登録済みメールからの参照トグルスイッチを含む）と開閉式の「詳細設定」（`DetailsToggle`／`DetailsPanel`、`AccountEditorViewModel.IsDetailsExpanded`、既定は閉じる）に分かれる。二段階認証は「未設定」と「設定あり」の2択（D-054）。詳細設定の中の項目が原因の誤りは `Fail(..., inDetails: true)` か `ShowError` で詳細設定を開く。画面は `SizeToContent=Height` で高さ上限 `MaxContentHeight`（640）。UIA で詳細設定の項目を操作するときは先に `Open-Details` で開く（閉じた要素は UIA に出ない）。テストで WPF の束縛を待つときは `UiThread.SettleAsync`（入れ子のメッセージループは使わない。D-045、D-049、D-054）。
- 追加・編集画面のサービス一覧は `ServiceOptions`（全サービス＋末尾の `NewServiceChoice`「＋ 新しいサービスを追加…」）。新しいサービスのとき `AccountInput.ServiceId` は空で、`VaultViewModel.WithTargetService` → `VaultOperations.WithService` が保存時に名前で解決（大文字小文字・前後の空白を無視して既存を再利用）か追加して、アカウント操作と 1 回の保存にまとめる。`AccountInput.ServiceId` を直接使う処理を足すときは、この解決を通すこと（D-048）。
- パスワード生成は Core の `PasswordGenerator`／`PasswordGeneratorOptions`（暗号用乱数、選んだ文字種を必ず含む、`Validate()` が理由を返す）と App の `PasswordGeneratorViewModel`／`PasswordGeneratorWindow`。条件は `VaultPreferences.PasswordGenerator` に保存し、保存はアカウントの保存と同じコミット（`WithGeneratorOptions` → `ApplyAll`）で行う。候補欄は読み取り専用で、クリック時に全選択されコピー（Ctrl+C）可能。「その他」の初期値はともに有効（D-041、D-050、D-051）。記号の一括切替ボタン（すべて解除／すべて選択）を備え、記号オフ時は一覧が非表示（D-052、D-053）。`ApplyAll` は作成 ID を伝えるので、複数手順にしても `CreatedId` を使える（D-041）。
- 署名は `scripts/signing-thumbprint.txt` の拇印（PC・ユーザーごとに異なる。利用者の `Cert:\CurrentUser\My` の自己署名証明書）で `sign-publish.ps1` が行う。対象は `LoginVault*.exe`／`LoginVault*.dll` だけで、Microsoft 署名済みの同梱ファイルには触れない。`verify-signature.ps1` は「拇印一致かつ Valid または UnknownError（ルート未信頼）」を合格とし、`HashMismatch`／`NotSigned` を不合格にする。信頼ストアへの登録（`-Trust`）は利用者の明示操作で、勝手に行わない（D-040）。
- UIA 撮影スクリプトはビルド直後の初回実行でタイミング起因の失敗（項目未検出・入力未達）をすることがある。アプリの不具合と断定する前に再実行する（D-039）。
- UI は Fluent の自前スタイル（`src/LoginVault.App/Themes/`）。配色は `DynamicResource` で参照し、ライト／ダーク／SF の 3 辞書は同じキーを持つ（`ViewSmokeTests` が一致を検証）。SF 固有の見た目は角丸・字体・`FluentHudVisibility` などテーマ単位のキーで切り替え、スタイルの上書き辞書や `StaticResource` での配色参照は作らない（D-029）。「表示」→「テーマ」サブメニュー（`ThemeMenu`。入れ子の見出しはテンプレート `FluentSubmenuHeader`、D-047）のテーマ切替は `ui-settings.json` に保存され、`--theme` を指定した起動ではその値（`system` を含む）がその起動だけ使われる。未指定なら保存値（D-033）。 字体は全テーマ Yu Gothic UI（`FluentFontFamily` ほか）、アプリアイコンは `Assets/`（`docs/アイコン/` と同一）で、画面内では pack URI の `Image` で表示する（D-030）。`ListBoxItem`／`ComboBoxItem` の `ItemContainerStyle` は必ず `BasedOn="{StaticResource FluentNavigationItem}"` などで継承する。アイコンは `FluentGlyphs` のグリフ（Segoe Fluent Icons）。ID／パスワードの表示は `SecretField`、パスワード入力は `PasswordEntryField` を使い、生の `PasswordBox` を画面に直接置かない（D-026）。
- `TextBox`／`PasswordBox` のテンプレートで `PART_ContentHost` に `Padding` を Margin として重ねない（内部で適用されるため二重になり高さが約 44px になる。D-028）。
- 無操作検出（`InputActivityMonitor`）は位置が変わらない MouseMove を操作と数えない。ポインターを置いたままでも自動ロックされる（D-031）。
- UIA スクリプトは `run-dev.ps1` が起動した開発用インスタンス（`src\LoginVault.App\bin` 配下＋`--data-dir`）だけを `Get-DevProcess` で対象にし、日常利用のアプリを掴まない（D-036）。スプラッシュは `ShowInTaskbar=False` のため `Process.MainWindowHandle` に現れず、タイトル一致で探す。メインウィンドウはタイトル「Login Vault v<版>」の前方一致（`Wait-MainWindow`）で得た要素から `Hwnd` 関数で取る（`MainWindowHandle` は最初の値を保持し続ける）。タイトルは `ShellViewModel.WindowTitle`（`AppVersion.Product`）の束縛で、版は `Directory.Build.props` の `Version` だけで決まる（D-043）。
- XAML を変更したら `tests/LoginVault.App.Tests/Views/ViewSmokeTests.cs` を通す。全ビューとダイアログを STA 上で生成・レイアウトし、リソースキーやバインディングパスの誤りを検出する。`KeyBinding` に `RelativeSource` は使えない（コードビハインドで処理）。
- 設定値は暗号化保管庫の内部に保存し、平文の設定ファイルは作らない（D-006）。唯一の例外はテーマ名だけを持つ `ui-settings.json`（`Startup/ThemeSettings`、D-033）で、ここに他の設定や秘密情報を足さない。
- 復元は文書全体の置き換えで、現在のマスターパスワードで暗号化し直す。復元前の保管庫は `backups/pre-restore-*.dat` に退避する（D-010）。
- `vault.dat` がなくても世代・退避コピー・保存途中の一時ファイルがあれば `InitializeAsync` は `RecoverableFilesPresent` で拒否し、新しい保管庫を作らない。`File.Replace` が途中で失敗した場合は `VaultFileStore.Replace` が新内容の昇格→旧版の復帰を試みる（D-036）。
- `PasswordEntryField` の表示中の平文欄はコピー・切り取り・右クリック・Undo を持たず、ウィンドウの非アクティブ化で隠れる。ロック時は `VaultViewModel.BeginLock()` をダイアログを閉じる前に呼び、閉じられたダイアログから戻るループがエディターを再表示しないようにする（D-036）。
- 秘密を扱う `TextBox` には添付プロパティ `controls:ClipboardExport.IsBlocked="True"`（コピー・切り取りの無効化＋ドラッグの取り消し）を付ける。現在の対象は `PasswordEntryField` の平文欄（`InputScope="Password"` も指定）と `AccountEditorWindow` のログインID 欄。メモ・補足欄は対象外（D-037）。
- `ShellViewModel.LockAsync` は各段階を個別に保護し、`_lifecycle.LockAsync()`（鍵の破棄）まで必ず進めてから最初の例外を投げ直す。この順序を崩さない。`ClipboardGuard` のコピーは世代番号で追い越しを検出し、`CopyOutcome.Superseded` は VM で無視する（D-037）。
- 文書の上限は `VaultLimits`（Core/Model）に集約し、`VaultValidator` が強制する（件数超過は個々の検査をしない。所見は 200 件で打ち切り）。上限を変えるときは README と D-037 も更新する。
- 世代バックアップとして扱うのは `VaultPaths.TryParseGenerationStamp` が読める名前だけ。`backups` 内のそれ以外の `vault-*.dat` は数えず・消さない（`HasRecoverableFiles` だけは数える。D-037）。
- WPF のコントロールや `Application` を触るテストは `tests/LoginVault.App.Tests/Support/UiThread.cs` の `UiThread.Invoke`（共通 STA スレッド）と `UiThread.EnsureApplication()` を使う。テストごとに STA スレッドを作ると `Application` のスレッド親和性で失敗する（D-037）。
- `runtimeconfig` の `StartupHookSupport=false` と `EnableUnsafeBinaryFormatterSerialization=false` は `RuntimeConfigTests` が守る。WPF の既定に戻さない（D-037）。
- 画面キャプチャ除外は保管庫内の `VaultPreferences.ExcludeFromScreenCapture`（既定 false）。「表示」メニューの項目（`CaptureExclusionItem`、`IsCheckable=True`。テンプレートは `IsCheckable` の項目にしかチェック印の列を確保しない）は、コードビハインドがクリック後とメニューを開いたときに保管庫の値でチェックを合わせ、`VaultViewModel.ToggleScreenCaptureExclusionAsync` で保存し、`ShellViewModel.IsScreenCaptureExcluded` の変化を `App.xaml.cs` が `ScreenCaptureExclusion.Apply` で反映する（ロック中も同じセッション内は維持）。`ScreenCaptureExclusion` はオンの間だけ UI スレッドに `WH_CALLWNDPROC` フックを入れ、ポップアップやダイアログを含むトップレベルウィンドウを表示直前に除外する（`Loaded` は保険。D-044）。`SettingsViewModel.TryBuild` はダイアログにない設定を元の値から引き継ぐので、設定項目を増やしたら同じ経路を使う。UIA 撮影はオンのあいだ黒くなるため、撮影スクリプトは既定（オフ）の架空データで動かす（D-038、`scripts/ui-check-capture.ps1` が効果を検証）。
