# Rukbat

[English](../../README.md)

Rukbatは、Windowsのシステムスクリプトを書くための小さな静的型付き言語です。
`.rukbat`のソースとインポート先を、Windows PowerShell 5.1で単独実行できる1つの`.ps1`ファイルにコンパイルします。
生成したスクリプトの実行先にRukbatをインストールする必要はありません。

<a id="getting-started"></a>

## はじめに

Windows x64では、VS Codeの **Extensions: Install from VSIX** で`rukbat-<version>-win32-x64.vsix`をインストールします。
コンパイラは拡張機能に含まれるため、.NETの別途インストールやPATHの設定は不要です。

信頼済みのフォルダーを開き、次の内容を`hello.rukbat`として保存します。

```rukbat
fn greet(name: string): void {
    print("Hello, " + name)
}

greet("Rukbat")
```

1. コマンドパレットから **Rukbat: Run File** を実行します。引数がなければ`[]`のまま確定します。
2. **Rukbat: Build PowerShell** を実行すると、ソースと同じディレクトリに`hello.ps1`が生成されます。
3. 生成したスクリプトを単独で実行します。

   ```powershell
   powershell -NoProfile -File .\hello.ps1
   ```

拡張機能には、補完、診断、整形、デバッグもあります。
エディタのコマンドは[VS Code拡張機能ガイド](VSCODE.md)を参照してください。

ターミナルから使う場合はCLIのZIPを展開し、その中の`rukbat.exe`を実行します。

```powershell
.\rukbat.exe check .\hello.rukbat
.\rukbat.exe run .\hello.rukbat
.\rukbat.exe build .\hello.rukbat
```

生成したスクリプトにはPowerShellのFullLanguageモードが必要です。
ネイティブヘルパーを使う機能には、Add-Typeの実行許可も必要です。
パッケージ、実行環境の要件、更新方法は[配布ガイド](DISTRIBUTION.md)を参照してください。

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
