# Rukbat for VS Code

[English](../../editors/vscode/README.md) | [日本語目次](README.md)

Windowsで`.rukbat`プログラムを編集、検査、実行、デバッグする拡張機能です。
ハイライト、補完、診断、ホバー、ナビゲーション、名前の変更、引数ヒント、標準形式への整形を提供します。

<a id="install-and-run"></a>

## インストールと実行

1. **Extensions: Install from VSIX** でWindows x64向けVSIXをインストールします。
2. 信頼済みのフォルダーを開き、`.rukbat`ファイルを保存します。
3. **Rukbat: Run File** を実行します。引数をJSON文字列配列で入力し、引数がなければ`[]`のまま確定します。

VSIXには対応するコンパイラと言語サーバーが含まれます。
.NETの別途インストール、PATHの設定、コンパイラのダウンロードは不要です。
`rukbat.server.command`を空にすると同梱版を使います。VSIX更新後はウィンドウを再読み込みしてください。

<a id="commands"></a>

## コマンド

| コマンド | 動作 |
|---|---|
| **Rukbat: Check File** | 構文、インポート、型を検査します。 |
| **Rukbat: Test Project** | アクティブなファイルのワークスペースフォルダーまたはディレクトリ内のテストを実行します。 |
| **Rukbat: Build PowerShell** | ソースと同じディレクトリに単独実行可能な`.ps1`を書き出します。 |
| **Rukbat: Run File** | ビルドしてWindows PowerShellで実行します。 |
| **Rukbat: Debug File** / F5 | ブレークポイント、ステップ実行、変数表示でソースをデバッグします。 |
| **Rukbat: Check Environment** | ローカルのPowerShellとネイティブヘルパーの利用可否を確認します。 |
| **Rukbat: Generate Documentation** | ソースコメントからオフラインHTMLを生成します。 |
| **Rukbat: Restart Language Server** | 設定変更後に言語サーバーを再起動します。 |

エディタのタイトルには **Run File**、**Debug File** の順にアイコンを表示します。
**Check File**、**Build PowerShell**、**Test Project** は **More Actions**（`...`）メニューにあります。
すべてのコマンドはコマンドパレットからも実行できます。

実行・生成コマンドは先にファイルを保存します。
check/build/run/testの出力はタスクターミナル、デバッグの出力はDebug Consoleに表示されます。
相対パスの基準はアクティブなファイルのワークスペースフォルダーです。ワークスペース外ではファイルのディレクトリを使います。
ハイライトはワークスペースを信頼していなくても使えますが、コンパイラのコマンドには信頼設定が必要です。

要件は[配布ガイド](DISTRIBUTION.md)、編集対象の範囲は[エディタ機能](EDITOR.md)、デバッガーの制約は[デバッグ](DEBUGGING.md)、コメントの書式は[ソースコメントからの文書生成](DOCUMENTATION.md)を参照してください。
スニペットには`cli`、`doc`、`module_doc`があります。

<a id="troubleshooting"></a>

## 問題が起きたとき

言語サーバーの機能を使う前に、名前のないファイルを保存してください。
起動に失敗したら **Rukbat** 出力チャンネルとコンパイラ設定を確認し、サーバーを再起動します。
解析は未保存のソースとインポート先も使います。構文エラーがあると一部の解析機能を使えないことがあります。

<a id="extension-development"></a>

## 拡張機能の開発

リポジトリをチェックアウトして開発する場合は`dotnet build Rukbat.sln`を実行し、DLLの絶対パスを設定します。

```json
{
    "rukbat.server.command": "dotnet",
    "rukbat.server.arguments": ["C:/path/to/rukbat/src/Rukbat/bin/Debug/net8.0/rukbat.dll"]
}
```

この設定はLSPとエディタの操作の両方に適用されます。
`editors/vscode`ディレクトリを開いて **Rukbat Extension** を起動します。
同じディレクトリで`npm ci`、`npm test`、`npm run test:editor`を実行します。
エディタテストにはビルド済みコンパイラが必要です。実行ファイルを指定しなければ、VS Codeを`.vscode-test`にダウンロードします。

| テスト設定 | 用途 |
|---|---|
| `RUKBAT_TEST_DLL` | コンパイラDLLの絶対パスを指定します。 |
| `RUKBAT_TEST_VSCODE` | 既存のVS Code実行ファイルを使います。 |
| `RUKBAT_TEST_BUNDLED=1` | 同梱コンパイラをテストします。 |
| `RUKBAT_TEST_EXTENSION` | 展開済みVSIXの`extension`ディレクトリを指定します。 |

リポジトリのルートから`tools/package.ps1`を実行すると、VSIXとCLIのZIPをまとめて作成します。
`npm run package`には、バージョンが一致する生成済みランタイムが必要です。
