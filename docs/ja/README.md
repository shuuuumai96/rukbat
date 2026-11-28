# Rukbat

[English](../../README.md) · [言語仕様](language.md) · [標準ライブラリ](STDLIB.md) · [サンプル](../../examples/)

RukbatはWindowsの運用スクリプトを書くための、小さな静的型付き言語です。
スクリプトを書いた人が、次の変更も担当するとは限りません。
引き継いだ人がソースを上から読んで、どこを直せばよいか分かることを大切にしています。

たとえば、[CSVを絞り込むサンプル](../../examples/csv-filter/main.rukbat)では、
`enabled` が真の行だけを残し、その行の `note` に `checked` と書き込みます。

```rukbat
kept: csv.Row[] = []
record_number := 1

for person in people.rows {
    record_number = record_number + 1

    if parse_bool_cell(person, "enabled", record_number) {
        kept.append(csv.set(person, "note", "checked"))
    }
}
```

`record_number := 1` でレコード番号を宣言し、ループ内の `=` でその値を更新します。
更新するときに左辺を `recrod_number` と書き間違えれば、`rukbat check` で未宣言の変数として見つかります。

配布するのは、`rukbat build` で生成した `.ps1` ファイルです。
Windows PowerShell 5.1で実行できるので、配布先にRukbatをインストールする必要はありません。

<a id="getting-started"></a>

## はじめに

[GitHub Releases](https://github.com/shuuuumai96/rukbat/releases/tag/v0.3.0)からWindows x64向けの **0.3.0 preview** をダウンロードできます。
0.2.0から更新する場合は、[移行案内](releases/0.3.0-migration.md)を参照してください。

| ファイル | 用途 |
|---|---|
| `rukbat-0.3.0-win32-x64.vsix` | コンパイラと言語サーバーを同梱したVS Code拡張機能。 |
| `rukbat-0.3.0-win-x64.zip` | 展開して使えるコマンドライン版。 |
| `SHA256SUMS` | 両パッケージのSHA-256ハッシュ一覧。 |

[パッケージ作成手順](DISTRIBUTION.md#build-the-distribution-maintainers)に沿って、ソースからビルドすることもできます。

VS Codeの **Extensions: Install from VSIX** でVSIXをインストールします。
VS Code 1.82以降が必要です。
信頼済みのフォルダーを開き、次のコードを `hello.rukbat` として保存します。

```rukbat
fn greet(name: string) {
    print("Hello, " + name)
}

greet("Rukbat")
```

コマンドパレットから **Rukbat: Run File** を実行します。引数がなければ`[]`のまま確定します。
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

0.3.0は評価版です。クリーンなWindows環境でのオフライン動作は未検証のため、利用は自己責任でお願いします。
今後の0.x版では、構文、標準ライブラリのAPI、開発ツールの動作が変わる場合があります。
自動処理で使うコンパイラのバージョンは固定し、更新時にはソースの静的検査、再ビルド、テストを行ってください。

配布パッケージの対象はWindows x64です。プログラムの実行にはWindows PowerShell 5.1を使います。PowerShell 7のインストールは任意です。
プロセスの封じ込めにはWindows 10 / Server 2016以降、HTTPには実行先の.NET Framework 4.7.1以降が必要です。
対応範囲と制約は[0.3.0のリリースノート（英語）](../releases/0.3.0.md)にまとめています。

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
