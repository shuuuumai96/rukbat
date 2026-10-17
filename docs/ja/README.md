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

配布パッケージはまだリリースしていません。
[パッケージ作成手順](DISTRIBUTION.md#build-the-distribution-maintainers)に沿って、Windows x64向けのVSIXとCLIのZIPをビルドしてください。

VS Codeの **Extensions: Install from VSIX** でVSIXをインストールします。
信頼済みのフォルダーに上の例を`hello.rukbat`として保存し、コマンドパレットから **Rukbat: Run File** を実行します。引数がなければ`[]`のまま確定します。
**Rukbat: Build PowerShell** で、ソースと同じ場所に`hello.ps1`を生成します。
拡張機能にはコンパイラのほか、補完、診断、整形、デバッグが含まれます。

ターミナルから使う場合はCLIのZIPを展開し、その中の`rukbat.exe`を実行します。

```powershell
.\rukbat.exe check .\hello.rukbat
.\rukbat.exe run .\hello.rukbat
.\rukbat.exe build .\hello.rukbat
powershell -NoProfile -File .\hello.ps1
```

パッケージには.NETを同梱しているため、ランタイムの別途インストールは不要です。
生成したスクリプトにはFullLanguageモード、ネイティブヘルパーにはAdd-Typeの実行許可が必要です。
実行環境の要件は[配布ガイド](DISTRIBUTION.md)を参照してください。

<a id="documentation"></a>

## ドキュメント

- [言語仕様](language.md)：構文、型、コマンドライン。
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
