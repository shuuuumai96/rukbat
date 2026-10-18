# WindowsでRukbatを使う

[English](../../docs/DISTRIBUTION.md) | [日本語目次](README.md)

初回のプレビュー版を0.1.0として準備しています。
公開後は[GitHub Releases](https://github.com/shuuuumai96/rukbat/releases)からVSIX、CLIのZIP、`SHA256SUMS`をダウンロードしてください。
互換性と制約は[リリースノート（英語）](../releases/0.1.0.md)を参照してください。

<a id="vs-code-install-one-extension"></a>

## VS Codeに拡張機能をインストールする

**Extensions: Install from VSIX** で`rukbat-<version>-win32-x64.vsix`をインストールします。
Windowsの信頼済みワークスペースで、保存済みの`.rukbat`ファイルを開いてください。
診断、補完、整形には同梱のコンパイラを使います。
.NET SDKやランタイムの別途インストール、PATHの変更、コンパイラの設定、追加ダウンロードは不要です。
パッケージはWindows x64向けです。同梱の実行ファイルにも、環境のアプリケーション制御ポリシーが適用されます。

保存済みソースの実行には **Rukbat: Run File**、同じディレクトリへの`.ps1`生成には **Rukbat: Build PowerShell** を使います。
実行引数は`["config.json", "report.json"]`のようなJSON文字列配列です。
コマンドは先にファイルを保存し、保存をキャンセルすると処理も中止します。
作業ディレクトリは、アクティブなファイルが属するワークスペースフォルダーです。ワークスペース外ではファイルのディレクトリを使います。
出力と終了コードはタスクターミナル、コンパイラの診断はProblemsに表示されます。ファイルを開くだけでは実行しません。

同梱コンパイラを使う場合、`rukbat.server.command`は空のままにします。
パッケージの対象外のホストでは外部コンパイラが必要ですが、プログラムの実行はWindowsで行います。
全コマンドと開発時の設定は[拡張機能ガイド](VSCODE.md)を参照してください。

<a id="cli-extract-one-zip"></a>

## CLIのZIPを展開する

`rukbat-<version>-win-x64.zip`を書き込み可能な場所に展開します。
実行ファイルは隣接するランタイムとライブラリを使うため、ディレクトリ全体を一緒に配置してください。
そのディレクトリでターミナルを開きます。

```powershell
.\rukbat.exe --version
.\rukbat.exe doctor
.\rukbat.exe check .\example\hello.rukbat
.\rukbat.exe test .\example
.\rukbat.exe build .\example\hello.rukbat
.\rukbat.exe run .\example\hello.rukbat
```

パッケージの利用に管理者権限、.NETのインストール、ネットワーク接続は不要です。
PATHへの登録は任意です。自動処理では実行ファイルの絶対パスを指定できます。
生成したスクリプトはWindows PowerShell 5.1で別途実行し、コンパイラ、拡張機能、同梱ランタイムを必要としません。
ネイティブAPIにはFullLanguageとAdd-Typeの許可が必要です。
HTTPには実行先の.NET Framework 4.7.1以降、プロセスの封じ込めにはWindows 10 / Server 2016以降が必要です。

`--version`はコンパイラのバージョンを表示し、0を返します。
`doctor`はコンパイラの場所、OSとアーキテクチャ、Windows PowerShellのバージョンとパス、言語モード、ネイティブヘルパーとHTTPの利用可否を表示します。
すべての確認に成功すると0、機能がない、許可されていない、確認に失敗した場合は2を返します。
確認処理の制限時間は15秒です。プロファイルを読み込まず、ポリシーも変更しません。
スクリプトの実行ポリシー、ジョブ固有の権限、外部サービス、スケジューラー設定までは保証しません。

<a id="offline-use-and-controlled-updates"></a>

## オフライン利用と更新管理

VSIX、ZIP、`SHA256SUMS`を組織で認められた経路で転送します。
インストール前に、信頼できる`SHA256SUMS`とハッシュを照合してください。
ハッシュ一覧はファイルの変更を検出できますが、それだけで発行元を証明するものではありません。

アーカイブ名にはバージョンが含まれます。
承認済みZIPはバージョン別のディレクトリに保管し、自動ビルドではその実行ファイルを固定して使います。
ロールバック時は以前のディレクトリを指定するか、以前のVSIXをインストールします。

<a id="build-the-distribution-maintainers"></a>

## 配布パッケージを作る（開発者向け）

開発には.NET 8 SDKとNode.js/npmが必要です。リポジトリのルートで実行します。

```powershell
.\tools\package.ps1
# Optional: choose a new destination directory.
.\tools\package.ps1 -Destination .\artifacts\approved-build
```

このスクリプトはトリミングなしの自己完結型win-x64コンパイラを発行し、拡張機能とのバージョン一致を確認します。
そのコンパイラをWindows向けVSIXに同梱し、CLIのZIPとSHA256SUMSを作成します。
依存関係のキャッシュが揃っていなければ、復元時にネットワーク接続が必要です。
既存の出力先ディレクトリは使えません。失敗時には不完全な出力が残ることがあり、成功した実行だけが完全な配布物を作成します。
拡張機能のランタイムディレクトリは生成物で、Gitの管理対象外です。

両パッケージに、RukbatのMIT Licenseと第三者ソフトウェアの表記を同梱します。
拡張機能のルートに置く`LICENSE`は、パッケージ作成スクリプトがリポジトリからコピーします。
Markdownパーサーも同梱しています。
ソースコメントからの文書生成に追加インストールは不要です。

### GitHubのリリースを準備する

初回のタグは`v0.1.0`、タイトルは`Rukbat 0.1.0 preview`とします。
GitHubでリリースの下書きを作り、プレリリースに設定してください。
本文には`docs/releases/0.1.0.md`を使います。
添付するファイルは、1回の成功したビルドで作ったバージョン付きのCLIのZIP、VSIX、`SHA256SUMS`です。作業用の`portable`ディレクトリは添付しません。

検証したソースのリビジョンを記録し、下記の確認を終えてから下書きを公開します。
公開時には、英語と日本語のREADMEおよび配布ガイドの「準備中」の案内を、公開したリリースへのリンクに更新してください。
ローカルでパッケージを作っても、GitHubのリリースやVS Code Marketplaceには公開されません。

<a id="release-acceptance"></a>

## リリース前の確認

リリースを承認する前に、システムにインストールされた.NETに依存せず、ZIPとVSIXからcheck/build/run/test/fmt/doc/LSPが動作することを確認します。
最終確認には、SDKもランタイムも別途インストールされておらず、ネットワークにも接続していないクリーンなWindows環境を使います。
生成した`.ps1`はコンパイラのない別の実行先でも確認し、以前の承認済みパッケージを保管してください。

ZIPを新しいディレクトリに展開して`node tools/test-distribution.js <extracted-directory>`を実行すると、同梱ランタイムの設定を確認します。
子プロセスのPATHから.NETを除き、DOTNET_ROOTを存在しないディレクトリに向けたうえで、全CLIコマンドとLSPを実行します。
また、ソース削除後の生成スクリプトを、インストール済みの両PowerShellホストで実行します。
この条件付きの試験だけでは、クリーンなPCでの確認を代替できません。
エディタの確認では、[拡張機能ガイド](VSCODE.md#extension-development)に従って展開済みVSIXを使えば、既存のユーザー拡張機能に上書きせずに試せます。
