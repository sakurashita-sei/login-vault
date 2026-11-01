# Login Vault（ログイン・ヴォールト）

個人用・ローカル専用の Windows デスクトップ向けログイン情報管理アプリの初期版です。
「サービス」（Google、Amazon、ゲーム名など）の見出しの下に複数の「アカウント」を登録し、
メールアカウントを他のアカウントのログインIDとして参照できます。保存データは
マスターパスワードから導出した鍵で全体を暗号化した単一ファイルに保存します。

設計書は `docs/login-vault-design.md`、実装条件は `docs/login-vault-task.md`、
判断の記録は `docs/decisions.md`、検証結果は `docs/verification.md` を参照してください。

## 必要環境

| 項目 | 内容 |
|---|---|
| OS | Windows 10 / 11（WPF のため Windows 専用） |
| .NET SDK | 10.0.4xx（`global.json` で 10.x に固定。本リポジトリは `net10.0` / `net10.0-windows` で検証。TFM は `Directory.Build.props` の2プロパティに集約。経緯は `docs/decisions.md` D-001、D-039） |
| PowerShell | Windows PowerShell 5.1 以降（スクリプトは 5.1 互換） |
| 追加ライブラリ | CommunityToolkit.Mvvm（MIT）、xunit（Apache-2.0）。NuGet 復元に一度だけネットワークが必要 |

通常実行時に外部通信、テレメトリー、オンライン画像取得は行いません。

## 起動（発行物）

`scripts\publish.ps1`（`scripts\verify.ps1` にも含まれます）は `artifacts\publish\win-x64\LoginVault.exe` を発行したあと、リポジトリ直下に起動用ショートカット `Login Vault.lnk` を作ります。発行フォルダーは階層が深いため、普段はこのショートカットから起動してください。

ショートカットは通常の保管庫の場所（`%LOCALAPPDATA%\LoginVault`）を使い、テーマは「表示」→「テーマ」で最後に選んだもの（未選択なら Windows の設定）で起動します。SF テーマで使いたい場合は「表示」→「テーマ」→「SF」を選べば、次回以降も SF で起動します。exe 本体は発行フォルダー内の DLL 群と一緒に動くため、exe だけを別の場所へコピーしないでください。

日常利用での注意（`docs/security-review.md` 10 章）:

- 発行フォルダーはリポジトリ内（利用者が書き込める場所）にあります。同じユーザー権限で動くプログラムは中の DLL を差し替えられます。長く使う場合は、発行フォルダーごと管理者だけが書き込める場所（`%ProgramFiles%` 配下など）へ移し、ショートカットのリンク先を変えてください。差し替えの有無は「署名」の節の `verify-signature.ps1` で確かめられます。
- 発行物は .NET 10（LTS、2028 年 11 月までサポート）のランタイムを同梱しており（自己完結型）、Windows Update では更新されません。ランタイムのセキュリティ修正を取り込むには、SDK を更新して `scripts\publish.ps1` で発行し直してください（月例が目安。`docs/decisions.md` D-039）。

## 署名（自己署名証明書）

発行物の `LoginVault.exe` と `LoginVault*.dll` には、`scripts\new-signing-cert.ps1` で作る自己署名のコード署名証明書（`CN=saisassekii`）で Authenticode 署名を付けます（`docs/decisions.md` D-040）。目的は「ファイルが差し替え・改ざんされていないことを確かめられる」ことで、SmartScreen の評価や他の PC での信頼は得られません（それには商用の OV／EV 証明書が必要です）。

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\new-signing-cert.ps1          # 証明書を作り、拇印を scripts\signing-thumbprint.txt に書く（1 回だけ）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\publish.ps1                    # 発行後に自動で署名する（拇印ファイルがなければ署名しない。-NoSign で省略）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify-signature.ps1           # 発行物の署名と整合性を検証する（verify.ps1 にも含まれる）
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\new-signing-cert.ps1 -Trust   # 任意: 現在のユーザーの信頼ストアに登録し、プロパティの「デジタル署名」で発行元を確認済みにする（確認ダイアログが出る）
```

- 証明書は現在の Windows ユーザーの個人ストア（`Cert:\CurrentUser\My`）に作られ、秘密鍵はエクスポート不可です。`scripts\signing-thumbprint.txt` は PC（ユーザー）ごとに異なるため Git には含めません（`.gitignore` 済み）。clone した直後は署名なしで発行され、署名したい人は各自 `new-signing-cert.ps1` を実行します。
- 信頼ストアに登録しない場合、署名の状態は「ルート未信頼」ですが、`verify-signature.ps1` は署名者の拇印の一致と改ざんの有無（HashMismatch）を確認できます。登録すると状態が Valid になります。登録は `Import-Certificate` を使う任意の操作で、この証明書の用途はコード署名に限られます。
- 署名には DigiCert のタイムスタンプを付けます（証明書の期限が切れても署名が有効なままになります）。取得できなければ警告してタイムスタンプなしで署名します。
- 限界: 同じユーザー権限で動くプログラムは、この証明書で署名し直すことも、未署名のファイルに差し替えることもできます（Windows は通常の exe の署名を強制しません）。定期的に `verify-signature.ps1` を実行して確かめる運用が前提です。

## 開発起動（架空データ専用）

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\run-dev.ps1
```

リポジトリ内の `.dev-data`（Git 管理外）を `--data-dir` に渡して起動します。通常の保管庫には触れません。
`-DataDir <パス>` で別のディレクトリも指定できます（通常の保管庫 `%LOCALAPPDATA%\LoginVault` は拒否します）。`-Theme dark`（または `light`／`sf`／`system`）で Windows の設定を変えずに配色を固定して確認できます（アプリ本体の引数は `--theme system|light|dark|sf`）。`-Theme` を省くと保存したテーマ（なければ Windows の設定）に従います。パスワードをコマンドラインに渡す引数はありません。

## テーマ

既定では Windows のアプリモード（ライト／ダーク）とアクセント色に追従します。3 つ目のテーマ「SF」（濃紺の面にシアンの線と光、角丸なし）は `--theme sf` で起動するか、解錠後の「表示」→「テーマ」→「SF」を選ぶと使えます。メニューで選んだテーマはデータディレクトリの `ui-settings.json` に保存され、次回以降の起動（スプラッシュ・ロック画面を含む）にも反映されます。このファイルに書かれるのはテーマ名だけで、秘密情報は含みません（暗号化保管庫の外に置く唯一の設定。`docs/decisions.md` D-033）。起動引数 `--theme` を付けた起動では、その値（`system` を含む）がその起動だけ使われ、保存した選択は変わりません。

「表示」メニューの「画面キャプチャから除外」をオンにすると、このアプリのウィンドウは画面共有・録画・スクリーンショットに写らなくなります（Windows の `SetWindowDisplayAffinity`。Windows 10 バージョン 2004 以降。既定はオフ）。対象はメイン画面・ダイアログに加え、メニュー・ドロップダウン・ツールヒント・ファイル選択ダイアログも含み、新しく開く画面は表示される直前に除外されます。オンのときはメニューの項目にチェック印が付きます。この設定は暗号化保管庫の中に保存されて記憶され、次回の起動では解錠した時点から効き、ロック画面にも同じセッション内では効いたままになります（解錠前のロック画面は秘密を表示しないため対象外）。オンのあいだは自分で撮るスクリーンショットも黒くなります（`docs/decisions.md` D-038、D-044）。

タイトルバーには「Login Vault v1.0.0」のように版を表示します（`Directory.Build.props` の `Version`。スプラッシュの表示と同じ値。D-043）。起動時には約 2 秒のスプラッシュ画面（起動引数の確認→多重起動の確認→保管庫の確認→画面の準備の進捗）を表示します。アプリアイコンは `docs/アイコン/` のもので、実行ファイル・タスクバー・タイトルバー・初回設定とロック画面・スプラッシュに使われます。文字はすべて Yu Gothic UI です（`docs/decisions.md` D-030）。

## ビルド・テスト・書式・発行

```powershell
dotnet restore LoginVault.sln
dotnet build LoginVault.sln -c Release --no-restore
dotnet format LoginVault.sln --verify-no-changes --no-restore
dotnet test LoginVault.sln -c Release --no-build --no-restore
powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\publish.ps1
```

- 一括実行: `powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\verify.ps1`（復元→ビルド→書式検証→テスト→発行。失敗があれば非ゼロ終了）
- 単一テストの実行例: `dotnet test tests\LoginVault.Core.Tests -c Release --filter "FullyQualifiedName~VaultCipherTests"`
- 書式の自動修正: `dotnet format LoginVault.sln`
- 画面の自動操作確認: `run-dev.ps1` で起動した状態で `powershell -NoProfile -ExecutionPolicy RemoteSigned -File scripts\ui-check.ps1` を実行すると、UI Automation で初回設定から検索・ロックまでを操作し `artifacts\ui-check` にスクリーンショットを保存します（空の `.dev-data` が前提、架空データのみ）。`scripts\ui-check-flows.ps1` は既存の架空保管庫を解錠して設定・バックアップ作成・復元・マスターパスワード変更ダイアログの流れを確認します（保存ダイアログにはキーボードで `%TEMP%\LoginVaultUiCheck` 配下のパスを入力します）。`scripts\ui-check-theme.ps1` は既存の架空保管庫を解錠し、「表示」メニューでの実行中のテーマ切替（ライト／ダーク／SF）と、無操作ロック（1 分に設定して約 70 秒待機）後の通知表示・解錠失敗時の表示を撮影します（`-SkipIdle` で無操作ロックの部分を省けます）。`scripts\ui-check-startup.ps1` は起動直後のスプラッシュと最初の画面だけを撮影して閉じます（保存したテーマが起動時に反映されるかの確認用）。スプラッシュを撮るには、撮影スクリプトを先に起動して待機させてからアプリを起動してください。これらの撮影スクリプトは `run-dev.ps1` が起動した開発用インスタンス（`src\LoginVault.App\bin` 配下の実行ファイルで `--data-dir` 付き）だけを対象にし、日常利用のアプリには触れません（複数あれば中止します）
- 発行物: `artifacts\publish\win-x64\LoginVault.exe`（自己完結型、トリミング・単一ファイル化なし）

`-ExecutionPolicy RemoteSigned` はそのプロセスだけに効く指定で、Windows の実行ポリシー設定は変更しません。

## プロジェクト構成

```text
src/LoginVault.Core/            モデル、検証、検索、メール参照（WPF・Infrastructure 非依存）
src/LoginVault.Infrastructure/  保管庫ファイル形式、AES-GCM、PBKDF2、原子的保存、世代バックアップ、セッション管理
src/LoginVault.App/             WPF 画面、ViewModel（CommunityToolkit.Mvvm）、Win32 クリップボード、ロック、単一インスタンス
tests/LoginVault.Core.Tests/    Core と Infrastructure の自動テスト
tests/LoginVault.App.Tests/     App 層（ViewModel、クリップボード、ロック、起動引数）の自動テスト
scripts/                        verify.ps1 / publish.ps1 / run-dev.ps1 / ui-check*.ps1（UIA による画面の自動確認）
docs/                           設計書、判断記録、進捗、検証記録、手動テスト、セキュリティ自己レビュー
```

## 保存先とファイル

| 項目 | 場所 |
|---|---|
| 通常の保管庫 | `%LOCALAPPDATA%\LoginVault\vault.dat` |
| 自動バックアップ（直近 N 世代、既定 10） | `%LOCALAPPDATA%\LoginVault\backups\vault-<UTC時刻>-<連番>-<乱数>.dat` |
| 復元前の退避コピー | `%LOCALAPPDATA%\LoginVault\backups\pre-restore-<UTC時刻>-<乱数>.dat` |
| 保存中の一時ファイル | 同じフォルダーの `vault.dat.<GUID>.tmp`（暗号化済み。正常時は残らない） |
| テーマ設定 | `%LOCALAPPDATA%\LoginVault\ui-settings.json`（テーマ名だけの平文 JSON。秘密情報なし） |

保管庫ファイルはサービス名・ログインID・メモ・参照関係を含めて全体が暗号化されています
（AES-256-GCM、PBKDF2-HMAC-SHA256 60万回、形式の詳細は `docs/decisions.md` の「保存形式」）。
設定値（自動ロック時間など）も保管庫の内部に保存され、平文の設定ファイルやログは作りません（例外はテーマ名だけの `ui-settings.json`）。

`backups` フォルダーの中で上の形式の名前に合わないファイル（手でコピーした `vault-backup.dat` など）は世代として数えず、世代整理でも消しません（D-037）。

保管庫には上限があります（サービス 5,000 件、アカウント 20,000 件、1 アカウントの二段階認証の方式 16 件、サービス名・表示名・ログインID・方式名・補足 1,000 文字、パスワード 4,096 文字、メモ 20,000 文字）。個人利用で届く値ではなく、細工されたバックアップで検証や画面が止まらないための防壁です（`VaultLimits`、D-037）。

`vault.dat` が見つからないのに `backups` フォルダーのバックアップや保存途中の一時ファイルが残っている場合、アプリは新しい保管庫を作らず、初回設定画面を開いた時点で復旧手順を表示します。復旧するには、アプリを終了し、`backups` で最も新しい `vault-*.dat`（保存途中のファイルがある場合は `vault.dat.*.tmp`）を `vault.dat` という名前にコピーしてデータディレクトリ直下へ置き、起動し直してください。

## バックアップと復元

- 自動バックアップ: 正常に保存するたびに直前版を `backups` に残し、設定した世代数を超えた古いものを削除します。
- 手動バックアップ: メニュー「ファイル」→「バックアップを作成…」で暗号化ファイルを任意の場所（USB メモリーなど別媒体を推奨）へコピーします。保管庫のデータディレクトリの中には保存できません。コピー後に読み戻して照合し、一致しなければ削除して報告します。保存ダイアログの初期フォルダーは Windows の既定（「ドキュメント」など）で、OneDrive の同期対象になっていることがあります。同期させたくない場合は保存先を選び直してください。
- 復元: 「ファイル」→「バックアップから復元…」でファイルと、そのバックアップ作成時点のマスターパスワードを入力します。
  現在の保管庫は `pre-restore-` で始まる名前で退避したうえで、バックアップの内容で全体を置き換えます（マージはしません）。
  復元後の保管庫は、現在のマスターパスワードで暗号化し直されます。設定（無操作ロック時間など）もバックアップの値になり、変わった項目は完了メッセージに列挙されます。
- バックアップには削除済み・変更前の情報が残ります。不要になった世代や手動コピーは自分で管理してください。
- 移行前に、別の場所へコピーしたバックアップを実際に開けることを確認してください。

## マスターパスワードについての注意

- マスターパスワードを忘れると、保管庫は復元できません。Login Vault はマスターパスワードも復号鍵も保存せず、
  Windows アカウントや DPAPI にも結び付けていないため、復旧手段はありません。
- 空のマスターパスワードは拒否します。12 文字未満や空白のみの値は警告したうえで使用できます。前後の空白も値の一部として扱います。
- 文字列は入力されたとおり（Unicode の正規化なし）に鍵の材料にします。濁点付きの文字などは入力方法によって内部表現が異なることがあるため、マスターパスワードは英数字と記号を中心にすることをおすすめします。
- マスターパスワードを変更すると保管庫は新しいパスワードで暗号化し直されますが、変更前のバックアップは旧パスワードのままです。

## 画面の概要

見た目は Microsoft の Fluent Design に沿った自前のスタイル（`src/LoginVault.App/Themes`）で、Windows の「アプリ モード」（ライト／ダーク）とアクセント色に追従します。ID とパスワードは入力欄風の表示で、右側の目のアイコンで表示切替、コピーのアイコンでコピーします。

- 左: 「すべて」「メールのみ」とサービス一覧（件数付き）、サービスの追加・名前変更・削除。新しいサービスは、アカウントの追加画面で「サービス名」の一覧から「＋ 新しいサービスを追加…」を選べば、アカウントと一緒に 1 回で登録できます（同じ名前のサービスがあればそこに追加。サービスが 1 件もなくても「＋追加」から始められます。D-048）。
- 右: 選択したサービスのアカウント一覧（表示名、ID、固定長の伏せ字、二段階認証、メモ）と追加・編集・削除。
- 上: メニュー（ファイル／サービス／アカウント／表示）、検索（空白区切りで AND、パスワードは検索対象外）、設定、ロック（Ctrl+L）。
- コピー時は Windows のクリップボード履歴・クラウド同期から除外する形式を同時に設定し、一定時間（既定 30 秒）後に
  自分のコピーが残っている場合だけ消去します。パスワードの表示は 1 件ずつで、一定時間（既定 15 秒）、選択変更、
  ウィンドウの非アクティブ化、ロックで隠れます。無操作（既定 5 分）、Windows のロック、スリープで保管庫をロックします。
- アカウントの追加・編集画面は、サービス名・ログインID（直下に「登録済みメールから選択」トグルスイッチ）・パスワードを表示し、表示名・メールアカウントかどうか・二段階認証（「未設定」と「設定あり」の2択、未設定は未確認を含む）・メモは「詳細設定 ∨」を押すと開きます（既定は閉じた状態）。詳細設定に値が入っているアカウントは、閉じたままでも見出しに「設定あり」と出ます。詳細設定の中の入力に誤りがあると、保存時に自動で開きます（D-045、D-049、D-054）。
- アカウントの追加・編集画面のパスワード欄の横にある「生成...」で、条件（8／12／16／自由入力の文字数、英大文字・英小文字・数字・記号の有無と使う記号、似通った英数字の除外、8 文字以内での重複なし）を選んでパスワードを生成し、「この値を使う」でそのまま欄に入れられます。「その他」の2項目（似通った英数字の除外・8文字以内の重複なし）は初期状態で有効です。生成した候補はクリックすると全体が自動選択され、コピー（Ctrl+C）することもできます。記号選択には一括切替ボタン（すべて選択／すべて解除）を備え、「記号」チェックを外すと一覧は非表示になります。最後に使った条件は保管庫の中に保存されます（`docs/decisions.md` D-041、D-050、D-051、D-052、D-053）。
- ID とパスワードの持ち出しはこのコピーボタンだけです。表示中のパスワード欄と編集画面のログインID 欄は、Ctrl+C・右クリック・ドラッグでコピーできません。
  編集画面のメモ欄と二段階認証の補足欄は通常の文字入力欄（コピーは Windows 標準の経路で、履歴・同期の除外はありません）なので、履歴に残したくない値はメモに入れないでください。
- 画面に表示した値は、「表示」→「画面キャプチャから除外」がオフのあいだは画面共有・録画・スクリーンショットに写ります。オンにすると写らなくなりますが、UI Automation（支援技術の API）からは読み取れます。

## 実データを移す前の確認

1. `docs/manual-test.md` の項目を Windows 実機で確認する（クリップボード履歴 Win+V、同期、ロック、スリープ、復元など）。
2. 架空データで登録・編集・削除・検索・コピー・バックアップ・復元を一通り試す。
3. 手動バックアップを別の場所に作り、復元で実際に開けることを確認する。
4. `docs/security-review.md` の「残る制約」を理解したうえで移行する。自己レビューはセキュリティ監査の代わりではありません。

## ライセンス

MIT License（`LICENSE`、Copyright (c) 2026 saisassekii (GitHub: @saisassekii)）。アプリアイコン（`docs/アイコン/`、`src/LoginVault.App/Assets/`）は Claude Design で生成したものです。利用しているライブラリは CommunityToolkit.Mvvm（MIT）、.NET ランタイム（MIT）、テスト用の xunit（Apache-2.0）と coverlet（MIT）です。
このソフトウェアは無保証です。自己レビューと自動テストは第三者によるセキュリティ監査の代わりにはなりません（`docs/security-review.md`）。

## 対象外（初期版）

ブラウザ拡張、自動入力、ゲームへのキー送信、TOTP コード生成、複数 PC 同期、平文エクスポート、メモ帳インポート、Windows Hello / DPAPI 連携。（パスワード生成は D-041 で追加済み）
