# Rukbat

[English](../../README.md) · [言語仕様](language.md) · [標準ライブラリ](STDLIB.md) · [サンプル](../../examples/)

Rukbatは、Windowsのシステムスクリプトを書くための小さな静的型付き言語です。
Windows PowerShell 5.1で単独実行できるスクリプトにコンパイルします。
実行先にRukbatをインストールする必要はありません。

```rukbat
fn greet(name: string): void {
    print("Hello, " + name)
}

greet("Rukbat")
```

<a id="getting-started"></a>

## はじめに

初回のプレビュー版を **0.1.0** として準備しています。
公開後は[GitHub Releases](https://github.com/shuuuumai96/rukbat/releases)からWindows x64向けのパッケージをダウンロードしてください。

| ファイル | 用途 |
|---|---|
| `rukbat-0.1.0-win32-x64.vsix` | コンパイラと言語サーバーを同梱したVS Code拡張機能。 |
| `rukbat-0.1.0-win-x64.zip` | 展開して使えるコマンドライン版。 |
| `SHA256SUMS` | 両パッケージのSHA-256ハッシュ一覧。 |

公開前に試す場合は、[パッケージ作成手順](DISTRIBUTION.md#build-the-distribution-maintainers)に沿ってソースからビルドできます。

VS Codeの **Extensions: Install from VSIX** でVSIXをインストールします。
VS Code 1.82以降が必要です。
信頼済みのフォルダーに上の例を`hello.rukbat`として保存し、コマンドパレットから **Rukbat: Run File** を実行します。引数がなければ`[]`のまま確定します。
**Rukbat: Build PowerShell** で、ソースと同じ場所に`hello.ps1`を生成します。
拡張機能にはコンパイラのほか、補完、診断、整形、デバッグが含まれます。

ターミナルから使う場合はCLIのZIP全体を展開します。
展開先でターミナルを開き、同梱のサンプルを実行してください。

```powershell
.\rukbat.exe --version
.\rukbat.exe doctor
.\rukbat.exe check .\example\hello.rukbat
.\rukbat.exe run .\example\hello.rukbat
.\rukbat.exe build .\example\hello.rukbat
powershell -NoProfile -File .\example\hello.ps1
```

パッケージには.NETを同梱しているため、ランタイムの別途インストールは不要です。
生成したスクリプトにはFullLanguageモード、ネイティブヘルパーにはAdd-Typeの実行許可が必要です。
実行環境の要件は[配布ガイド](DISTRIBUTION.md)を参照してください。

## プレビュー版の位置づけと互換性

0.1.0は、試用とフィードバックを目的としたプレビュー版です。
今後の0.x版では、構文、標準ライブラリのAPI、開発ツールの動作が変わる場合があります。
自動処理で使うコンパイラのバージョンは固定し、更新時にはソースの静的検査、再ビルド、テストを行ってください。

配布パッケージの対象はWindows x64です。プログラムの実行にはWindows PowerShell 5.1を使います。PowerShell 7のインストールは任意です。
プロセスの封じ込めにはWindows 10 / Server 2016以降、HTTPには実行先の.NET Framework 4.7.1以降が必要です。
対応範囲と制約は[0.1.0のリリースノート（英語）](../releases/0.1.0.md)にまとめています。

不具合は[GitHub Issues](https://github.com/shuuuumai96/rukbat/issues)に報告してください。
`rukbat --version`と`rukbat doctor`の結果、再現する最小限のソース、期待した結果と実際の結果を添えてください。
投稿前に、認証情報や公開できないパスを取り除いてください。

<a id="documentation"></a>

## ドキュメント

- [言語仕様](language.md)：構文、型、コマンドライン。
- [スタイルガイド](STYLE.md)：命名、整形、読みやすいスクリプトの書き方。
- [標準ライブラリ](STDLIB.md)：ファイル、CSV、JSON、プロセスなどのAPI。
- [サンプル（英語）](../../examples/)：[CSVの絞り込み](../../examples/csv-filter/README.md)、[カテゴリ別集計](../../examples/csv-count/README.md)、[JSON設定の更新](../../examples/json-config/README.md)。
- [コマンドラインプログラム](CLI.md)、[テスト](TESTING.md)、[デバッグ](DEBUGGING.md)。
- [ソースコメントからの文書生成](DOCUMENTATION.md)、[エディタ機能](EDITOR.md)。
- [無人実行（英語）](../UNATTENDED.md)、[運用（英語）](../OPERATIONS.md)。
- [設計思想（英語）](../PHILOSOPHY.md)、[コンパイラの構成（英語）](../ARCHITECTURE.md)。

<a id="developing-the-compiler"></a>

## コンパイラを開発する

<details>
<summary>ソースからのビルドとテスト</summary>

Rukbat自体のビルドには.NET 8 SDKが必要です。リポジトリのルートで実行します。

```powershell
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
dotnet test --no-restore
```

実行テストには、Windows PowerShell 5.1（`powershell`）とPowerShell 7（`pwsh`）の両方をPATHに登録してください。
依存関係がキャッシュされていなければ、復元時にネットワーク接続が必要です。

VSIXとポータブルCLIのZIPを作るには、Node.js/npmをインストールして`tools/package.ps1`を実行します。
詳細は[パッケージ作成手順](DISTRIBUTION.md#build-the-distribution-maintainers)を参照してください。

</details>

## ライセンス

Rukbatは[MIT License](../../LICENSE)で公開します。
同梱する依存ライブラリにはそれぞれのライセンスが適用されます。
[第三者ソフトウェアの表記](../THIRD_PARTY_NOTICES.txt)と、各パッケージに含まれるライセンスファイルを参照してください。
