# Rukbatプログラムをテストする

[English](../../docs/TESTING.md) | [日本語目次](README.md)

`rukbat test`は通常の関数をテストとして検出し、単独実行可能なPowerShellにコンパイルして、テストごとに別のプロセスで実行します。
テストはアプリケーションと同じ場所か、`tests`ディレクトリに置きます。

<a id="first-test"></a>

## 最初のテスト

`test_math.rukbat`を作成します。

```rukbat
import test
import "./math.rukbat" as arithmetic

fn test_add(): void {
    test.assert(arithmetic.add(2, 3) == 5)
}
```

プロジェクトのディレクトリで実行します。

```powershell
rukbat test
rukbat test tests
rukbat test tests/test_math.rukbat
rukbat test --filter test_add
rukbat test --shell pwsh
rukbat test --timeout 60
```

既定のパスは`.`です。ディレクトリを指定すると`test_*.rukbat`を再帰的に探索します。
ファイル名の接頭辞と関数名は大文字小文字を区別し、拡張子は区別しません。
`.git`、`bin`、`obj`、`.local`、`artifacts`、`TestResults`、シンボリックリンク、ジャンクションは探索しません。
`.rukbat`ファイルを直接指定した場合は名前に関係なく対象にしますが、リンクの直接指定は拒否します。

選んだファイル内の、名前が`test_`で始まるトップレベル関数だけを収集します。
インポートした関数と構造体メソッドはヘルパーとして使えますが、追加のテストにはなりません。
テストの戻り値は`void`で、引数はなし、または`test.Context`1個です。
シグネチャが違うと収集エラー（`RUK5000`）になります。
実行順はファイルパスと関数名の序数順で一定です。
`--filter`は`relative/file.rukbat::test_name`に指定した部分文字列が含まれるテストを選びます。
大文字小文字を区別し、パターンや式としては解釈しません。

実行前に、探索した全ソース、インポート、テストシグネチャを検査します。フィルターで除外したソースも含みます。
選択した全エントリポイントについて、生成PowerShellの構文もテスト開始前に検査します。
収集やコンパイルでエラーがあると、全体の実行を中止します。
`test`は既存の`.ps1`を使わず、上書きもしません。

<a id="assertions-and-failures"></a>

## アサーションと失敗

```rukbat
test.assert(result == expected)
test.assert(names.length == 2)
test.assert(text.contains(message, "ready"))
test.fail("The expected report was not created")
```

`test.assert(condition: bool): void`は、条件がfalseなら通常の`Error`を発生させます。
メッセージにはソースの式を含め、失敗報告用に実行時のソース位置を保持します。
条件全体がプリミティブ値の直接比較（`==`、`!=`、`<`、`<=`、`>`、`>=`）なら、評価した左右の値も表示します。
オペランドは通常どおり左から右へ1回ずつ評価し、論理式の短絡評価も維持します。
文字列はJSONの引用形式、nullは`<null>`で表示します。

`test.fail(message: string): void`は指定メッセージの通常の`Error`を発生させます。
両関数とも位置引数だけを受け取り、通常の`build`/`run`プログラムや単独実行スクリプトでも使えます。

捕捉されないError、0以外の終了、タイムアウト、テストが正常に戻る前のプロセス終了は失敗です。`exit(0)`も失敗になります。
アサーションなどが失敗せず、テストから通常の`return`で戻れば成功です。
1件が失敗しても残りのテストは続けます。成功時の出力は表示しません。
失敗時には取得した標準出力・標準エラー出力とソース位置を表示し、最後に成功・失敗の件数を示します。
診断はリダイレクト時もUTF-8で書き出します。

<a id="files-and-fixtures"></a>

## ファイルとテストデータ

```rukbat
import test
import encoding
import fs
import path

fn test_report(ctx: test.Context): void {
    input := path.join([ctx.source_dir, "fixtures", "input.txt"])
    output := path.join([ctx.temp_dir, "report.txt"])
    fs.write_text(output, fs.read_text(input, encoding.utf8), encoding.utf8)
    test.assert(fs.is_file(output))
}
```

ランナーは、テスト固有の空の作業ディレクトリ`ctx.temp_dir`と、テストソースのディレクトリ`ctx.source_dir`を、どちらも絶対パスで渡します。
相対ファイルパスも`ctx.temp_dir`を基準に解決します。
リポジトリに保存したテストデータを読むときは`ctx.source_dir`を使ってください。
`path.script_dir()`は一時生成スクリプトのディレクトリであり、ソースのディレクトリではありません。

テストごとにプロセスを分けるため、グローバル変数、インポートしたモジュールの状態、環境変数やPowerShellの場所の変更は分離されます。
各プロセスは対象関数を呼ぶ前にモジュールの依存関係を初期化します。
再利用するロジックは、トップレベルでアプリケーションを開始する副作用のないモジュールに置いてください。
コマンドのエントリポイントをインポートすると、その副作用がテストごとに繰り返されます。
共通の準備処理には、各テストから明示的に呼ぶ通常のヘルパー関数を使えます。
後始末には`try`/`catch`を使います。暗黙のフィクスチャやsetup/teardownフックはありません。

成功したテストの一時生成物は、ディレクトリリンクをたどらずに削除します。
失敗したテストは生成スクリプトと作業ディレクトリを残し、`%TEMP%/rukbat-test-...`配下の場所を表示します。
通常の失敗では`stdout.txt`と`stderr.txt`も残します。失敗時の生成物は自動では整理しません。

<a id="multiple-cases-and-expected-errors"></a>

## 複数の入力と想定するエラー

複数の入力には通常の配列とループを使います。
ループ全体が1件のテストなので、最初の失敗で止まります。直接比較の診断には実際の値と期待値が表示されます。

```rukbat
struct Case {
    input: string
    expected: int
}

fn test_parse_int(): void {
    cases: Case[] = [
        Case { input: "0", expected: 0 },
        Case { input: "-12", expected: -12 }
    ]

    for item in cases {
        test.assert(text.parse_int(item.input) == item.expected)
    }
}

fn test_reject_invalid_int(): void {
    try {
        text.parse_int("invalid")
    } catch err {
        return
    }

    test.fail("Expected Error was not thrown")
}
```

この例には`import test`と`import text`が必要です。
Errorを期待するテストでは、catch内でreturnし、`try`/`catch`の後に`test.fail`を置きます。
これでメッセージに依存せず、Errorが発生したことを確認できます。
メッセージも仕様の一部なら、catch内でreturnの前に検査します。
`test.fail`をtry内に置くと、そのアサーション自体をcatchしてしまいます。

<a id="execution-and-automation"></a>

## 実行と自動化

ランナーにはWindows 10 / Server 2016以降が必要です。既定ではPATH上の`powershell.exe`を使います。
PowerShell 7を使うには`--shell pwsh`を指定します。ホストがない場合はエラーにし、黙ってスキップしません。
生成スクリプトはFullLanguageを必要とし、ホストの実行ポリシーに従います。ランナーはポリシーを変更しません。

各テストの既定の制限時間はプロセス生成から30秒です。
PowerShellの起動、モジュール初期化、テスト本体、子孫プロセス、出力取得までを含みます。
`--timeout`には1～86400秒の整数を指定します。
`process.run`と同じネイティブWindows Job Objectヘルパーでホストと通常の子孫プロセスを封じ込めます。
タイムアウトでそのジョブを終了し、後始末の後に次のテストへ進みます。
後始末はプロセスに最大5秒、出力取得に最大5秒かかります。
タイムアウトや出力取得失敗では原因を報告しますが、取得ストリームは利用できません。
標準入力は閉じ、非対話で実行します。出力取得の上限はストリームごとに64 MiBです。

プロセスの分離は、外部への副作用を取り消したり、一時ディレクトリ外へのアクセスを禁止したりするものではありません。
サービス、レジストリ変更、本番ファイル、リモート操作は通常どおり作用します。
こうした結合テストには使い捨ての入力と明示的なテスト設定を使ってください。

| 終了コード | 意味 |
|---|---|
| `0` | 選択した全テストが成功。 |
| `1` | 収集・コンパイルの失敗、または1件以上のテスト失敗。 |
| `2` | 不正な引数、ホストなし、非対応プラットフォーム、起動失敗、I/O・後始末の失敗。 |
| `5` | テストが見つからない、またはフィルターに一致するテストがない。 |

CIでは`rukbat test`を呼び、その終了コードを使えます。
コンパイラの開発には`dotnet test`を使います。こちらはコンパイラとランナーの実装をテストします。

実行可能な[テストの例](../../examples/testing/test_records.rukbat)には、CSVの絞り込み、入れ子のJSON更新、ファイル出力、複数入力、想定するエラーが含まれます。
リポジトリのルートで実行します。

```powershell
.\rukbat test examples/testing
.\rukbat test examples/testing --shell pwsh
```

<a id="compiler-contributor-tests"></a>

## コンパイラ開発者向けのテスト

コンパイラの検査と生成PowerShellの実行は別のテスト群です。
配布対象はWindows PowerShell 5.1で、PowerShell 7でも追加の確認を行います。
VS Codeのテストは[拡張機能ガイド](VSCODE.md#extension-development)を参照してください。

対象を絞るには`dotnet test --filter FullyQualifiedName~<name>`を使います。
名前の例は`Constant`、`Documentation`、`LanguageServerTests`です。
ソースコメントの検査には`rukbat doc entry.rukbat --check`を使い、例は実行しません。

<a id="categories"></a>

### カテゴリ

| カテゴリ | 対象 |
|---|---|
| `Unit` | 字句解析、構文解析、モジュール読込、名前解決、型検査、コード生成、整形、プログラムを実行しないCLIの動作。 |
| `Runtime` | PowerShellホスト上の生成スクリプトの動作。大半はPowerShell 7を使い、`PowerShell51`も付いたテストは主要対象で直接実行。 |
| `PowerShell51` | Windows PowerShell 5.1での直接実行。主要な配布対象の確認を含む。 |
| `CrossVersion` | Windows PowerShell 5.1とPowerShell 7の両方で確認する公開動作。 |

カテゴリは重複します。`PowerShell51`と`CrossVersion`は`Runtime`の部分集合です。
別の属性`Smoke=PowerShell51`では、起動、ライブラリ、プロセス、エラー、CLI、単独実行の代表的な確認を選べます。選択した全ケースを5.1で実行します。

<a id="commands"></a>

### コマンド

`dotnet test`の既定対象は、`Directory.Build.props`のVSTestフィルターにより`Unit`です。
実行テストは明示的に指定します。

```powershell
dotnet test -p:RukbatTestCategory=Unit
dotnet test -p:RukbatTestCategory=Runtime
dotnet test -p:RukbatTestCategory=PowerShell51
dotnet test -p:RukbatTestCategory=CrossVersion
dotnet test --filter "Smoke=PowerShell51"
dotnet test -p:RukbatTestCategory=All
```

`All`は既定のフィルターを外します。テストの並列実行とプロセス共有は無効です。
