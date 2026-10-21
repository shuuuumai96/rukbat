# コマンドラインプログラム

[English](../../docs/CLI.md) | [日本語目次](README.md)

`import cli`で、位置引数、値を取るオプション、フラグ、ヘルプをまとめて定義します。
パーサーはデータを返し、出力、終了コード、処理の実行はプログラムが制御します。

```rukbat
import cli
import encoding
import fs
import json

command := cli.command("list-files.ps1", "List matching files.")
command.arguments = [cli.argument("directory", "Directory to search")]
pattern := cli.option("pattern", "Filename pattern")
pattern.default_value = "*"
output := cli.option("output", "Output file; omitted means stdout")
output.short_name = "o"
output.value_name = "FILE"
command.options = [pattern, output]

try {
    parsed := cli.parse(command, args())
    if cli.help_requested(parsed) {
        print(cli.help(command))
        exit(0)
    }
    files := fs.list_files(cli.get(parsed, "directory"), cli.get(parsed, "pattern"))
    report := json.stringify(files)
    if cli.has(parsed, "output") {
        fs.write_text(cli.get(parsed, "output"), report, encoding.utf8)
    } else {
        print(report)
    }
} catch err {
    eprint(err.message)
    exit(2)
}
```

`list-files.rukbat`として保存し、実行またはビルドします。

```powershell
rukbat run list-files.rukbat -- C:\Logs --pattern '*.log'
rukbat build list-files.rukbat
.\list-files.ps1 --pattern '*.log' C:\Logs -o report.json
.\list-files.ps1 --help
```

<a id="invocation-and-help"></a>

## 起動方法とヘルプ

`cli.command`には表示用の名前を渡します。
ヘルプはRukbat経由かPowerShell経由かを自動判定しません。
両方を使う場合は、[file-browser](../../examples/file-browser/main.rukbat)のように起動手順を明記します。

```text
Rukbat (from the source directory):
  rukbat run list-files.rukbat -- [options] DIRECTORY

Generated PowerShell (from the generated script's directory):
  .\list-files.ps1 [options] DIRECTORY

VS Code: Rukbat: Run File
  ["C:/Logs", "--pattern", "*.log"]
  Help: ["--help"]
```

CLIの例では`rukbat`がPATH上にあることを前提とします。
VS Codeは同梱コンパイラを使い、引数入力欄にはJSON配列だけを渡します。
ターミナルでは`--`でコンパイラの引数とプログラムの引数を区切ります。

<a id="options-and-flags"></a>

## オプションとフラグ

個々の定義は配列に追加する前に設定してください。
定義を代入・追加すると、そのフィールドと配列がコピーされます。元の変数を後から変えても、コマンドの定義は更新されません。
解析結果も値のスナップショットを保持するため、その後に定義を変更しても既存の結果は変わりません。

値を取らず、指定の有無を調べるフラグは次のように定義します。

```rukbat
recursive := cli.flag("recursive", "Search subdirectories")
recursive.short_name = "r"
command.flags = [recursive]
// After parsing:
if cli.has(parsed, "recursive") {
    print("Recursive search requested")
}
```

複数の値を受け取るにはrepeatableを設定し、get_allを使います。
位置引数では最後の1つだけを繰り返し可能にできます。

```rukbat
tags := cli.option("tag", "Tag to attach")
tags.repeatable = true
command.options.append(tags)
// --tag first --tag second
for tag in cli.get_all(parsed, "tag") {
    print(tag)
}
```

choicesで値を限定し（`format.choices = ["json", "csv"]`）、requiredで必須にし、allow_emptyで明示的な空文字列を許可します。
既定値を指定できるのは、省略可能で繰り返し不可のオプションだけです。
数値への変換には`text.parse_int(cli.get(parsed, "count"))`を使います。
値の範囲、ファイルシステムの状態、プログラム固有の引数の組み合わせは、通常のコードで検査します。
定義名の文字列を重複させたくない場合は`cli.get(parsed, output.name)`を使えます。

<a id="parsing-and-errors"></a>

## 解析とエラー

名前は小文字で定義し、完全一致で照合します。オプションは位置引数の前後に置けます。
繰り返し不可の項目を重複指定するとエラーです。
ハイフンで始まる値は`--name=VALUE`、ハイフンで始まる位置引数は`--`の後に渡します。
`--name=`は空文字列であり、省略ではありません。
空白やシェルの特殊文字を含む値も、呼び出し元のシェルが1引数として渡せば1文字列のまま扱います。
CLIは文字列の評価、再分割、展開を行いません。

オプション終端より前の`--help`/`-h`は、不正な入力や必須引数の不足より優先されます。
`--name=--help`と`-- --help`は値のままです。
結果の値を読む前、ファイル・プロセス・ネットワーク操作の前にヘルプ要求を処理してください。
ヘルプ表示時にそうした操作を実行しないようにするには、インポートするモジュールのトップレベル初期化からも外します。

`cli.parse`と`cli.help`は出力も終了もしません。
一般的なプログラムでは、ヘルプを要求された場合は標準出力に表示し、終了コード0で終了します。
入力の不備による`Error`は`catch`で捕捉し、標準エラー出力に表示して終了コード2で終了します。
運用上の失敗には別の終了方針を選べます。
Resultにはフィールドがなく、JSONやPowerShellのデータとして渡せません。get/get_all/hasを使い、プログラム用の通常の構造体に詰め替えます。
hasでも未定義の名前はエラーになり、タイプミスを「指定なし」として扱いません。
hasには適用済みの既定値も含まれるため、ユーザーが明示的に指定したかどうかは判定できません。

全フィールド、検査条件、引数の文法、エラーの順序は[標準ライブラリ](STDLIB.md#cli)を参照してください。
