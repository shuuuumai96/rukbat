# Rukbat標準ライブラリ

[English](../../docs/STDLIB.md) | [日本語目次](README.md)

標準ライブラリはコンパイラのバージョンに含まれます。
標準モジュールは正式名でインポートし、別名は付けられません。

```rukbat
import fs
import path
import csv
import json
import env
import process
import text
import test
import encoding
import math
import datetime
import array
import zip
import hash
import http
import cli
import log
```

ローカルモジュールには明示的な相対`.rukbat`パスと別名を使います。標準モジュール名は別名として予約されています。
生成スクリプトには、インポートで使う標準ライブラリのランタイム部分だけを含め、Windows PowerShell 5.1で単独実行できます。
以下に記載する失敗はすべて通常のRukbat実行時`Error`であり、`try` / `catch`で処理できます。

<a id="choosing-filesystem-and-path-operations"></a>

## ファイルシステム操作とパス操作の選択

実際のファイルやディレクトリを調べたり変更したりする場合は`fs`を使います。
ファイルシステムの実体を調べず、パス値の組み立て、分解、比較をする場合は`path`を使います。どちらも文字列を扱います。
`path.filename`や`path.parent`の対象が実在する必要はありません。
`path.script_dir`は起動時に保存した配布スクリプトのディレクトリを返します。
`path.equal`と`path.find_duplicate`は保存した作業ディレクトリを基準にパス表記を解決しますが、ファイルシステム上の実体が同一かは比較しません。

| 目的 | 操作 | 主な制約 |
|---|---|---|
| テキストの読込・置換 | `fs.read_text`, `fs.write_text` | 文字コードは必須。書き込みはファイルを置換し、追記や親ディレクトリの作成はしない。 |
| テキストの追記 | `fs.append_text` | 文字コードは必須。BOMや改行を追加しない。ファイルがなければ作成するが、親は必要。 |
| 一致するファイルの列挙 | `fs.list_files`, `fs.list_files_recursive` | ファイルだけを返す。`_recursive`は子ディレクトリも対象。 |
| 直下のディレクトリの列挙 | `fs.list_dirs` | 空・Hiddenのディレクトリも含む。子の再解析ポイントを除き、再帰しない。 |
| 対象の種類の確認 | `fs.is_file`, `fs.is_dir` | 存在しない、またはアクセスできない場合はfalse。後続操作の成功は保証しない。 |
| ファイル・ディレクトリのメタデータ取得 | `fs.stat` | 種類、バイト数、UTCの最終書込時刻。ディレクトリのサイズはnull。不存在・アクセス不可・末端の再解析ポイントはError。 |
| ディレクトリと親の作成 | `fs.create_dir_all` | 既存ディレクトリなら成功。同じパスがファイルならError。 |
| ファイル1個のコピー・移動 | `fs.copy_file`, `fs.move_file` | 宛先はディレクトリではなく完全なファイルパス。既存の宛先は上書きしない。親は必要。 |
| ディレクトリツリーの移動・名前変更 | `fs.move_dir` | 同一ボリューム内の名前変更。宛先は完全なディレクトリパスで、存在してはならない。親は必要。 |
| ファイル1個の削除 | `fs.remove_file` | ディレクトリならError。 |
| 空ディレクトリの削除 | `fs.remove_dir` | ファイルまたは空でないディレクトリならError。 |
| ディレクトリツリーの削除 | `fs.remove_dir_all` | 明示的な再帰削除。ディレクトリリンクはたどらない。 |
| ジャーナル付き移動計画の実行・復旧 | `fs.move_files_batch`, `fs.recover_file_moves` | 同一ドライブのローカルNTFS専用。後述の復旧規則を参照。 |
| パス値の組み立て・分解 | `path.join`, `path.filename`, `path.extension`, `path.parent` | 存在を確認しない。 |
| 解決済みパス表記の比較 | `path.equal`, `path.find_duplicate` | Windowsの大文字小文字を区別しない表記比較。実体の同一性は調べない。 |
| 配布スクリプトの隣のデータを参照 | `path.script_dir` | 配布した`.ps1`のディレクトリ。作業ディレクトリやソースディレクトリではない。 |

全引数は必須の位置引数です。別名、任意フラグ、ファイルとディレクトリの暗黙の振り分けはありません。
`_file`は1ファイル、`_dir`はディレクトリを指します。create_dir_allとremove_dir_allの`_all`は親やサブツリーまで対象にすることを示します。

```rukbat
import fs
import path

output := path.join([path.script_dir(), "output"])
fs.create_dir_all(output)

for source in fs.list_files("input", "*.csv") {
    destination := path.join([output, path.filename(source)])
    fs.copy_file(source, destination)
}
```

この例のinputは開始時の作業ディレクトリからの相対パス、outputは配布スクリプトの隣です。
ディレクトリのコピーと生のバイト列のI/Oには対応していません。

<a id="standard-output-and-error"></a>

## 標準出力と標準エラー出力

組み込みの`print(value)`と`eprint(value)`は、それぞれ標準出力と標準エラー出力に書きます。
型検査は同じで、プラットフォームの改行を1個追加します。インポートは不要です。
明示的な文字列化には`text.from_*`を使います。出力だけでは終了コードは変わりません。

<a id="log"></a>

## `log`

診断メッセージには通常のLogger値を使います。既定ではINFO以上を標準エラー出力に書きます。
同じレコードをファイルにも残すには、フィールドを1つ設定します。

```rukbat
import log

logger := log.create()
logger.file = "job.log"
log.info(logger, "Starting")
```

ログは標準出力に値を流しません。ERRORは重要度であり、throwやexitを要求するものではありません。
レコードの準備や出力に失敗したときだけErrorになります。

| シグネチャ | 動作 |
|---|---|
| `log.create(): log.Logger` | I/Oを行わず、既定の設定を返します。 |
| `log.debug(logger: log.Logger, message: string): void` | 出力対象にDEBUGレコードを書きます。 |
| `log.info(logger: log.Logger, message: string): void` | 出力対象にINFOレコードを書きます。 |
| `log.warn(logger: log.Logger, message: string): void` | 出力対象にWARNレコードを書きます。 |
| `log.error(logger: log.Logger, message: string): void` | 出力対象にERRORレコードを書きます。 |
| `log.enabled(logger: log.Logger, level: log.Level): bool` | 少なくとも1つの出力先が指定レベルを受け付けるかを返します。 |

引数はすべて必須の位置引数です。文字列でない値は明示的に文字列化してください。
ログが必要な関数にはLoggerを渡します。グローバルロガーはありません。

<a id="logger-configuration-and-levels"></a>

### Loggerの設定とレベル

`log.Logger`は、次の3フィールドを持つ通常の構造体です。

| フィールド | 型 | 既定値 | 意味 |
|---|---|---|---|
| `stderr_level` | `log.Level?` | `log.level_info` | 標準エラー出力の最低重要度。nullで無効。 |
| `file` | `string?` | `null` | ファイルパス。nullでファイル出力を無効化。 |
| `file_level` | `log.Level` | `log.level_info` | ファイルの最低重要度。fileがnullの間は無視。 |

読み取り専用のレベル定数は、低い順に`log.level_debug`、`log.level_info`、`log.level_warn`、`log.level_error`です。
しきい値はそのレベル以上を受け付けます。ファイルと標準エラー出力のしきい値は独立しています。
ファイルだけに出すには`stderr_level = null`、すべて無効にするには`stderr_level = null`と`file = null`を設定します。

Levelは不透明な不変型です。通常の格納と、nullでない同型の等値比較ができます。
順序比較、算術、独自の数値レベル、公開フィールド、構造体リテラル、文字列・数値への暗黙変換はありません。
null許容Levelは通常のnull比較と絞り込みに従います。
Levelとそれを含むLoggerなどの値はJSONやPowerShellと受け渡せず、Levelを直接表示することもできません。

Loggerリテラルでは3フィールドをすべて初期化します。
ストリームや外部リソースを保持せず、close操作は不要です。
代入、引数、戻り値、コンテナーへの格納は通常の値コピーです。コピーしたLoggerの変更は元に影響しません。

<a id="filtering-and-evaluation"></a>

### フィルタリングと評価

引数はフィルタリング前に左から右へ1回ずつ評価します。
`log.info(logger, message())`では、message()より先にLoggerをコピーします。
message()内で設定を変えても、その呼び出しには反映されません。
フィルタリングは引数の副作用やエラーを抑制しません。
重いメッセージ生成には`if log.enabled(logger, log.level_debug) { ... }`で明示的に条件を付けます。

enabledが調べるのはしきい値だけです。
パス検証、ファイルシステムの確認、時刻取得、ネイティブヘルパーの読込はせず、後の書き込み成功も保証しません。不正なパスを設定していてもtrueを返し得ます。
どちらの出力先も受け付けない呼び出しでは、メッセージ検証、ネイティブ読込、時刻取得、パス解決、整形、エンコード、I/Oを行いません。
しきい値で除外されたファイル出力の設定が、標準エラー出力を妨げることはありません。

<a id="records-and-destinations"></a>

### レコードと出力先

レコードのフィールド順は固定で、末尾はCRLF1個です。

```text
time=2026-10-16T09:00:00.1234567Z level=INFO message="Starting"
```

出力対象の呼び出しごとにUTCの壁時計時刻を1回取得し、両出力先で共有します。
書式はdatetime.stringifyと同じカルチャ非依存の`yyyy-MM-ddTHH:mm:ss.fffffffZ`です。
小数7桁は時計の精度を保証しません。時刻は一意でも単調増加でもなく、順序が逆転する場合もあります。

メッセージは必ず引用符付きJSON文字列です。
引用符とバックスラッシュには`\"`と`\\`、バックスペース、タブ、LF、フォームフィード、CRには`\b`、`\t`、`\n`、`\f`、`\r`を使います。
それ以外のU+0000～U+001F、U+007F～U+009F、U+2028、U+2029は小文字の`\uXXXX`です。
その他の有効なUnicodeスカラー値は補助文字も含めて保持します。空メッセージも有効です。
対にならないUTF-16サロゲートは出力前にErrorになります。
メッセージ内の改行や端末エスケープ文字が、物理的な追加行やANSI制御シーケンスを作ることはありません。
メッセージの秘匿化はしません。

標準エラー出力はConsole.Errorを使い、レコードごとにフラッシュします。
スクリプトのUTF-8コンソール設定とホストのリダイレクトに従い、色やPowerShellストリームの接頭辞を付けません。

ファイルは厳密なBOMなしUTF-8です。
相対パスは、後でPowerShellや.NETの場所を変えても、保存した開始時の作業ディレクトリを基準にします。
配布場所を基準にするにはpath.script_dir()を使います。
ファイルがなければ最初の出力時に作りますが、親ディレクトリは必要です。
既存バイト列は走査、デコード、書き換え、切り詰めをしません。
既存ログは互換性のあるUTF-8で、レコード境界で終わっている必要があります。BOMや改行の補完、末尾の修復はしません。
[ログの例](../../examples/logging.rukbat)も参照してください。

ファイルを開く前にバイト列を準備します。
書き込みごとにFileMode.Append、FileAccess.Write、FileShare.Readで開き、Flush(true)を呼んで閉じてから戻ります。fs.append_textと同じです。
バッファリング・バックグラウンドモード、ロック再試行、プロセス間の不可分な追記や順序保証はありません。
同時書き込みは失敗し得ます。読み取り側との共有モードも双方向で互換性が必要で、実行中のfs.lines走査はログ追尾には使えません。
I/O失敗でレコードの一部だけが残る場合があります。フラッシュ完了は、停電やフラッシュ要求に従わないストレージでの永続性を保証しません。

<a id="failure-behavior"></a>

### 失敗時の動作

共有レコードの準備は出力前に行い、ここで失敗するとどちらにも書きません。
出力は標準エラー出力、選択したファイルの順に試し、前者が失敗してもファイル出力を試します。
パス解決はファイル出力の段階で行うため、不正な選択パスでも標準エラー出力は妨げません。
両方を試した後、失敗した出力先と説明をまとめた1つのErrorを送出します。
ファイルエラーには解決済みパス、解決失敗時には指定パスを含めます。closeも失敗した場合は元のwrite/flushの失敗を保持します。
出力エラーにはメッセージ本文を含めず、通常のソース情報でログ呼び出し位置を示します。

一方への成功した出力は取り消しません。Errorでも何も書かなかったとは限らず、再試行で重複する場合があります。
catch内のログも通常どおり失敗し得ます。元のErrorを保つ必要があれば、ログの失敗を別途処理してください。
自動再試行、代替出力先、ログローテーションはありません。

<a id="text"></a>

## `text`

<a id="text-operations"></a>

### テキスト操作

`text.operation(value, ...)`のように値を先頭に渡します。引数は必須の位置引数です。
操作は元の引数を変えずに新しい値を返します。モードフラグや別名はありません。
照合は文字どおりで、大文字小文字を区別する序数比較を使い、OSのカルチャに依存しません。
正規表現、ワイルドカード、Unicode正規化は行いません。
null許容文字列は先に絞り込む必要があり、nullは空文字列ではありません。

| シグネチャ | 例 | 結果 |
|---|---|---|
| `text.trim(value: string): string` | `text.trim("  東京  ")` | `"東京"` |
| `text.length(value: string): int` | `text.length("A😀B")` | `3` |
| `text.slice(value: string, start: int, count: int): string` | `text.slice("A😀B", 1, 1)` | `"😀"` |
| `text.index_of(value: string, search: string): int?` | `text.index_of("A😀B", "B")` | `2` |
| `text.upper(value: string): string` | `text.upper("report.csv")` | `"REPORT.CSV"` |
| `text.lower(value: string): string` | `text.lower("REPORT.CSV")` | `"report.csv"` |
| `text.contains(value: string, search: string): bool` | `text.contains("接続エラー", "エラー")` | `true` |
| `text.starts_with(value: string, prefix: string): bool` | `text.starts_with("report.csv", "report")` | `true` |
| `text.ends_with(value: string, suffix: string): bool` | `text.ends_with("report.csv", ".csv")` | `true` |
| `text.replace_all(value: string, search: string, replacement: string): string` | `text.replace_all("a-b-c", "-", "_")` | `"a_b_c"` |
| `text.split(value: string, separator: string): string[]` | `text.split("赤,青,緑", ",")` | `["赤", "青", "緑"]` |
| `text.split_lines(value: string): string[]` | `text.split_lines("赤\r\n青\n緑")` | `["赤", "青", "緑"]` |
| `text.join(values: string[], separator: string): string` | `text.join(["赤", "青"], ",")` | `"赤,青"` |

trimが除くのは先頭と末尾の空白だけで、内部は変えません。
対象はU+0009～U+000D、U+0020、U+0085、U+00A0、U+1680、U+2000～U+200A、U+2028、U+2029、U+202F、U+205F、U+3000です。
タブ、改行、全角空白を含みます。U+180E、U+200B（ゼロ幅スペース）、U+FEFF（BOM）は除きません。
空または空白だけの入力は`""`です。対象集合は固定で、PowerShell 5.1と7のUnicodeテーブル差に影響されません。

contains、starts_with、ends_withは検索文字列・接頭辞・接尾辞が空なら、valueが空でもtrueを返します。
空でない検索文字列は空のvalueには一致しません。

replace_allは左から右へ、重複しない箇所を置換します。
置換文字列は文字どおりに扱い、再検索しません。`"a"`のaをaaに置換すると`"aa"`、`"aaa"`のaaをbに置換すると`"ba"`です。
空の置換文字列なら一致箇所を削除し、一致しなければ元の値です。searchが空なら、入力も空であってもErrorになります。

splitはseparator全体を1つの文字列として、重複させず左から照合します。空の要素と空白をすべて保持します。
`text.split("a,,b,", ",")`は`["a", "", "b", ""]`、区切りがなければ[value]、空入力は`[""]`です。
空のseparatorはErrorです。単純な区切り分割なので、CSVの引用やエスケープにはcsv.parseを使います。

split_linesはCRLFを1つの改行として扱い、単独のLFとCRにも対応します。
改行を除き、内容と空行を保持します。空入力は`[]`です。
末尾の改行は最後の行を終えるだけで、追加の空行は作りません。
`"a\n"`は`["a"]`、`"\n"`は`[""]`、`"a\n\n"`は`["a", ""]`です。
U+0085、U+2028、U+2029など、その他のUnicode区切りは行内容のままです。

joinは要素間にだけseparatorを置き、末尾には付けません。空の要素も保持します。
空配列は`""`、1要素ならその要素、空のseparatorなら直接連結します。
空でない区切りでsplitした結果を同じ区切りでjoinすると、元の入力を正確に復元します。

length、slice、index_ofは、0始まりのUnicodeコードポイント数を使います。
UTF-16コード単位数や見た目の文字（書記素クラスター）数ではありません。絵文字などの補助文字は1、結合文字は別に数えます。
sliceの指定は開始位置と個数で、終了位置ではありません。startが長さに等しくてよいのはcountが0のときだけです。
負数や範囲外を切り詰めて補正せず、Errorにします。
index_ofは最初の文字どおりの序数一致の位置、なければnullを返し、空の検索文字列なら0です。
この3操作とupper/lowerは、対にならないUTF-16サロゲートを拒否します。
大文字小文字の変換はホスト.NETのInvariant Unicodeテーブルを使い、正規化や完全なケースフォールディングはしません。
新たに追加された文字の対応はランタイム版に依存する場合があります。

手続き的なファイル処理には[ログ抽出の例](../../examples/filter-errors.rukbat)を参照してください。

<a id="conversion-and-parsing"></a>

### 文字列変換と解析

| シグネチャ | 意味 |
|---|---|
| `text.from_int(value: int): string` | 符号付き10進表現。 |
| `text.from_float(value: float): string` | 有限のbinary64値を、再解析で元の値を保てるカルチャ非依存の文字列に変換。 |
| `text.from_bool(value: bool): string` | 小文字の`true`または`false`。 |
| `text.parse_int(value: string): int` | 符号付き64ビット範囲内の10進整数を解析。 |
| `text.parse_float(value: string): float` | 有限の10進表記のbinary64値を解析。 |
| `text.parse_bool(value: string): bool` | 大文字小文字を区別し、`true`または`false`だけを受け付ける。 |

parse操作は先頭・末尾のASCII空白（U+0009～U+000DとU+0020）を除き、その他の空白を拒否します。
数値解析はOSロケールに依存せず、ASCII数字を使います。
整数は`[+-]?[0-9]+`で、小数点と指数は不可です。
floatは`[+-]?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?`で、小数点はドットです。
`.5`、`1.`、桁区切り、NaN、Infinityは拒否します。
不正な入力、オーバーフロー、0でない入力が0にアンダーフローする場合はErrorです。表現できる非正規化数は許可します。
floatの文字列化は再解析時の数値を保ちますが、入力表記や0の符号は保持を保証しません。

<a id="array"></a>

## `array`

すべての操作は、元の要素型で新しい配列を返し、通常の深い値コピーに従います。変更を残すには結果を代入します。
引数は必須の位置引数で、明示的な型引数は受け付けません。
以下のTとKは推論する要素型を表し、ユーザー定義ジェネリックではありません。空の入力には宣言済み配列型が必要です。

| シグネチャ | 意味 |
|---|---|
| `array.slice(values: T[], start: int, count: int): T[]` | 0始まりのstartからcount個の要素をコピー。 |
| `array.remove_at(values: T[], index: int): T[]` | 0始まりのindexの要素を除いたコピーを返す。 |
| `array.sort(values: T[]): T[]` | 順序比較できる要素を昇順に安定ソート。 |
| `array.sort_descending(values: T[]): T[]` | 順序比較できる要素を降順に安定ソート。 |
| `array.sort_by(values: T[], keys: K[]): T[]` | キーの昇順で値を安定ソート。 |
| `array.sort_by_descending(values: T[], keys: K[]): T[]` | キーの降順で値を安定ソート。 |

sliceは開始位置と個数を指定し、順番を保ちます。開始位置が長さと等しくてよいのは個数0の場合だけです。
負数・範囲外は補正せずErrorにします。remove_atは空配列へのすべてのインデックスを拒否します。
両方ともnull許容値、入れ子の配列、構造体、標準の不透明値を含む全対応要素型に使えます。

順序比較できる要素・キーは、nullでないint、float、string、datetime.Timestamp、datetime.Date、datetime.Durationです。
bool、null許容キー、構造体、配列などは型検査で拒否します。
文字列はカルチャ非依存のUTF-16コード単位の序数比較で、大文字小文字を区別します。正規化や自然順ソートはしません。
整数は正確に、datetimeキーは正確なtick数で比較します。同じキーは降順でも元の順序を保ちます。
sort_by系には値と同じ個数のキーが必要で、数が違うとErrorです。入力とキーは記述順に1回ずつ評価します。
CSV行や構造体の並べ替えには別のキー配列を作ります。

```rukbat
import array
import csv
import text

table := csv.parse_table("name,amount\nHanako,20\nTaro,10\n")
amounts: int[] = []
for row in table.rows {
    amounts.append(text.parse_int(csv.get(row, "amount")))
}
table.rows = array.sort_by_descending(table.rows, amounts)
print(csv.stringify_table(table))
```

<a id="math"></a>

## `math`

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `math.to_float(value: int): float` | binary64へ明示的に変換。最近接丸めを使い、中間値は偶数側に丸める。大きな整数は精度を失う場合がある。 | なし。 |
| `math.to_int(value: float): int` | 整数値のfloatを符号付き64ビット整数に変換。小数部分は切り捨てない。 | 小数部分がある、または範囲外ならError。 |
| `math.trunc(value: float): float` | 小数部分をゼロ方向に切り捨てる。 | なし。 |
| `math.floor(value: float): float` | 負の無限大方向に丸める。 | なし。 |
| `math.ceil(value: float): float` | 正の無限大方向に丸める。 | なし。 |

引数は必須の位置引数で、型引数はありません。暗黙の型変換も追加しません。
`math.to_int(math.trunc(3.9))`は3、`math.to_int(3.9)`はErrorです。
floatは近似的な2進値であり、正確な10進の金額ではありません。
整数のJSON・文字列変換は64ビットの全桁を保ちます。`json.parse<float>`と`powershell<float>`は近似の数値変換を明示的に求める操作です。
JSONの受け手がbinary64を使うと大きな整数の精度を失う場合があるため、必要なら識別子を文字列で交換してください。

<a id="datetime"></a>

## `datetime`

`datetime.Timestamp`、`datetime.Date`、`datetime.Duration`、`datetime.Zone`は不透明な不変値です。
通常の変数、引数、戻り値、null許容型、配列、構造体フィールドとして使えますが、公開フィールドや構造体リテラルはありません。
`datetime.utc`、`datetime.tokyo`、`datetime.eastern`、`datetime.pacific_mexico`などは読み取り専用のZone定数です。
全対応名とWindows IDは[タイムゾーン定数一覧（英語）](../TIME_ZONES.md)にあります。マシンのローカルゾーン設定には依存しません。
Timestamp、Date、Durationは同型の等値・順序比較、Zoneは正式IDによる等値比較に対応します。算術には以下の関数を使います。

Timestampは表示方法に依存しないUTCの1時点で、入力のオフセット表記は保持しません。
Dateはタイムゾーンのないグレゴリオ暦の日付で、年は1～9999です。
TimestampとDurationの分解能は100ナノ秒です。Durationは符号付き64ビットのtick数を保持し、作成時に範囲を検査します。
時計の分解能と正確さは実行先の性質です。nowは壁時計であり、単調な経過時間の測定ではありません。

| シグネチャ | 意味 |
|---|---|
| `datetime.now(): datetime.Timestamp` | 壁時計を1回読み、UTCの時点を返す。 |
| `datetime.parse(text: string): datetime.Timestamp` | オフセット付きの日時を解析し、時点と小数部最大7桁を保持する。 |
| `datetime.stringify(value: datetime.Timestamp): string` | 標準のUTC表記yyyy-MM-ddTHH:mm:ss.fffffffZを返す。 |
| `datetime.format(value: datetime.Timestamp, pattern: string, zone: datetime.Zone): string` | 明示したゾーン規則と制限付き書式で日時を整形する。 |
| `datetime.parse_local(text: string, pattern: string, zone: datetime.Zone): datetime.Timestamp` | 明示したゾーンのローカル日時を解析。不正・曖昧な日時はError。 |
| `datetime.zone(id: string): datetime.Zone` | Tokyo Standard TimeなどのWindowsタイムゾーンIDを大文字小文字を区別して検索する。 |
| `datetime.local_zone(): datetime.Zone` | 実行先マシンの現在のローカルゾーンを明示的にスナップショットとして取得する。 |
| `datetime.zone_id(value: datetime.Zone): string` | 外部設定用のWindowsタイムゾーンIDを返す。 |
| `datetime.create_date(year: int, month: int, day: int): datetime.Date` | 不正な要素を補正せず、有効なグレゴリオ暦の日付を作る。 |
| `datetime.date(value: datetime.Timestamp, zone: datetime.Zone): datetime.Date` | 指定ゾーンでの暦日を取り出す。 |
| `datetime.parse_date(text: string): datetime.Date` | 正確にyyyy-MM-dd形式を解析する。 |
| `datetime.stringify_date(value: datetime.Date): string` | 標準のyyyy-MM-dd形式を返す。 |
| `datetime.format_date(value: datetime.Date, pattern: string): string` | 日付用トークンだけで日付を整形する。 |
| `datetime.at(date: datetime.Date, hour: int, minute: int, second: int, zone: datetime.Zone): datetime.Timestamp` | 秒の小数部が0のローカル日時を作り、明示したゾーンで解決する。 |
| `datetime.add_days(value: datetime.Date, count: int): datetime.Date` | 符号付きの日数を暦日として加える。 |
| `datetime.add_months(value: datetime.Date, count: int): datetime.Date` | 符号付きの月数を加える。対応する日がなければ移動先の月末にする。 |
| `datetime.milliseconds(count: int): datetime.Duration` | 整数ミリ秒から符号付き経過時間を作る。 |
| `datetime.seconds(count: int): datetime.Duration` | 整数秒から符号付き経過時間を作る。 |
| `datetime.minutes(count: int): datetime.Duration` | 整数分から符号付き経過時間を作る。 |
| `datetime.hours(count: int): datetime.Duration` | 整数時間から符号付き経過時間を作る。 |
| `datetime.days(count: int): datetime.Duration` | 符号付き経過時間を作る。1日は正確に24時間。 |
| `datetime.add(value: datetime.Timestamp, duration: datetime.Duration): datetime.Timestamp` | UTCの時間軸上で経過時間を加える。 |
| `datetime.between(start: datetime.Timestamp, end: datetime.Timestamp): datetime.Duration` | 精度を失わずendからstartを引く。 |
| `datetime.total_seconds(value: datetime.Duration): float` | 表示・集計用に近似のbinary64秒数を返す。正確な判定にはDuration同士を比較する。 |
| `datetime.from_unix_seconds(value: int): datetime.Timestamp` | 1970-01-01T00:00:00Zからの符号付き整数秒で時点を作る。 |
| `datetime.from_unix_milliseconds(value: int): datetime.Timestamp` | Unixエポックからの符号付き整数ミリ秒で時点を作る。 |
| `datetime.to_unix_seconds(value: datetime.Timestamp): int` | エポックからの符号付き秒数を返す。エポック前も含め、小数部を負の無限大方向へ切り捨てる。 |
| `datetime.to_unix_milliseconds(value: datetime.Timestamp): int` | エポックからの符号付きミリ秒数を返す。エポック前も含め、ミリ秒未満を負の無限大方向へ切り捨てる。 |

引数は必須の位置引数で、型引数や暗黙変換はありません。
不正入力、未知のゾーン、範囲超過、存在しない・曖昧なローカル日時はErrorです。
黙って範囲を折り返す、日付範囲に収める、現在日に置き換えることはしません。
add_monthsの月末補正は仕様であり、1か月加えてから引いても同じ日に戻るとは限りません。

parseが受け付けるのは`YYYY-MM-DDTHH:mm:ss[.fraction](Z|+HH:mm|-HH:mm)`です。
ASCII数字、大文字T/Z、小数1～7桁、最大±14時間のオフセットを使います。
空白、オフセットなし、うるう秒（`:60`）、`24:00`、桁数超過、不明なオフセット`-00:00`は拒否します。
stringifyは必ず小数7桁とZを出力し、再parseで正確な時点を復元します。parse_dateは時刻も空白も受け付けません。

書式トークンは`yyyy`、`MM`、`dd`、`HH`、`mm`、`ss`、1～7個の`f`、数値オフセットの`zzz`です。
その他の英字は一重・二重引用符で囲んだリテラルにします。区切り文字はそのまま使い、数値出力はカルチャに依存しません。
未知のトークン、閉じていない引用符、空パターンはErrorです。.NETの標準書式名は受け付けません。
format_dateは時刻・オフセットのトークンを拒否します。
parse_localはオフセットを拒否し、yyyy、MM、dd、HH、mmを各1回必要とします。秒の省略は0、小数部にはssが必要です。フィールドの重複も拒否します。
表示書式によって精度を省略できるため、正確な保存にはstringifyを使います。

Zoneは夏時間の切替を含むWindowsのタイムゾーン規則を保持します。入力日時のオフセットはZoneとは異なります。
zoneはIANA別名の解決やマシンのゾーンの推測をしません。ローカル設定から選ぶのはlocal_zoneだけです。
定数とzone(id)は実行先にインストールされたWindowsの規則を使います。
定数はアクセス時だけ解決し、IDがなければErrorです。使わない定数のIDが実行先にある必要はありません。
定数一覧はRukbatで固定し、コンパイラを動かすマシンのゾーンから生成しません。
外部設定や一覧より後に追加されたIDにはzone(id)を使います。
夏時間切替で2回現れる時刻や存在しない時刻はErrorです。特定の時点を選ぶにはオフセット付きparseを使います。
days(1)は24時間の経過、add_days(date, 1)は次の暦日です。

datetime値は、入れ子の配列・構造体内も含めて、直接のprint/eprint、JSON変換、PowerShellの引数・結果に使えません。
受け渡しには文字列、ゾーンID、Unix整数、近似の経過時間への明示的な変換を使います。
生成スクリプトはヘルパーを内蔵し、Rukbatの別途インストールなしでWindows PowerShell 5.1で動きます。

```rukbat
import datetime

tokyo := datetime.tokyo
now := datetime.now()
print(datetime.stringify(now))
print(datetime.format(now, "yyyy-MM-dd HH:mm:ss zzz", tokyo))
deadline := datetime.parse("2026-10-31T18:00:00+09:00")
if now >= deadline {
    print("expired")
}
today := datetime.date(now, tokyo)
yesterday := datetime.add_days(today, -1)
print(datetime.stringify_date(yesterday))
```

<a id="encoding"></a>

## `encoding`

`encoding.Encoding`は検証済みの型で、26個の読み取り専用の定義済み値があります。

| 値 | 外部名 | 意味 |
|---|---|---|
| `encoding.utf8` | `utf-8` | UTF-8 |
| `encoding.utf16_le` | `utf-16le` | UTF-16リトルエンディアン |
| `encoding.utf16_be` | `utf-16be` | UTF-16ビッグエンディアン |
| `encoding.windows_932` | `windows-932` | NEC/IBM拡張を含むWindows CP932 |
| `encoding.utf32_le` | `utf-32le` | UTF-32リトルエンディアン |
| `encoding.utf32_be` | `utf-32be` | UTF-32ビッグエンディアン |
| `encoding.ascii` | `ascii` | 厳密なASCII |
| `encoding.iso_8859_1` | `iso-8859-1` | ISO-8859-1 |
| `encoding.windows_874` | `windows-874` | Windowsコードページ874 |
| `encoding.windows_1250` | `windows-1250` | Windowsコードページ1250 |
| `encoding.windows_1251` | `windows-1251` | Windowsコードページ1251 |
| `encoding.windows_1252` | `windows-1252` | Windowsコードページ1252 |
| `encoding.windows_1253` | `windows-1253` | Windowsコードページ1253 |
| `encoding.windows_1254` | `windows-1254` | Windowsコードページ1254 |
| `encoding.windows_1255` | `windows-1255` | Windowsコードページ1255 |
| `encoding.windows_1256` | `windows-1256` | Windowsコードページ1256 |
| `encoding.windows_1257` | `windows-1257` | Windowsコードページ1257 |
| `encoding.windows_1258` | `windows-1258` | Windowsコードページ1258 |
| `encoding.windows_936` | `windows-936` | Windowsコードページ936 |
| `encoding.windows_949` | `windows-949` | Windowsコードページ949 |
| `encoding.windows_950` | `windows-950` | Windowsコードページ950 |
| `encoding.windows_437` | `windows-437` | Windowsコードページ437 |
| `encoding.windows_850` | `windows-850` | Windowsコードページ850 |
| `encoding.windows_852` | `windows-852` | Windowsコードページ852 |
| `encoding.windows_866` | `windows-866` | Windowsコードページ866 |
| `encoding.gb18030` | `gb18030` | GB18030（Windowsコードページ54936） |

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `encoding.parse(name: string): encoding.Encoding` | 外部名を型付きの値に変換。大文字小文字を区別し、上記の定義済み名か、追加対応コードページの正式なwindows-N形式を使う。別名、前後空白の除去、自動判定は行わない。 | 未知・利用不可・非対応の名前はError。 |
| `encoding.from_code_page(number: int): encoding.Encoding` | 実行先の固定Windowsコードページを検証し、正式な値を返す。932はencoding.windows_932と等しい。 | 厳密な変換器が利用できない、または非対応ならError。 |
| `encoding.name(value: encoding.Encoding): string` | 設定やログ用の正式な外部名を返す。 | なし。 |

対応する文字コードは定義済み値だけではありません。
実行先のWindows APIが厳密なデコード・エンコードのフラグを受け付ける場合、from_code_pageで追加の固定コードページを使えます。
追加値はwindows-N形式ですが、定義済みの特別名は維持します。65001はutf-8でありwindows-65001ではありません。
parse(name(value))は同じ値に戻ります。

0、1、2、3（環境依存の既定値）、Symbol（42）、UTF-7、ISO-2022やISCIIのような状態を持つ文字コード、EUC-JP（20932 / 51932）は非対応です。
非対応値はErrorにします。文字の黙った置換や最も近い文字への代替変換はしません。利用可否は実行先マシンで確認します。

ソース内では型付きの値を直接使い、外部テキストを受け取る場合だけparseを使います。

```rukbat
import encoding
import fs

selected := encoding.windows_932
content := fs.read_text("input.csv", selected)
print(encoding.name(selected))
```

通常の代入、引数、戻り値、配列、構造体フィールド、null許容型、同型の`==` / `!=`に対応します。
フィールド、コンストラクター、数値変換、文字列との暗黙変換はありません。
表示・保存にはencoding.name、復元にはencoding.parseを使います。
入れ子のコンテナーも含めて、直接のJSON変換とPowerShellの引数・結果は拒否します。

<a id="fs"></a>

## `fs`

ファイルとディレクトリの両方の操作を含み、どちらも`import fs`で使えます。

<a id="file-operations"></a>

### ファイル操作

`fs.Info`は`is_dir: bool`、`size: int?`、`modified: datetime.Timestamp`（UTCの最終書込時刻）を持つ通常の値構造体です。
fs.statはメタデータのスナップショットを読みます。ファイルのsizeはバイト数、ディレクトリではnullです。
時刻の比較や整形にはdatetimeをインポートします。
不在・アクセス不可のエントリ、末端の再解析ポイント（リンク・ジャンクションを含む）はErrorです。親パスは通常のファイルシステム解決に従います。
メタデータは呼び出し中や後に変わり得ます。不可分なスナップショット、ファイルロック、実体の同一性確認、後続操作の成功保証ではありません。
時刻の精度はファイルシステムに依存します。
modifiedがTimestampなのでInfoは直接JSON変換できません。出力時は別の構造体にフィールドをコピーし、modifiedをdatetime.stringifyで変換します。

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `fs.stat(path: string): fs.Info` | ファイル・ディレクトリの種類、バイト数、UTCの最終書込時刻を読む。 | 不存在、アクセス不可、再解析ポイント、I/O失敗はError。 |
| `fs.list_files(directory: string, pattern: string): string[]` | 直下のHiddenでないファイルのうち、ファイルシステムのワイルドカードpatternに一致する絶対パスを返す。ディレクトリは除き、再帰せず、順序は保証しない。 | ディレクトリのアクセス不可・不存在、不正パターンはError。 |
| `fs.list_files_recursive(directory: string, pattern: string): string[]` | directory配下の全階層でファイル名ワイルドカードpatternに一致する絶対パスを返す。直下も含み、ディレクトリは除く。順序は保証しない。 | ディレクトリのアクセス不可・不存在、directoryへのファイル指定、不正パス・パターン、走査失敗はError。 |
| `fs.read_text(path: string, encoding: encoding.Encoding): string` | 明示した1つの文字コードでテキストを読む。 | ファイルの不存在・アクセス不可、不正バイト列、矛盾するUTF-16 BOMはError。 |
| `fs.lines(path: string, encoding: encoding.Encoding): fs.Lines` | 物理行を順次読む不変の記述子を作る。各forが独立した走査を開き、終了時に閉じる。 | 不正パス・文字コードは記述子作成時のError。open・read・デコード・BOM不一致・共有競合・後始末の失敗は走査中のError。 |
| `fs.write_text(path: string, text: string, encoding: encoding.Encoding): void` | 明示した文字コードで、BOMや暗黙の改行を付けずファイルを置換。親は作らない。新しい全内容を宛先ディレクトリの一時ファイルに書いてから、移動または置換する。 | アクセス不可、親なし、エンコード不可はError。 |
| `fs.append_text(path: string, text: string, encoding: encoding.Encoding): void` | 厳密にエンコードしたバイト列を追記。BOMや改行を付けない。空テキストでもファイルがなければ作成するが、親は作らない。 | 不正・アクセス不可のパス、親なし、エンコード不可、共有競合、I/O失敗はError。I/O失敗では一部だけ追記される場合がある。 |
| `fs.create_temp_file(directory: string): string` | 既存ディレクトリに空の一時ファイルを排他的に作り、絶対パスを返す。 | 親の不正・不存在・アクセス不可、親へのファイル指定、名前衝突の再試行上限到達、I/O失敗はError。 |
| `fs.is_file(path: string): bool` | 存在するファイルならtrue。不在、ディレクトリ、不正パス、アクセス失敗などFile.Existsが判定できない場合はfalse。 | パス解決や存在確認の失敗はErrorではなくfalse。 |
| `fs.copy_file(source: string, destination: string): void` | ファイル1個をコピー。既存宛先の上書きや親の作成はしない。 | コピー元の不存在・アクセス不可・ディレクトリ指定、宛先あり、親なし、同一パス、I/O失敗はError。 |
| `fs.move_file(source: string, destination: string): void` | ファイル1個を移動。同じディレクトリ内なら名前変更。既存宛先の上書きや親の作成はしない。成功後は元がなくなる。 | 移動元の不存在・ディレクトリ指定、宛先あり、親なし、同一パス、I/O失敗はError。ボリューム間移動は可能だが不可分ではなく、エラー後に宛先が残る場合がある。 |
| `fs.remove_file(path: string): void` | ファイル1個を削除。不在でも成功。 | ディレクトリ、読み取り専用・使用中のファイル、不正パス、I/O失敗はError。ファイルの再解析ポイントは参照先ではなくエントリを削除。 |

encoding引数はencoding.Encoding型で、文字列は型検査で拒否します。
encodingをインポートし、定義済み値か、検証済みのencoding.from_code_page / encoding.parseの結果を渡します。
encoding.utf8は読込時に先頭のUTF-8 BOMだけを受け付けて除去します。
UTF-16/32は一致する先頭BOMを除去し、BOMなしも受け付け、逆エンディアンのBOMは拒否します。
書き込みはすべて厳密でBOMなしです。
encoding.windows_932は、従来の日本語Windowsソフトで使われるWindowsコードページ932（CP932 / Windows Shift-JIS）です。
すべてのShift_JIS変種を実装するものではなく、BOMの特別扱いもありません。
どの文字コードも不正な入力やエンコードできない出力は拒否し、データを置換しません。

fsの相対パスはエントリスクリプト開始時に保存した作業ディレクトリを基準にします。スクリプトの場所や後から変えたPowerShellの場所ではありません。
fs.write_textは全内容を検証・エンコードしてから、宛先ディレクトリ内の一意な一時ファイルに書き、フラッシュします。
最後に宛先が存在すればFile.Replace、なければFile.Moveを使います。その最後の操作より前の失敗では既存宛先を変えません。
宛先がない間に他の処理が作った場合、そのファイルは削除せずFile.Replaceで置換します。
最後の操作中のプロセス・システム・ストレージ・ファイルシステム障害に対する永続性や、全ファイルシステムでの不可分性は保証しません。
File.Replaceは属性やACLなどの宛先メタデータを統合することがあり、その途中でも失敗し得ます。fs.write_textはメタデータやACLの保持を保証しません。

fs.list_filesは直下、fs.list_files_recursiveは全階層の絶対パスを返します。
directoryはどちらも文字どおりのパスです。空でないpatternはファイル名だけに適用し、同じファイルシステムフィルターを使います。
単純な形式として`*`と`?`に対応します。`/`や`\`は使えず、ディレクトリ階層を表すglobにはできません。
list_filesはHiddenファイルを、list_files_recursiveはHiddenファイルとHiddenディレクトリ配下を除外します。System属性だけでは除外しません。
list_files_recursiveのルートに明示したディレクトリ再解析ポイントは許可しますが、配下で見つけたReparsePointは返さず、子のリンクやジャンクションをたどりません。
プラットフォームのエラー時に途中までの結果は返しません。

windows_932の読込はWindowsのコードページ対応表を厳密に検証して使います。
同じ文字に対応するNEC選定IBM・IBM拡張のバイト列も含みます。例えば`EE E0`と`FB FC`はどちらもU+9AD9です。
正しく読めても、再エンコードで同じバイト列に戻るとは限りません。
デコーダーは生成スクリプトに内蔵し、追加インストールは不要です。

fs.append_textはファイルを開く・作る前にテキストを検証・エンコードします。
BOMを含む既存バイト列は変えず、その文字コードを検出・検証しません。呼び出し側が既存内容と互換性のある文字コードを選びます。
1つの追記ストリームを開き、その間は読み取りを許可して他の書き手を排除し、新しいバイト列だけを書いてフラッシュします。
共有競合は待機や再試行をせずErrorにします。
トランザクションによる置換ではなく、write/flush失敗やホスト終了で末尾の一部だけが残る場合があります。
空テキストは不在の空ファイルを作り、既存ファイルには何も追加しません。BOMも改行も追加しないため、必要な改行はテキストに明示します。

<a id="sequential-line-reads"></a>

### 行を順次読み込む

`fs.Lines`は開始ディレクトリ基準で解決した絶対パスと正式な文字コード名を保持します。
作成・コピー時にファイルを開いたり確認したりはしません。
変数、const、引数、戻り値、フィールド、配列、辞書、null許容値として保持できます。
フィールド、コンストラクター、インデックス、length、next、closeは公開しません。
コンテナー内も含めて、表示、等値比較、JSON変換、PowerShell転送は拒否します。

文字列を読むには`for line in fs.lines(path, encoding)`を使います。
走査ごとに入力を1回評価し、FileShare.Readの独立した読み取り専用ハンドルを開いて先頭から読みます。繰り返し・入れ子の走査も独立します。
記述子やループ変数への再代入で、進行中の読込位置は変わりません。
記述子は内容やファイルの実体を固定せず、次の走査では保存パスにその時点で存在するファイルを開きます。
書き込み・削除・置換は開いているハンドルと競合し得ます。通常ファイルの同期読込であり、ログ追尾ではありません。

CRLF、単独CR、単独LFが行区切りで、結果から除きます。
空入力は0行、物理的な空行は空文字列です。末尾の区切りから追加の空行は作りません。
NULやUnicodeの区切りなど、その他の文字は保持します。対応する全encoding値とBOM規則を使います。
有効で変化しない入力なら、最後まで読んだ結果は`text.split_lines(fs.read_text(path, encoding))`と一致します。

EOF、break、return、外に伝わるErrorでは、その先に進む前にハンドルを解放します。
continueと本体内で処理したエラーでは開いたままです。正常なexitやホスト強制終了では生成コードの後始末を保証しません。
return式は後始末前に評価・コピーしますが、結果の出力は後始末の成功後です。
後始末失敗はErrorになり、元のErrorもある場合はその位置とメッセージを保持して後始末の情報を追加します。

先読みがあるため、有効な行の後のエラーが、その行を本体に渡す前に発生し得ます。不正入力で何行処理されるかは未定義です。
クローズは未読部分をデコード・検証しません。
内部で保持するメモリは固定バッファと、それまでの最長行に比例します。ユーザーが保存した値は別です。
行長上限はなく、任意の長い行に対する固定メモリ上限もありません。
複数行にまたがるCSVはレコードパーサーが必要で、物理行ごとのcsv.parseでは扱えません。

<a id="directory-operations"></a>

### ディレクトリ操作

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `fs.list_dirs(directory: string, pattern: string): string[]` | 名前ワイルドカードに一致する直下の通常ディレクトリの絶対パス。空・Hidden・Systemも含む。再帰せず、子の再解析ポイントは除外。順序は未定義。 | ルートの不正・アクセス不可・不存在、ルートへのファイル指定、不正パターン、列挙失敗はError。 |
| `fs.move_dir(source: string, destination: string): void` | 同一ボリューム内の名前変更で通常ディレクトリと内容を移動。同じ親内なら名前変更。宛先は存在不可、親は必要。 | 移動元の不存在・ファイル・再解析ポイント、宛先あり、親なし、同一パス、元の配下への移動、異なるドライブ・UNC共有・ボリューム、保護対象の移動元、I/O失敗はError。 |
| `fs.create_dir_all(path: string): void` | ディレクトリと不足する親を作る。既存ディレクトリなら成功。 | 同じパスのファイル、不正パス、I/O失敗はError。 |
| `fs.create_temp_dir(directory: string): string` | 既存ディレクトリに空の一時ディレクトリを排他的に作り、絶対パスを返す。 | 親の不正・不存在・アクセス不可、親へのファイル指定、名前衝突の再試行上限到達、I/O失敗はError。 |
| `fs.is_dir(path: string): bool` | 存在しアクセス可能なディレクトリだけtrue。ファイル、不在、不正パス、アクセス不可、判定不能はfalse。 | Errorではなくfalseを返す。 |
| `fs.remove_dir(path: string): void` | 空ディレクトリを削除。不在でも成功。 | ファイル、空でないディレクトリ、ファイルシステムのルート、保存した作業ディレクトリとその祖先、I/O失敗はError。ディレクトリの再解析ポイントは参照先ではなくエントリを削除。 |
| `fs.remove_dir_all(path: string): void` | ディレクトリと通常のサブツリーを削除。不在でも成功。 | ファイル、ファイルシステムのルート、保存した作業ディレクトリとその祖先、I/O失敗はError。部分削除があり得る。対象や配下の再解析ポイントはエントリを削除し、たどらない。 |

fs.list_dirsはルートを文字どおりに扱い、相対パスは保存した開始ディレクトリ基準で解決します。
空でないpatternはディレクトリ名に適用し、list_filesと同じファイルシステムのワイルドカードを使います。
`*`と`?`に対応し、`/`や`\`は拒否します。エラー時に途中までの結果は返しません。
ファイル列挙とは異なりHiddenも含みます。明示したルートは再解析ポイントでもよく、子の再解析ポイントだけを除きます。
走査が必要なら繰り返し呼び出します。

fs.move_dirは内容のコピーと削除ではなくディレクトリエントリの移動で、空・Hiddenの子も含むツリーを保ちます。
子のリンクはたどらずツリーと一緒に移しますが、移動元自体を再解析ポイントにはできません。
ファイルシステムのルート、保存した開始ディレクトリ、その祖先も移動元にはできません。
両パスは開始ディレクトリ基準で解決し、その後の場所の変更には依存しません。
末尾区切りは同一性を区別せず、大文字小文字を区別しない序数比較によって、大小だけの名前変更も拒否します。
自分の配下への移動は不可で、ボリューム間移動をコピー・削除で代替しません。
親パスは通常のファイルシステム解決に従い、特定ディレクトリ内への封じ込めや悪意ある並行変更への保証はありません。
親作成、既存宛先の置換、復旧ジャーナルは行いません。

```rukbat
import fs
import path
import encoding

fs.create_dir_all("processed")
for directory in fs.list_dirs("incoming", "batch-*") {
    destination := path.resolve(path.resolve(path.working_dir(), "processed"), path.filename(directory))
    fs.move_dir(directory, destination)
    fs.append_text("moves.log", "Moved " + destination + "\r\n", encoding.utf8)
}
```

<a id="temporary-files-and-directories"></a>

### 一時ファイルと一時ディレクトリ

fs.create_temp_fileとfs.create_temp_dirは、未使用名を提案するだけでなく、戻る前に排他的に作成します。
他の呼び出しが先に作った場合も、既存のファイル・ディレクトリを再利用、切り詰め、削除しません。
名前衝突なら新しいランダム候補で再試行し、1呼び出し最大32回です。他のファイルシステムエラーは名前衝突として再試行せず失敗します。

親ディレクトリは事前に必要です。文字どおりのパスとして扱い、相対なら他のfs操作と同じ開始ディレクトリ基準です。
親は明示的に選びます。OSの一時ディレクトリを自動選択したり親を作成したりしません。
親の再解析ポイントを含め通常のパス解決を使います。親をロックせず、返したエントリを他プロセスの後続変更から保護もしません。
名前と拡張子は実装詳細なので、返されたパスをそのまま使ってください。
返る時点でファイルは0バイトで開いたハンドルはなく、ディレクトリは空です。Windowsの権限は親から通常どおり継承します。

後始末は明示的にfs.remove_file、fs.remove_dir、fs.remove_dir_allを呼びます。
return、Error、プロセス終了で自動削除しません。後続処理の失敗時にも後始末するならtry/catchを使ってください。

```rukbat
import fs
import encoding

fs.create_dir_all("work")
directory := fs.create_temp_dir("work")
try {
    file := fs.create_temp_file(directory)
    fs.write_text(file, "temporary content", encoding.utf8)
    print(fs.read_text(file, encoding.utf8))
    fs.remove_file(file)
} catch error {
    fs.remove_dir_all(directory)
    throw error
}
fs.remove_dir(directory)
```

<a id="recoverable-file-moves"></a>

### 復旧可能なファイル移動

| シグネチャ | 意味 |
|---|---|
| `fs.move_files_batch(sources: string[], destinations: string[], journal: string): void` | 以下の制約の下で、永続的な復旧ジャーナル付きの移動計画を実行。 |
| `fs.recover_file_moves(journal: string): void` | 以下の制約の下で、記録された移動計画を復旧または検証。 |

移動計画の上限は100000件、シリアライズした計画が64 MiBです。超過は移動元の変更前に拒否し、ジャーナル読込にも対応する上限を適用します。

move_files_batchは、排他的なファイルロックと永続的な復旧ジャーナルで計画全体を移動します。
recover_file_movesは中断した未コミットの計画を元のパスに戻します。
ローカル固定NTFSが必要で、全移動元・宛先・ジャーナルが同一ドライブ上になければなりません。
パス中の再解析ポイント、複数リンクを持つファイル、代替データストリーム、重なるパス、曖昧な末尾ドット・空白パスは拒否します。
ジャーナルの親は事前に必要です。

最初の名前変更の前に、全移動元をwrite/deleteに対して、そのディレクトリをrename/deleteに対してロックし、宛先ディレクトリを作成・ロックします。
絶対パス、ファイルID、作成時刻、SHA-256ハッシュを記録し、チェックサム付きジャーナルをストレージへフラッシュします。
ジャーナルも排他ロックするため、同じジャーナルの同時使用は移動前に失敗します。
名前変更はロックしたハンドルで行い、既存宛先は置換しません。全宛先の親を準備するまで移動しません。
準備失敗後に空ディレクトリや空ジャーナルが残る場合があります。

通常の移動失敗では完了済み移動を戻します。ロールバックも失敗するとErrorが保持したジャーナルを示すので、捨てないでください。
ホスト終了後はrecover_file_moves、または同じ計画・ジャーナルのmove_files_batchを呼びます。
復旧はファイル変更前に、記録した全ファイルの実体と内容を検証します。
別のファイルへの置換・変更、壊れたジャーナル、曖昧な移動元・宛先の状態、アクセス不可があれば、上書きせず復旧を停止します。
復旧自体も中断・再実行できます。ロールバック完了後は同じmove_files_batchで再実行できます。

全移動後にコミット印をフラッシュします。
コミット済みの計画を繰り返すと宛先を検証して返り、再移動しません。別計画にそのジャーナルは再利用できません。
コミット済み計画へのrecover_file_movesも検証だけで、完了した移動を取り消しません。
ジャーナルは完了記録として保持し、別の新しいジョブには新しいジャーナルを使います。
復旧時のジャーナル不在はエラーです。空でない壊れた・途中で切れたジャーナルを黙って初期化しません。

クラッシュ後に復旧できますが、他の読み手には途中のパスが見え、不可分なスナップショットにはなりません。作成したディレクトリは残ります。
プロセス終了後の復旧はテストで確認していますが、ディスク破損やフラッシュ要求を無視するハードウェアまでは保証しません。
ジャーナルと操作対象ディレクトリはジョブのアカウントの管理下に置いてください。
この保証はmove_files_batchに限られ、任意のfs.move_fileの連続、削除、外部プログラムの副作用には適用しません。

<a id="path"></a>

## `path`

ファイルシステムのエントリを調べず、パス値を操作します。
存在確認にはfs.is_file / fs.is_dirを使います。path.parentは親のパス値を計算するだけです。

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `path.script_dir(): string` | 起動時に保存した配布スクリプトのディレクトリを返す。 | なし。 |
| `path.working_dir(): string` | エントリスクリプト開始時に保存した、ファイルシステム上の作業ディレクトリの絶対パスを返す。 | なし。 |
| `path.resolve(base_dir: string, value: string): string` | 明示した絶対ディレクトリを基準に解決し、正規化したWindows絶対パスを返す。実在は不要。 | 基準が絶対パスでない、不正・空のパス、ドライブ相対パス、非対応形式はError。 |
| `path.relative(base_dir: string, target: string): string` | 絶対パスで指定した基準ディレクトリから対象までの相対パスを、構成要素単位で求める。対象も絶対パスで指定する。同じ場所なら`.`。実在は不要。 | 絶対パスでない・不正な入力、非対応形式、異なるドライブ・UNC共有はError。 |
| `path.is_absolute(value: string): bool` | 対応するWindowsパスが、現在の場所に依存せずドライブまたはUNCサーバー・共有を確定するかを調べる。 | 空、不正、相対、非対応形式はfalse。 |
| `path.stem(value: string): string` | 末端ファイル名から最後の拡張子を除く。ルート、末尾区切り、末端が`.`や`..`なら空文字列。 | 不正・空のパス、非対応形式はError。 |
| `path.change_extension(value: string, extension: string): string` | 最後の拡張子を置換したパスを返す。空文字列なら拡張子削除。ディレクトリ表記を保ち、ファイル自体は変えない。 | 不正パス・拡張子、非対応形式、ファイル名なし、結果のファイル名が空ならError。 |
| `path.join(parts: string[]): string` | 実行先プラットフォームの規則で1個以上のパス要素を結合。 | 空配列、不正要素はError。 |
| `path.filename(value: string): string` | 末端ファイル名を返す。ディレクトリ区切りで終わる場合は空文字列。 | 不正パスはError。 |
| `path.extension(value: string): string` | 末端要素の拡張子を先頭の`.`込みで返す。なければ空文字列。 | 不正パスはError。 |
| `path.parent(value: string): string` | ファイルシステムの確認や相対パスの解決をせず、字句上の親を返す。親要素がなければ`.`、ルートは保持。ルート以外の末尾区切りは無視。 | 空、空白のみ、不正パスはError。 |
| `path.equal(left: string, right: string): bool` | 両方を保存した開始ディレクトリ基準で解決し、Windowsの大文字小文字を区別しない序数比較で比較。実在は不要。 | 不正、空、空白のみ、ドライブ相対パスはError。 |
| `path.find_duplicate(paths: string[]): string?` | path.equalの規則で最初に重複したパスを元の表記で返す。なければnull。 | 重複を見つける前の不正パスはError。 |

join、filename、extension、parentは文字列を操作し、相対パスを解決せず実在も要求しません。
`parent("C:child")`は`"C:"`ですが、ファイル操作はこのドライブ相対表記を拒否します。
UNC共有ルートは末尾区切り付きで保持します。joinが相対パスを返した場合、後で標準APIに渡した時点で保存した作業ディレクトリ基準に解決します。

path.script_dir()は、モジュール初期化前に$PSScriptRootから保存した、実行中の.ps1の絶対ディレクトリです。
インポートした関数内でも使え、元ソースの場所ではなく配布スクリプトの場所に従います。

```rukbat
config := path.join([path.script_dir(), "config.json"])
```

working_dirは、fsの相対パスと子プロセスの既定の作業場所が使う、同じ開始ディレクトリを返します。
後でPowerShellや.NETの場所を変えても、インポートした関数から呼んでも値は変わりません。

resolveのbase_dirは完全修飾の絶対パスで、実在しなくてもディレクトリとして扱います。
通常の相対valueはそこを基準にし、`/data/file.csv`のようなルート相対は基準のドライブまたはUNC共有のルートから解決します。
完全修飾のvalueは自身のルートを使います。
`.`と`..`はWindowsの絶対パス正規化に従い、区切りは`\`に揃えます。ドライブ・UNC共有ルートの末尾`\`と、その他の末尾区切りは保持します。
リンク解決、存在確認、環境変数・`~`展開、ワイルドカード解釈はしません。

```rukbat
input := path.resolve(path.working_dir(), "input/data.csv")
config := path.resolve(path.script_dir(), "config/settings.json")
data := path.resolve(path.parent(config), "../data")
```

relativeは両引数とも完全修飾の絶対パスを必要とするため、相対入力には先にresolveを使います。
両パスを正規化し、ルートと構成要素をWindowsの大文字小文字を区別しない序数比較で照合します。baseは常にディレクトリです。
末尾区切りは無視し、同じ構成要素なら`.`、区切りは`\`、親方向には`..`を返します。
`relative("C:/work/data", "C:/work/out/a.csv")`は`"..\\out\\a.csv"`です。
ドライブ・サーバー・UNC共有が異なれば、絶対パスを返さずErrorにします。実体の同一性や配下への封じ込めを調べる操作ではありません。

is_absoluteは`C:/work`のようなドライブ絶対パスと`//server/share/data`のような完全なUNCパスを受け付けます。
通常の相対パス、ルート相対の`/data`、ドライブ相対の`C:data`、不完全なUNCルート、非対応形式はfalseです。
trueでも存在・権限・後続操作の成功は保証しません。

resolve、relative、is_absolute、stem、change_extensionは通常のWindowsドライブ・UNCパスに対応します。
デバイス名前空間（`\\?\`、`\\.\`）、PowerShellプロバイダーパス、代替データストリーム、ドライブ相対表記、不正なパス文字は拒否します。
変換操作はError、is_absoluteはfalseです。空や空白だけの入力も不正です。

stemとchange_extensionは最後の拡張子を扱います。
`stem("archive.tar.gz")`は`"archive.tar"`、`stem("README")`は`"README"`、`stem(".env")`は`""`です。extension(".env")が".env"だからです。
`stem("report.")`は`"report"`で、拡張子の置換・除去でも最後のドットを除きます。
拡張子を変えてもディレクトリの文字列、区切り、相対・絶対の形式を保ちます。
指定拡張子は空、またはドットで始まり後に1文字以上続く文字列です。区切り、コロン、制御文字、不正なパス文字は不可です。
`change_extension("dir/report.csv", ".json")`は`"dir/report.json"`、拡張子に`""`を渡せば`"dir/report"`です。
ルート、末尾区切り、末端が`.`や`..`、`change_extension(".env", "")`のようにファイル名がなくなる除去は拒否します。

equalは解決したパス表記を比較します。シンボリックリンク、ジャンクション、ハードリンク、短縮名、ファイルシステムのオブジェクトIDは解決しません。
equalとfind_duplicateは末尾区切りを保持して比較するため、`equal("C:/work", "C:/work/")`はfalseです。
relativeはディレクトリ構成要素で比較するため、この組には`.`を返します。

find_duplicateは入力順に調べ、前のパスと最初に重複したパスを元の表記で返します。空配列を含め、重複なしならnullです。
equalと同じ開始ディレクトリ基準の解決と、大文字小文字を区別しない序数比較を使い、実在は不要です。
重複を見つける前の不正パスはErrorで、最初の重複で探索を止めます。
各パスを1回解決して集合に照合し、計画検証で全組み合わせの比較を避けます。入力配列は変更しません。

<a id="choosing-csv-and-json-operations"></a>

## CSVとJSONの操作の選択

入力に合う表現を選びます。

| 目的 | API |
|---|---|
| ヘッダー付きCSVで列名からセルを読む | csv.parse_table(text)がcsv.Tableを返す。csv.get、csv.set、csv.stringify_tableを使う。 |
| ヘッダーなしCSV、または列数が異なるレコード | csv.parse(text)がstring[][]を返す。書き込みはcsv.stringify(rows)。 |
| 構造が未知・混在するJSON | json.parse(text)がjson.Valueを返す。get / itemsで選び、`as<T>`で変換。 |
| 固定スキーマのJSON | `json.parse<T>(text)`でその型を検証。 |
| 未知のプロパティを保ったJSON更新 | json.set / remove、set_at / remove_at / appendを使い、返された値を代入。 |
| JSONの書き出し | json.stringify(value)が入力型を推論。 |

パーサーに渡すのはファイル名ではなく内容です。文字列化は内容を返すだけで、ファイルに書きません。
ファイルアクセスと明示的な文字コードにはfs.read_text / fs.write_textを使います。
引数は位置引数だけで、別名はありません。文書全体をメモリに保持し、ストリーミングAPIではありません。
シリアライズはデータを保持しますが、元のテキスト配置は保ちません。

<a id="csv"></a>

## `csv`

<a id="header-tables"></a>

### ヘッダー付きの表

最初のレコードが列名であるCSVに使います。
`csv.Table`の公開フィールドは、ヘッダー順の`columns: string[]`と、データ行だけの`rows: csv.Row[]`です。
csv.Rowは不透明な不変値で、get / setでセルを扱います。
どちらも公開の構造体リテラルはありません。表はparse_table / table、行はrowで作ります。

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `csv.parse_table(text: string): csv.Table` | ヘッダーを解析し、全データ行を検証して返す。 | 空入力、空・重複の列名、不正CSV、列数不一致はError。列数エラーにはヘッダー込みの1始まりCSVレコード番号を示す。 |
| `csv.table(columns: string[], rows: string[][]): csv.Table` | 列名とセル文字列から表を作る。rowsが空配列ならヘッダーだけの表。 | 不正列名・列数不一致はError。行番号は渡したデータ行の1から。 |
| `csv.row(columns: string[], values: string[]): csv.Row` | 指定列名とセル文字列から1行を作る。 | 不正列名・列数不一致はError。 |
| `csv.get(row: csv.Row, column: string): string` | 大文字小文字を区別する完全な列名でセルを読む。 | 未知の列はError。 |
| `csv.has_column(table: csv.Table, column: string): bool` | データ行がなくても、現在のヘッダーに列があるかを確認。 | 不正な表の列名はError。 |
| `csv.set(row: csv.Row, column: string, value: string): csv.Row` | 1セルを置換したコピーを返す。変更を残すには代入する。 | 未知の列はError。 |
| `csv.stringify_table(table: csv.Table): string` | stringifyと同じエスケープで、ヘッダーの後にデータ行を書き出す。 | 表の列名が不正、または名前・順序の違う列を持つ行があればError。 |

列名は文字どおりの文字列です。ドット、スラッシュ、括弧、空白、Unicodeに特別な意味はありません。
前後空白を除かず、Aとaを区別します。空でない列名が1個以上必要です。
セルは空文字列も含めて文字列のままです。型付きの値にはtext.parse_int、text.parse_float、text.parse_boolで変換します。
必須列の確認や任意列の処理には、走査前にhas_columnを使います。

ヘッダーだけのファイルはデータ0行です。空のデータレコードは空フィールド1個を表し、ヘッダーの列数と一致する必要があります。
行順は保持します。表と配列はRukbatの通常のコピー規則に従います。
setは元の行も、それを含む表も変更しません。
絞り込みはcsv.Row[]を作り、行の追加はtable.columnsから作った行をappendし、セルの更新は返された行を代入します。

```rukbat
import csv
import encoding
import fs

people := csv.parse_table(fs.read_text("people.csv", encoding.windows_932))
kept: csv.Row[] = []

for person in people.rows {
    if csv.get(person, "enabled") == "true" {
        kept.append(csv.set(person, "note", "checked"))
    }
}
people.rows = kept
people.rows.append(csv.row(people.columns, ["Hanako", "true", "new"]))
fs.write_text("enabled.csv", csv.stringify_table(people), encoding.utf8)
```

この例で追加する行は、列がname,enabled,noteの順であることを前提にします。列名でのアクセスは入力の順番によらず使えます。
違う列構成にするには、csv.tableと、その構成に合わせた行で新しい表を作ります。
table.columnsを変えても既存行の名前や順番は変わりません。stringify_tableはセルを誤った列へ割り当てず、不一致を拒否します。
csv.Rowはjsonで変換できず、`powershell<T>`の結果にもできません。他形式に出す場合は通常の構造体や文字列配列を明示的に作ります。

<a id="raw-records"></a>

### レコードの配列

ヘッダーがない入力や、意図的に列数が異なる入力に使います。

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `csv.parse(text: string): string[][]` | ヘッダーを解釈せず、列数の揃った表を要求せず、レコードを文字列として解析。 | 不正なCSV構文はError。 |
| `csv.stringify(rows: string[][]): string` | カンマ、引用符、改行をエスケープしてCSVにする。 | フィールドが0個の行はError。1始まりのレコード番号を示す。 |

フィールド区切りはカンマです。CRLFとLFがレコード区切りで、末尾のレコード区切りは任意です。
引用フィールド内のカンマと改行は保持し、内部の`""`は`"`になります。空白とUnicodeも保持します。
引用された空フィールドも含め、空フィールドは`""`です。空入力は`[]`、空レコードは`[""]`で、捨てません。
引用フィールド外の単独CR、引用されていないフィールド内の引用符、閉じていない引用符、閉じ引用符の後の区切り以外の内容はErrorです。
csv.parseはRukbat文字列を受け取り、文字コードのデコードやU+FEFFの特別扱いはしません。
例えば`csv.parse(fs.read_text("legacy.csv", encoding.windows_932))`のように、fs.read_textで文字コードを選びます。

stringifyはフィールド間にカンマ、最後を含む各レコード末尾にCRLFを付けます。
空フィールドと、カンマ・引用符・CR・LFを含むフィールドを引用し、内部の引用符を`""`にします。他のフィールドはそのままです。
`[]`は`""`、`[[""]]`は`"\"\"\r\n"`です。
行ごとに列数が違っても構いませんが、CSVでは0フィールドのレコードを表せないため、空行配列`[]`は拒否します。
表現できるレコードはすべて、parse(stringify(rows))で文字列と順番を保ちます。
どちらも入力を変更せず、数値・真偽値・表計算式のような表記でもセル内容を変えません。

この形式では最初のレコードも通常のデータです。ヘッダー検証と列名アクセスにはparse_tableを使います。
書き出すときにヘッダー行を明示的に含めることもできます。

```rukbat
import csv
import encoding
import fs

rows: string[][] = [
    ["name", "note"],
    ["Taro", "contains, a comma"],
    ["Hanako", ""]
]
fs.write_text("people.csv", csv.stringify(rows), encoding.utf8)

loaded := csv.parse(fs.read_text("people.csv", encoding.utf8))
print(loaded[1][0]) // Taro
```

<a id="json"></a>

## `json`

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `json.parse(text: string): json.Value` | スキーマやユーザー定義構造体なしでJSONを解析。全プロパティ、配列要素、数値トークンを保持。 | 不正JSON・プロパティ名重複はError。 |
| `json.parse<T>(text: string): T` | 明示したRukbat型にJSONを解析。 | 不正JSON、Tとの不一致はError。 |
| `json.stringify(value: T): string` | 対応する型付き値またはjson.Valueを、空白を省いたJSON文字列にする。 | 非対応値、有限でないRukbat floatはError。 |
| `json.get(value: json.Value, name: string): json.Value` | 大文字小文字を区別する完全な名前でオブジェクトの1プロパティを返す。 | プロパティなし、またはオブジェクト以外はError。 |
| `json.has(value: json.Value, name: string): bool` | 名前付きプロパティがあるかを確認。JSON nullの値も存在に含む。 | オブジェクト以外はError。 |
| `json.keys(value: json.Value): string[]` | プロパティ名を序数順に返す。空オブジェクトなら空配列。 | オブジェクト以外はError。 |
| `json.items(value: json.Value): json.Value[]` | nullや異種の値も含め、配列要素を順番に返す。空配列なら空配列。 | 配列以外はError。 |
| `json.kind(value: json.Value): string` | object、array、string、number、bool、nullのいずれかの文字列を返す。 | なし。 |
| `json.set(value: json.Value, name: string, replacement: T): json.Value` | 1プロパティを追加・置換したオブジェクトを返す。他のプロパティは保持。 | オブジェクト以外はError。 |
| `json.remove(value: json.Value, name: string): json.Value` | 名前付きプロパティを除いたオブジェクトを返す。不存在でも成功。 | オブジェクト以外はError。 |
| `json.set_at(value: json.Value, index: int, replacement: T): json.Value` | 既存の1要素を置換した配列を返す。インデックスは0始まり。 | 配列以外、範囲外はError。 |
| `json.remove_at(value: json.Value, index: int): json.Value` | 1要素を除いた配列を返す。後続要素は左へ詰める。 | 配列以外、範囲外はError。 |
| `json.append(value: json.Value, replacement: T): json.Value` | 末尾に1要素追加した配列を返す。空配列にも追加できる。 | 配列以外はError。 |
| `json.as<T>(value: json.Value): T` | 選択したJSON値だけを、`parse<T>`と同じ厳密な規則で明示したRukbat型へ変換。 | Tとの不一致はError。 |

<a id="reading-json-without-a-schema"></a>

### スキーマなしでJSONを読む

`json.Value`は標準ライブラリの不透明な不変値です。文書全体や入れ子のプロパティなど、任意のJSON値を表します。
構造体宣言は不要で、代入、引数、return、配列・構造体フィールドへの格納ができます。
内容へのアクセスはJSON操作だけで、公開フィールドやコンストラクターはありません。更新は新しい値を返し、元の値を変えません。

プロパティはget、配列の走査やインデックスアクセスはitems、必要な値だけの変換は`as<T>`を使います。
プロパティ名は文字どおりなので、ドット、スラッシュ、括弧、空名にも特別なエスケープやパス構文は不要です。
未知のプロパティ名の確認にはkeysを使います。

例えば応答が`{"data":{"users":[{"name":"Taro","nickname":null,"extra":{"unknown":1}}]}}`の場合です。

```rukbat
import json
import encoding
import fs

document := json.parse(fs.read_text("response.json", encoding.utf8))
data := json.get(document, "data")
users := json.items(json.get(data, "users"))

for user in users {
    name := json.as<string>(json.get(user, "name"))
    print(name)

    if json.has(user, "nickname") {
        nickname := json.as<string?>(json.get(user, "nickname"))

        if nickname != null {
            print(nickname)
        }
    }
}
```

存在するJSON nullはkindが"null"の有効なjson.Valueです。
プロパティがない場合は別で、hasはfalse、getはErrorです。
`json.Value?`には、値がないことを表すRukbatの`null`も格納できます。JSON nullを表すnullでないjson.Valueとは区別します。

数値は変換するまでJSON数値トークンのままです。
例えば9007199254740993や1e400を、精度を失わず解析・選択・文字列化できます。
intへの変換には正確な64ビット整数、floatには有限のbinary64値が必要です。
stringifyは数値表記を保ち、オブジェクトのプロパティを名前の序数順、配列は元の順番で出します。
空白や文字列エスケープの元の表記は保持しません。

itemsとkeysは独立した通常のRukbat配列を返し、その配列を変えても元JSONは変わりません。
大きな文書の一部だけ構造が分かる場合、選んだオブジェクトを`as<MyStruct>`で変換します。
既知の構造体にjson.Valueフィールドを持たせ、任意の部分木を保持することもできます。

<a id="updating-json-while-retaining-unknown-data"></a>

### 未知のデータを保ったままJSONを更新する

更新は毎回新しいjson.Valueを返すため、明示的に代入します。
名前はgetと同じ文字どおり・大文字小文字を区別する規則です。
set_at / remove_atには既存要素が必要で、末尾追加にはappendを使います。
置換値にはjson.Value、プリミティブ、対応する構造体・配列・文字列キー辞書、null許容値、nullリテラルを使えます。
型は推論し、更新操作は`<T>`を受け付けません。空の置換配列には型を明示した変数が必要です。
配列をappendすると展開せず1要素として追加します。

入れ子の子を先に更新し、その結果を親に戻します。同じ親に属する未知のプロパティ・要素や、変更しない部分木は、JSON数値トークンも含めて保持します。
子だけを更新しても親は変わりません。子全体を置換すれば、その全内容を置き換えます。

```rukbat
import json
import encoding
import fs

config := json.parse(fs.read_text("config.json", encoding.utf8))
service := json.get(config, "service")
service = json.set(service, "enabled", true)
config = json.set(config, "service", service)
config = json.remove(config, "obsolete")
config = json.set(config, "note", null)

workers := json.get(config, "workers")
worker := json.items(workers)[0]
worker = json.set(worker, "enabled", false)
workers = json.set_at(workers, 0, worker)
workers = json.append(workers, json.parse('{"name":"new","enabled":true}'))
config = json.set(config, "workers", workers)
fs.write_text("updated.json", json.stringify(config), encoding.utf8)
```

未知のプロパティを残す更新では、スキーマなしの解析を使ってください。
`parse<T>` / `as<T>`で構造体に変換すると未宣言プロパティを無視し、再度文字列化しても戻せません。
動的オブジェクトは空白を省いてキーをソートして出すため、元の空白、キー順、エスケープ表記は保ちません。

<a id="dictionaries-and-json-objects"></a>

### 辞書とJSONオブジェクト

型付きのメモリ内検索・変更には辞書を使い、JSONとの変換は明示的に行います。
json.stringify(d)は対応するdict[string, V]を、キーが序数順のオブジェクトにします。
`json.parse<dict[string, V]>(text)` / `json.as<dict[string, V]>(value)`はオブジェクトを要求し、全プロパティ値を厳密に変換してキーを序数順に挿入します。
JSON更新の置換値にも対応する辞書を使えます。null、数値、プロパティ重複の規則は同じです。
往復変換は内容を保ちますが、挿入順は正規の順序に変わります。

整数キー辞書は配列・構造体内も再帰的に拒否し、キーを暗黙に文字列化しません。
不透明値を含むdict[string, fs.Info]などはメモリ内で使えてもJSONへの制約は変わりません。
整数キーを出力する場合は、フィールド名を明示したエントリ構造体の配列を作ります。

```rukbat
import json

ports := {"https": 443, "http": 80}
print(json.stringify(ports))
loaded := json.parse<dict[string, int]>('{"http":80,"https":443}')
print(loaded["https"])
```

<a id="reading-a-known-type"></a>

### 既知の型として読む

Tにはint、float、string、bool、json.Value、構造体、対応値の配列、Vが再帰的に対応するdict[string, V]、対応型のnull許容形を使えます。
csv.Row、cli.Result、encoding.Encoding、hash.Algorithm、全datetime型、http.Method、http.Bodyは非対応です。
配列、null許容型、辞書値、構造体フィールド内も同じ制約で、fs.Info.modifiedも含みます。
stringifyとJSON更新に渡す値にも適用します。
parse / asの`<T>`は`powershell<T>`と同じ専用の結果型指定であり、汎用ジェネリックではありません。

`parse<T>`と`as<T>`は厳密に変換します。

- プロパティ名は大文字小文字を区別し、重複はError。
- null許容フィールドも含めて、宣言された全構造体フィールドが必須。
- 余分なプロパティは無視。
- JSON nullがRukbatのnullになるのはT?だけ。json.Value宛てならJSON値として保持。
- 数値をintにできるのは、表記に小数点・指数がなく、値がintに正確に収まる場合だけ。
- 数値をfloatにできるのは、結果が有限の場合だけ。
- 文字列、真偽値、配列、オブジェクトを別の種類へ強制変換しない。

stringifyは空白を省き、末尾改行を付けません。構造体のプロパティは宣言順、内部のjson.Valueオブジェクトはプロパティ名の序数順です。
null許容値は内部の値かJSON nullになり、入力自体は変えません。
型は推論するためstringifyは`<T>`を受け付けません。空配列やnullは型を明示した変数にして、具体型を与えます。

```rukbat
import json
import encoding
import fs

struct Config {
    name: string
    enabled: bool
}

config := Config {
    name: "nightly",
    enabled: true
}
fs.write_text("config.json", json.stringify(config), encoding.utf8)

loaded := json.parse<Config>(fs.read_text("config.json", encoding.utf8))
print(loaded.name)

numbers := json.parse<int[]>("[1,2,3]")
print(numbers[0])
```

<a id="env"></a>

## `env`

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `env.get(name: string): string?` | 現在のRukbatプロセスの環境変数を読む。未定義ならnull、定義済みの空値なら空文字列。 | プラットフォーム上の不正なアクセスはError。 |
| `env.set(name: string, value: string): void` | 現在のRukbatプロセスに空でない値を設定。 | 空値、プラットフォーム上の不正なアクセスはError。 |
| `env.remove(name: string): void` | 現在のRukbatプロセスから名前を削除。未定義でも成功。 | プラットフォーム上の不正なアクセスはError。 |

Windows PowerShell 5.1 / .NET Frameworkは、空文字列の設定を削除として扱います。
Rukbatでは`env.set(name, "")`を拒否します。削除にはenv.remove(name)を使います。
親プロセスから継承した空値は`""`として読みます。

変更は現在のプロセスと、その後に起動する子プロセスの継承に作用します。
ユーザー・マシンの永続的な環境設定は変更しません。

<a id="hash"></a>

## `hash`

`hash.Algorithm`は不透明な検証済み値型です。
読み取り専用メンバーは`hash.sha256`、`hash.sha384`、`hash.sha512`、`hash.sha1`、`hash.md5`です。
通常の生成物のチェックサムにはSHA-256を使います。SHA-1とMD5は既存のチェックサムとの照合用で、新たな改ざん検出には使いません。
値は代入、通常のコンテナー、引数、return、同型の`==` / `!=`に対応します。
コンストラクター、フィールド、順序比較、暗黙変換、直接の表示・JSON・PowerShellとの受け渡しはありません。コンテナー内にも再帰的に適用します。

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `hash.file(path: string, algorithm: hash.Algorithm): string` | ファイルの正確なバイト列を逐次ハッシュし、小文字の16進数で返す。名前やメタデータは含まない。 | 不存在・読込不可、共有競合、アルゴリズム利用不可はError。 |
| `hash.text(value: string, algorithm: hash.Algorithm, encoding: encoding.Encoding): string` | BOMや改行なしで厳密にエンコードし、小文字の16進ダイジェストを返す。 | 不正UTF-16、エンコード不可、アルゴリズム利用不可はError。 |
| `hash.parse(name: string): hash.Algorithm` | 外部名sha256、sha384、sha512、sha1、md5のいずれかの完全な小文字表記から値を復元。 | 未知名、別名、大小の違い、前後空白はError。 |
| `hash.name(algorithm: hash.Algorithm): string` | 正式な外部名を返す。 | なし。 |

ハッシュ中は同時読み取りを許可し、書き込みと削除を禁止するファイルハンドルを使います。
ダイジェストの比較だけで、照合元ダイジェストの出所は認証できません。

<a id="zip"></a>

## `zip`

次の通常の値構造体を公開します。

```rukbat
struct Options {
    name_encoding: encoding.Encoding
    max_entries: int
    max_total_bytes: int
}

struct Entry {
    name: string
    is_directory: bool
    size: int
    compressed_size: int
}
```

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `zip.create(source_dir: string, archive_path: string): void` | 入力ディレクトリの内容をUTF-8名のDeflate ZIPにする。隠しファイルと空ディレクトリを含め、元ディレクトリ自体を追加のルートにしない。 | 不正パス、既存出力、親なし、リンク、読込不可、入力配下への出力はError。 |
| `zip.options(name_encoding: encoding.Encoding): zip.Options` | 読込・展開設定を作る。max_entriesは100000、max_total_bytesは10737418240（10 GiB）。文字コードはZIPのUTF-8フラグがない名前だけに適用。 | 名前へのUTF-16/32指定、非対応の厳密変換器はError。 |
| `zip.list(archive_path: string, options: zip.Options): zip.Entry[]` | 検証した中央ディレクトリ情報をアーカイブ順に返す。名前の区切りは/、ディレクトリ名は/で終わり、サイズ単位はバイト。ディレクトリのsizeは0。 | 不正・危険・非対応のメタデータ、名前衝突、宣言上限超過はError。全ペイロードのCRCを読み検証するものではない。 |
| `zip.extract(archive_path: string, destination_dir: string, options: zip.Options): void` | 全エントリ名の検証後、新しいディレクトリへ展開。逐次処理で実サイズとCRCを検証。 | 既存宛先（空ディレクトリも含む）、親なし、不正メタデータ、データ破損、上限超過、リンク、I/O失敗はError。 |

引数は必須の位置引数です。相対ファイルパスは保存した開始ディレクトリ基準で解決します。
読込はStore、Deflate、ZIP64に対応し、暗号化、分割、その他の圧縮形式は拒否します。
新規アーカイブの名前はUTF-8です。読込時は各エントリのUTF-8フラグに厳密に従い、なければ明示したname_encodingを使います。
自動検出や置換はしません。Unicode Path拡張フィールドも解釈せず、フラグと文字コード指定に従います。
ファイル内容の文字コードは変えません。UTF-16/32はZIP名の文字コードにできません。

listとextractは変更された設定を毎回検証します。
max_entriesは1～2147483647、max_total_bytesはバイト数を表す正の符号付き64ビット整数です。
展開ディレクトリ作成前に宣言された非圧縮サイズ合計を検査し、読込中に実サイズも検査します。
中央ディレクトリ情報はライブラリが解析・エントリ配列を確保する前に64 MiBまで、ディレクトリ階層は256要素までに制限します。
デコードしたパス索引にも64 MiBの上限があり、UTF-16キーのバイト数と、キーごとに64バイトを数えます。
これらはランタイムの資源制限であり、ZIP形式自体の上限ではありません。
圧縮データはロックしたストリームから読み、アーカイブ全体をメモリに載せません。
ZIPコメントとUnicode名拡張は公開も保持もしません。

エントリ名には、絶対パス、空・`.`・`..`の要素、ドライブ接頭辞、NUL、Windowsの不正文字、代替データストリーム、予約デバイス名、末尾のドット・空白を使えません。
従来のバックスラッシュ区切りは/に揃えます。
名前重複は大小を区別しない序数比較で調べ、ファイルとディレクトリ、暗黙の親との衝突も含めます。危険な名前は書き換えず拒否します。
アーカイブ内のリンク・特殊ファイルは拒否します。入力・アーカイブ・出力親のパスにも、シンボリックリンクやジャンクションを含む再解析ポイントは使えません。

createとextractは宛先の隣に一時出力を作り、完了後に公開します。途中で宛先が作られても既存の宛先は置換しません。
通常の失敗では一時出力を削除し、削除失敗もErrorです。ホスト強制終了では`.rukbat-zip-<id>.tmp`が残る場合があります。
クラッシュ復旧ジャーナルはなく、変化する入力ツリーの不可分なスナップショットも保証しません。
読込中のファイルはwrite/deleteに対してロックしますが、ディレクトリ列挙や他のファイルは変化し得ます。
入力と出力の親は呼び出し側の管理下に置き、悪意ある並行ファイル変更は保証対象外です。
展開はACL、所有者、代替データストリーム、元の属性・変更時刻を復元しません。
ファイルを選ぶ場合はfs操作で事前に一時入力ディレクトリへ集めます。
ZIPはパッケージ形式であり、完全なWindowsバックアップ形式ではありません。
作成時はエントリを序数順に走査しますが、ランタイム間で同一のZIPバイト列になるとは限りません。

```rukbat
import zip
import hash
import encoding

zip.create("./release", "./release.zip")
print(hash.file("./release.zip", hash.sha256))
options := zip.options(encoding.windows_932)
zip.extract("./legacy.zip", "./installed", options)
```

<a id="process"></a>

## `process`

次の通常の値構造体を公開します。

```rukbat
struct Result {
    exit_code: int
    stdout: string
    stderr: string
}

struct EnvironmentVariable {
    name: string
    value: string?
}

struct Input {
    text: string
    encoding: encoding.Encoding
}

struct Options {
    timeout_ms: int?
    output_encoding: encoding.Encoding
    working_dir: string?
    environment: EnvironmentVariable[]
    stdin: Input?
}
```

| シグネチャ | 意味 | 失敗 |
|---|---|---|
| `process.options(timeout_ms: int?, output_encoding: encoding.Encoding): process.Options` | 実行設定を作る。working_dirとstdinはnull、environmentは空。nullのタイムアウトは明示的に無制限。制限時間は1～2147483647ミリ秒。 | 不正タイムアウト、非対応文字コードはError。 |
| `process.run(executable: string, arguments: string[], options: process.Options): process.Result` | 指定条件で封じ込めた実行ファイルを起動し、標準出力と標準エラー出力を取得。 | 不正条件、起動・入力送信・出力取得・デコードの失敗、期限内に終了しない場合はError。 |

引数は必須の位置引数で、シェルコマンド文字列を解釈しません。
実行ファイルと各引数は別々の文字列で、NULは起動前に拒否します。
0以外の終了コードも通常のResultです。
output_encodingは標準出力・標準エラー出力に適用し、ファイルAPIと同じ厳密な文字コードを受け付けます。子の出力文字コードを設定するのではなく、読込側のデコード方法です。
BOM文字と末尾改行は保持し、空出力は空文字列です。不正バイト列はErrorです。自動判定やBOM除去はしません。
両出力ストリームは並行して読み取ります。

```rukbat
import encoding
import process

options := process.options(30000, encoding.windows_932)
options.working_dir = "./work"
options.environment = [process.EnvironmentVariable { name: "APP_MODE", value: "batch" }]
options.stdin = process.Input { text: "request\n", encoding: encoding.utf8 }
result := process.run("legacy-tool.exe", ["--report"], options)
print(result.stdout)
```

0以下・上限超過のタイムアウトは、options作成時と、フィールド編集後も含むrun時に、起動前に拒否します。
プロセスは生成と不可分にWindows Job Objectへ割り当て、kill-on-closeと離脱禁止を設定します。
runはルートと子孫プロセス、入力送信、両出力取得を待ち、生成成功から1つの期限を計ります。
条件検証、環境取得、入力エンコードは生成前、出力デコードは取得後です。
同期操作なので常駐バックグラウンドサービスの起動には使えません。
タイムアウト、入力・出力取得失敗ではジョブを終了します。ホスト終了もジョブを閉じ、所属プロセスを終了します。
後始末はジョブ終了に最大5秒、I/O完了に最大5秒を待ち、失敗すればErrorです。nullタイムアウトは意図的な無制限処理にだけ使ってください。

封じ込めにはWindows 10 / Server 2016以降が必要で、封じ込めなしでの代替起動はしません。
継承するハンドルは標準入力・出力・エラー出力だけです。出力上限は各64 MiBで、超過するとジョブを終了してErrorにします。
対象は通常の子プロセス生成であり、既存サービスやリモートマシンに委ねた作業は含みません。終了させても外部の副作用は取り消せません。

working_dirがnullなら保存した開始ディレクトリを使います。
指定したパスもそこを基準に解決し、アクセス可能な既存ディレクトリが必要です。親プロセスの場所は変えません。

environmentはrunの呼び出しごとに親のWindows環境を取得し、上書き設定を重ねます。
省略した名前は継承し、nullでない値は設定・置換、空文字列は空値、nullは子から削除します。親や永続設定は変えません。
名前は大小を区別しない序数比較で、PATH/Pathを含む重複、空名、名前中の=やNUL、値中のNUL、不正UTF-16を起動前に拒否します。
継承されたWindows内部のドライブ状態は保ちますが、呼び出し側からは指定できません。
子だけの空値はネイティブ環境ブロックで実現し、env.setの空値規則は変えません。

stdinがnullなら、ジョブ実行元の入力を継承せず即EOFにします。
Inputは起動前に厳密にエンコードし、BOM・追加改行なし、エンコード後最大64 MiBです。
両出力を読む間に送信し、終わったら書き手を閉じてEOFを通知します。空入力も即EOFです。
エンコード不可、サイズ超過、子が先に入力を閉じる場合などの不完全書き込みはErrorです。
書き込み完了はパイプへの送信を示すだけで、子が読み処理した保証ではありません。
入力文字コードは出力と独立します。上限付きのテキスト入力であり、対話・ストリーミング入力ではありません。

区切りやドライブ接頭辞を含む実行ファイルパスは、working_dirによらず保存した開始ディレクトリ基準で解決します。
単なる名前は開始ディレクトリ、子に適用されるPATHの順に探し、PATHの相対エントリも開始ディレクトリ基準です。拡張子がなければ.exeを使います。
引数はWindows C/C++のargv引用規則でそのまま渡し、Rukbat文字列1個がargvの1引数になります。
引数内のパスは子が解釈し、通常は子のworking_dir基準です。
直接起動できるバイナリが必要で、.cmd/.batは拒否します。シェル解釈は追加しません。

<a id="http"></a>

## `http`

HTTPは同期的な手続き型APIで、PowerShellのWebコマンドレットの挙動には依存しません。HTTP/HTTPSのAPIリクエスト、テキスト・JSONの送信、サイズ制限付きのファイルダウンロードに対応します。引数はすべて必須の位置引数です。言語構文、パッケージ、配備先のサービスは追加しません。

`http.Method`は検証済みの値を持つ不透明型です。読み取り専用の値として`http.method_get`、`http.method_head`、`http.method_post`、`http.method_put`、`http.method_patch`、`http.method_delete`、`http.method_options`があります。同じ型同士の等値比較と通常のコンテナーに対応します。`http.Body`は、接続から切り離されたレスポンスのバイト列を保持する不透明な不変型です。コンストラクター、フィールド、添字アクセス、等値比較はなく、開いたストリームの有効期間を管理する必要もありません。両型とも、入れ子の場合を含めて、出力、JSON変換、PowerShellの引数・戻り値には使えません。Bodyのデコードには`http.text`を使います。不変の格納領域は、通常の代入やコンテナーで安全に共有できます。

HTTPステータス定数は、`http.status_<number>_<name>`という名前の読み取り専用の`int`値です。例は`http.status_200_ok`、`http.status_404_not_found`、`http.status_503_service_unavailable`です。`response.status`はintのままで、未割り当てや独自のコードも表せます。番号を省いた別名はありません。名前は[IANAレジストリ](https://www.iana.org/assignments/http-status-codes)に従い、RFC 9110の`413_content_too_large`、`414_uri_too_long`、`416_range_not_satisfiable`、`422_unprocessable_content`を含みます。`306_reserved`と従来の`418_im_a_teapot`は予約済みコードの名前です。`510_not_extended`は廃止済みです。`104_upload_resumption_supported`はレジストリ上の一時的な割り当てであり、アップロードの再開機能への対応を意味しません。

| 定数 | 値 |
|---|---|
| `http.status_100_continue` | 100 |
| `http.status_101_switching_protocols` | 101 |
| `http.status_102_processing` | 102 |
| `http.status_103_early_hints` | 103 |
| `http.status_104_upload_resumption_supported` | 104 |
| `http.status_200_ok` | 200 |
| `http.status_201_created` | 201 |
| `http.status_202_accepted` | 202 |
| `http.status_203_non_authoritative_information` | 203 |
| `http.status_204_no_content` | 204 |
| `http.status_205_reset_content` | 205 |
| `http.status_206_partial_content` | 206 |
| `http.status_207_multi_status` | 207 |
| `http.status_208_already_reported` | 208 |
| `http.status_226_im_used` | 226 |
| `http.status_300_multiple_choices` | 300 |
| `http.status_301_moved_permanently` | 301 |
| `http.status_302_found` | 302 |
| `http.status_303_see_other` | 303 |
| `http.status_304_not_modified` | 304 |
| `http.status_305_use_proxy` | 305 |
| `http.status_306_reserved` | 306 |
| `http.status_307_temporary_redirect` | 307 |
| `http.status_308_permanent_redirect` | 308 |
| `http.status_400_bad_request` | 400 |
| `http.status_401_unauthorized` | 401 |
| `http.status_402_payment_required` | 402 |
| `http.status_403_forbidden` | 403 |
| `http.status_404_not_found` | 404 |
| `http.status_405_method_not_allowed` | 405 |
| `http.status_406_not_acceptable` | 406 |
| `http.status_407_proxy_authentication_required` | 407 |
| `http.status_408_request_timeout` | 408 |
| `http.status_409_conflict` | 409 |
| `http.status_410_gone` | 410 |
| `http.status_411_length_required` | 411 |
| `http.status_412_precondition_failed` | 412 |
| `http.status_413_content_too_large` | 413 |
| `http.status_414_uri_too_long` | 414 |
| `http.status_415_unsupported_media_type` | 415 |
| `http.status_416_range_not_satisfiable` | 416 |
| `http.status_417_expectation_failed` | 417 |
| `http.status_418_im_a_teapot` | 418 |
| `http.status_421_misdirected_request` | 421 |
| `http.status_422_unprocessable_content` | 422 |
| `http.status_423_locked` | 423 |
| `http.status_424_failed_dependency` | 424 |
| `http.status_425_too_early` | 425 |
| `http.status_426_upgrade_required` | 426 |
| `http.status_428_precondition_required` | 428 |
| `http.status_429_too_many_requests` | 429 |
| `http.status_431_request_header_fields_too_large` | 431 |
| `http.status_451_unavailable_for_legal_reasons` | 451 |
| `http.status_500_internal_server_error` | 500 |
| `http.status_501_not_implemented` | 501 |
| `http.status_502_bad_gateway` | 502 |
| `http.status_503_service_unavailable` | 503 |
| `http.status_504_gateway_timeout` | 504 |
| `http.status_505_http_version_not_supported` | 505 |
| `http.status_506_variant_also_negotiates` | 506 |
| `http.status_507_insufficient_storage` | 507 |
| `http.status_508_loop_detected` | 508 |
| `http.status_510_not_extended` | 510 |
| `http.status_511_network_authentication_required` | 511 |

このモジュールは、変更可能な通常の値型structを公開します。以下の型名には`http`の修飾が付きます。

```rukbat
struct Header { name: string; value: string }
struct QueryParameter { name: string; value: string }
struct TextBody { text: string; encoding: encoding.Encoding }
struct Request {
    method: http.Method
    url: string
    headers: http.Header[]
    body: http.TextBody?
}
struct Options {
    timeout_ms: int
    max_response_bytes: int
    proxy_url: string?
    use_windows_credentials: bool
    proxy_use_windows_credentials: bool
    max_redirects: int
}
struct Response {
    status: int
    url: string
    headers: http.Header[]
    body: http.Body
}
struct DownloadResult {
    status: int
    url: string
    headers: http.Header[]
    saved: bool
    size: int
    error_body: http.Body?
}
```

| シグネチャ | 意味 | 失敗時 |
|---|---|---|
| `http.request(method: http.Method, url: string): http.Request` | ヘッダーが空、本文がnullのリクエストを作ります。 | URLが無効ならError。 |
| `http.options(timeout_ms: int): http.Options` | max_response_bytesが67108864、proxy_urlがnull、両認証フラグがfalse、max_redirectsが0の設定を作ります。 | タイムアウトが1〜2147483647ミリ秒の範囲外ならError。 |
| `http.send(request: http.Request, options: http.Options): http.Response` | リクエストを送信し、上限内のレスポンス全体を取得します。2xx以外も含みます。 | 条件の不正、接続・TLSの失敗、タイムアウト、不完全なレスポンス、非対応のコンテンツ符号化、上限超過はError。 |
| `http.text(body: http.Body, encoding: encoding.Encoding): string` | 取得済みのバイト列を明示した文字コードで厳密にデコードします。BOMと改行を保持します。 | 不正なバイト列や非対応の文字コードはError。 |
| `http.header_values(headers: http.Header[], name: string): string[]` | 名前の大文字・小文字を区別せず、該当するヘッダーの値をすべて集めます。該当しなければ空配列を返します。 | ヘッダー名が無効ならError。 |
| `http.with_query(url: string, parameters: http.QueryParameter[]): string` | 名前と値をUTF-8のパーセントエンコードで入力順にクエリへ追加します。同名の項目と既存のクエリ文字列を保持します。空白は+ではなく%20になります。 | URLやUnicodeが不正ならError。 |
| `http.download(request: http.Request, destination: string, max_bytes: int, options: http.Options): http.DownloadResult` | GETのステータス200の本文を新しいファイルへ逐次書き込み、完了後に保存先へ配置します。他のステータスではsavedがfalseとなり、本文をerror_bodyに取得して、出力ファイルは作りません。 | 条件や保存先の不正、タイムアウト、転送失敗、上限超過、配置・後始末の失敗はError。 |

`send`はHTTPレスポンスと通信失敗を区別します。404、429、500などの最終ステータスも通常のResponseとして返し、暗黙の成功判定は行いません。テキストのデコードに失敗しても、受信済みのステータスやヘッダーは参照できます。JSON変換は`json.parse(http.text(response.body, encoding.utf8))`のように明示します。既知の型には`json.parse<T>`を使えます。Content-Typeやcharsetの推測、BOMの除去、代替文字によるデコードは行いません。

URLはホストを持つ絶対http/https URLでなければなりません。ユーザー情報、フラグメント、空白、制御文字、バックスラッシュ、不正なパーセントエスケープは使えません。リクエストヘッダーの名前はASCIIトークン、値は制御文字を含まないASCII文字列です。ただし水平タブは使えます。Host、Content-Length、Transfer-Encoding、Connection、Expect、Accept-Encoding、Proxy-Authorization、Trailer、TE、Upgradeはランタイムが管理するため指定できません。AuthorizationとContent-Typeの重複も拒否します。Content-Typeには本文が必要です。Content-TypeとTextBody.encodingは、それぞれ明示して選びます。変更された設定や本文の厳密なエンコードを含め、検証はすべて通信前に行います。GETとHEADに本文は指定できません。TextBodyはBOMや改行を追加せずにエンコードし、上限は64 MiBです。nullの本文と空文字列は異なり、空文字列は長さ0のリクエスト本文になります。

設定はsend/downloadのたびに検証します。max_response_bytesは1〜67108864で、ダウンロードが成功しなかった場合の本文も含め、メモリに保持するすべてのレスポンス本文に適用します。リクエストヘッダーとデコード後のレスポンスヘッダーには、それぞれ64 KiBの上限があります。Set-Cookieを含め、別々に返されたヘッダー値は保持します。ただし、通信時の元の順序、大文字・小文字、空白は、基盤のHTTPパーサーでは保証されません。HEADでは、通知されたContent-Lengthにかかわらず空のBodyを返します。プロトコルのアップグレードには対応しません。共通の対象はHTTP/1.1です。

クライアントは`Accept-Encoding: identity`を要求します。受信したアプリケーションデータのバイト列は変換しません。identity以外のContent-Encodingは拒否します。ただしHEADでは、転送されない本文を説明するヘッダーなので例外です。チャンク転送のフレームはHTTPトランスポートが処理し、Bodyには含まれません。バイト数の上限はContent-Lengthの宣言だけでなく、読み取り中にも適用します。長さの不一致や不完全な受信はエラーです。

timeout_msは、通信開始からレスポンスの読み取り完了までに共通の経過時間の期限です。追従するリダイレクトも含みます。準備とエンコードはその前に行います。ヘルパーは期限に達すると通信のキャンセルを要求し、レスポンスを閉じ、遅れて発生した非同期の失敗も確認します。OSのDNS処理やキャンセル、ファイルシステムのフラッシュや後始末により、処理が戻るまでに時間がかかる場合があります。厳密なリアルタイム期限やプロセス全体の期限ではありません。運用スーパーバイザーには、ジョブ全体に対する別の期限があります。アプリケーション側の再試行、Cookieの保存、レスポンスのキャッシュ、既定でのWindows資格情報の使用は行いません。送信後のErrorは、サーバー側で副作用が発生しなかったことを保証しません。

max_redirectsが0なら、リダイレクトも通常のレスポンスとして返します。1〜20を指定すると、GETとHEADで、同一オリジン（スキーム、ホスト、実効ポート）内の301/302/303/307/308を指定回数まで追従します。HTTPSからHTTPへの変更を含む別オリジンへのリダイレクトは、追従せずそのレスポンスを返します。追従回数が上限を超えるとErrorになります。POSTなど他のメソッドではリダイレクトを有効にできません。リダイレクトの規則によって送信済みの本文を再送することもありません。TLS証明書の検証は有効なままです。ハンドラーごとにOSのTLS既定値を選び、PowerShell全体のTLS設定は変更しません。

proxy_urlがnullなら直接接続し、環境側のプロキシ、PAC、環境変数の設定は無視します。null以外では、資格情報、`/`以外のパス、クエリ、フラグメントを含まないhttpプロキシのオリジンを指定します。use_windows_credentialsは、現在のアカウントによる接続先へのチャレンジ認証を有効にします。Authorizationヘッダーとは併用できません。proxy_use_windows_credentialsは、現在のアカウントの資格情報によるプロキシへのチャレンジ認証を有効にし、proxy_urlを必要とします。明示したAuthorizationヘッダーにはBearerやBasicの値を渡せます。OAuthフロー、明示的なパスワード保存、PACの自動検出、マルチパートアップロード、ストリーミングアップロード、WebSocket、再開可能なダウンロードのAPIはありません。内部クライアントは、プロキシと認証設定に応じて、上限付きのキャッシュで接続を再利用します。Rukbatにはクライアントやセッションのオブジェクト、dispose操作を公開しません。

ダウンロードには本文のないGETと、正の符号付き64ビット整数のmax_bytesが必要です。完全なファイルを保存先へ配置するのはステータス200の場合だけです。204、206、3xx、2xx以外では、savedがfalse、sizeが0、error_bodyが非nullになります。error_bodyの内容は空の場合もあります。保存に成功した結果には実際のバイト数が入り、error_bodyはnullになります。成功時のファイルには、メモリに保持するレスポンスの上限ではなくmax_bytesを適用します。保存先は存在せず、親ディレクトリは存在し、祖先ディレクトリに再解析ポイントが含まれていない必要があります。相対パスは起動時に記録した作業ディレクトリを基準にします。Content-Dispositionから保存先を決めることはありません。保存先と同じディレクトリに一時ファイルを作り、フラッシュ後、置き換えを行わない移動によって配置します。通常の失敗では一時ファイルを削除し、後始末に失敗すればErrorになります。別の処理が同時に作成した保存先は保持します。ホストを強制終了すると`.rukbat-http-<id>.tmp`が残る場合があります。クラッシュ用のジャーナル、ファイルシステムの永続性、悪意ある親ディレクトリへの保証はありません。既存の保存先は置き換えません。

HTTPは、配備先にインストールされたSystem.Net.HttpとC# 5のヘルパーを使います。Desktopホストには、OSのTLS既定値に対応した.NET Frameworkハンドラー（4.7.1以降）が必要です。配備するスクリプトに依存ライブラリは同梱しません。他のネイティブヘルパーと同様、FullLanguageとAdd-Typeの使用権限が必要です。実際にプロキシやWindows認証を利用できるかは、実行アカウントとネットワーク設定に依存します。

```rukbat
import http
import encoding
import json

options := http.options(30000)
request := http.request(http.method_get, "https://example.com/api/jobs")
response := http.send(request, options)
if response.status == http.status_200_ok {
    document := json.parse(http.text(response.body, encoding.utf8))
    print(json.kind(document))
}
```

<a id="runtime-path-resolution"></a>

## 実行時のパス解決

`rukbat run`を含め、`powershell -File` / `pwsh -File`で直接実行すると、モジュールの初期化前にホストプロセスのカレントディレクトリを記録します。PowerShellの文からスクリプトを呼び出す場合は、そのセッションのファイルシステム上の現在位置を記録します。起動時のディレクトリに含まれる`[`や`]`などの文字は、そのまま文字として扱います。スクリプトのディレクトリは別に保持し、`path.script_dir()`で取得できます。

標準のファイルシステム操作では、空でない相対パスを、起動時に記録した作業ディレクトリを基準に解決してからPowerShellや.NETを呼び出します。絶対パスはそのまま使います。`\data\file.txt`のようなルート相対パスは、記録したディレクトリがあるドライブのルートを基準にします。`C:file.txt`のようなドライブ相対パスは、暗黙のドライブ別状態に依存するため拒否します。この形式に対して`fs.is_file`と`fs.is_dir`は`false`を返します。これらの存在確認関数を除き、不正または空の操作対象パスはErrorになります。読み取り、書き込み、列挙、コピー、移動、作成、削除のErrorの規則は変わりません。

<a id="program-arguments"></a>

## プログラムの引数

`args(): string[]`は、エントリプログラムに渡されたコマンドライン引数を順番どおりに返します。引数がなければ`[]`を返します。引数の宣言的な定義、検証、ヘルプには`cli`を使います。ヘルプを表示するタイミング、終了、実際の操作はプログラム側で決めます。

<a id="cli"></a>

## `cli`

コマンド定義は、変更可能な通常の値型structです。ファクトリー関数はすべてのフィールドを設定します。通常の値コピーの規則が適用されるため、コマンドの配列に入れる前に定義を設定してください。このモジュールは次のstructを公開します。以下の型名にはすべて`cli`の修飾が付きます。

```rukbat
struct Command {
    name: string
    description: string
    arguments: cli.Argument[]
    options: cli.Option[]
    flags: cli.Flag[]
}
struct Argument {
    name: string
    description: string
    required: bool
    repeatable: bool
    allow_empty: bool
    choices: string[]
}
struct Option {
    name: string
    description: string
    short_name: string?
    value_name: string
    required: bool
    default_value: string?
    repeatable: bool
    allow_empty: bool
    choices: string[]
}
struct Flag {
    name: string
    description: string
    short_name: string?
}
```

`cli.Result`は、公開フィールドやコンストラクターを持たない不透明な不変の値です。通常の代入、引数、戻り値、コンテナーに使えます。配列やstructの内部にある場合も含め、出力、等値比較、JSONとの相互変換、PowerShellとの受け渡しには使えません。データを外部へ出す前に、文字列や配列を明示的に読み取ってください。

| シグネチャ | 意味 | 失敗時 |
|---|---|---|
| `cli.command(name: string, description: string): cli.Command` | 引数、オプション、フラグの配列が空のコマンドを作ります。nameは呼び出し方の表示に使う名前で、ホストプロセスからは推測しません。 | 定義の検証はparse/helpで行います。 |
| `cli.argument(name: string, description: string): cli.Argument` | 必須で繰り返し不可の位置引数を作ります。allow_emptyはfalse、choicesは空です。 | 定義の検証はparse/helpで行います。 |
| `cli.option(name: string, description: string): cli.Option` | 省略可能な値付きオプションを作ります。short_name/default_valueはnull、value_nameはカルチャに依存せず大文字にしたname、required/repeatable/allow_emptyはfalse、choicesは空です。 | 定義の検証はparse/helpで行います。 |
| `cli.flag(name: string, description: string): cli.Flag` | 短い名前のない、指定の有無を表すフラグを作ります。 | 定義の検証はparse/helpで行います。 |
| `cli.parse(command: cli.Command, arguments: string[]): cli.Result` | 定義を検証して引数をそのまま解析します。ストリームへの出力、終了、ファイルの読み取り、コマンドの実行は行いません。結果には独立した実効値を保持します。 | 定義や入力が不正ならError。ヘルプ要求は入力エラーを抑制しますが、定義エラーは抑制しません。 |
| `cli.help(command: cli.Command): string` | 定義を検証し、定義順に一定の形式のヘルプを生成します。区切りはLFで、末尾の改行はありません。 | 定義が不正ならError。 |
| `cli.help_requested(result: cli.Result): bool` | オプション終端より前の--helpまたは-hを認識したかどうかを返します。 | なし。 |
| `cli.has(result: cli.Result, name: string): bool` | フラグの指定、または実効値の有無を返します。既定値や、明示的に許可された空の値も含みます。 | 未定義の名前やヘルプモードの結果はError。 |
| `cli.get(result: cli.Result, name: string): string` | 繰り返し不可の引数・オプションの値を1つ読み取ります。 | 未定義の名前、ヘルプモード、フラグ、繰り返し可能な項目、既定値も指定値もない場合はError。 |
| `cli.get_all(result: cli.Result, name: string): string[]` | 繰り返し可能な値を指定順に読み取ります。省略可能な項目が指定されていなければ[]を返します。戻り値の配列は独立しています。 | 未定義の名前、ヘルプモード、フラグ、繰り返し不可の項目はError。 |

定義の名前は`[a-z][a-z0-9]*(-[a-z0-9]+)*`に一致し、先頭に`--`を付けない形式です。引数、オプション、フラグ全体で重複できません。`help`は予約されています。短い名前はnullかASCII小文字1文字で、オプションとフラグ全体で重複できません。`h`は予約されています。コマンド名は空白だけにはできず、制御文字も使えません。オプションのvalue_nameは空にできず、空白や制御文字も使えません。説明にはUnicodeや改行を使えます。

必須の位置引数は、省略可能な位置引数より前に置きます。繰り返し可能な位置引数は最後の引数に限ります。必須なら、少なくとも1つの値が必要です。オプションも必須または繰り返し可能にできます。null以外の既定値を指定できるのは、省略可能かつ繰り返し不可のオプションだけです。既定値はallow_emptyとchoicesの条件を満たす必要があります。選択肢の文字列は重複できず、完全一致で比較します。choicesが空配列なら任意の値を受け入れます。空文字列を選択肢に含めるにはallow_emptyが必要です。空文字列をトリムしたり、既定値で置き換えたりはしません。必須フラグ、否定フラグ、真偽値を指定するフラグ構文はありません。

長い名前の値付きオプションは`--name VALUE`と`--name=VALUE`、短い名前は`-n VALUE`と`-n=VALUE`を受け入れます。トークンの分割に使うのは最初の等号だけです。フラグは値を取りません。オプションと位置引数は混在でき、位置引数の順序は保持します。フラグや繰り返し不可のオプションが重複すると、値が同じでもErrorになります。短い名前のまとめ書き（`-abc`）、値の直結（`-ofile`）、名前の省略、大文字・小文字を区別しない名前、暗黙の否定は受け入れません。

単独の`--`はオプション認識を終了し、値にはなりません。それ以降のトークンはすべて位置引数です。単独の`-`は値です。`-[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?`に一致する負の数の表記も値として扱い、数値への変換や範囲検査は行いません。それ以外のハイフンで始まる値には、`--name=--`を含む`--name=VALUE`形式か、位置引数の前の終端記号が必要です。値を待っているオプションは、次のトークンがハイフンで始まる非数値なら消費せず、値がないエラーを報告します。引用符の解釈、パス解決、glob展開、環境変数・設定の統合、レスポンスファイル、サブコマンドの処理は行いません。数値や用途固有の検証は呼び出し側が担い、必要に応じて既存のテキスト変換操作を使います。

`--`より前にある完全一致の`--help`と`-h`は、それ以前に未定義の入力、重複、値の不足があってもヘルプ要求になります。`--name=--help`は値、`-- --help`は位置引数のデータです。ヘルプの結果に使える問い合わせはhelp_requestedだけで、他の取得関数は拒否します。呼び出し側で、操作の前にヘルプを明示的に出力して終了します。インポートしたモジュールのトップレベル初期化は、ヘルプの分岐より前に実行されます。

入力のErrorは、指定順で最初に問題があった引数を示し、簡単な使用法と`Use --help for details.`を続けます。必須項目の不足はトークンのエラーの後に、定義順（引数、次にオプション）で報告します。未定義の綴りには、1文字の挿入・削除・置換で一致する定義済みの綴りが1つだけあれば、その候補を示す場合があります。候補が複数あれば表示せず、自動適用もしません。定義のErrorは`Invalid CLI definition:`という接頭辞で区別します。生成するヘルプは、引数、値付きオプション、フラグ、組み込みのヘルプフラグの順で、各配列の順序を保持します。説明は次の行から字下げして表示します。必須・繰り返し可能の印、既定値、選択肢も含めます。書式は端末幅やロケールに依存しません。

[コマンドラインプログラム](CLI.md)と[ファイルブラウザーの例（英語）](../../examples/file-browser/options.rukbat)を参照してください。`cli`モジュールにはAdd-Typeの使用権限は不要です。

<a id="test"></a>

## `test`

| シグネチャ | 意味 | 失敗時 |
|---|---|---|
| `test.assert(condition: bool): void` | 条件を一度だけ評価して検証します。 | falseなら、ソース式とソース位置を含むErrorを送出します。プリミティブ値を直接比較した場合は、評価済みの両辺の値も表示します。 |
| `test.fail(message: string): void` | 理由を指定して明示的に失敗させます。 | 指定したメッセージの通常のErrorを必ず送出します。 |

このモジュールは、`temp_dir: string`と`source_dir: string`を持つ通常のstruct、`test.Context`を公開します。`rukbat test`は、この型に完全一致する引数を1つ宣言したテストに値を渡します。`temp_dir`はテストごとに用意する一時作業ディレクトリ、`source_dir`はそのテストのソースファイルがあるディレクトリです。どちらも絶対パスです。

アサーションには、通常のRukbatの型検査、評価順序、数値変換、大文字・小文字を区別する比較、真偽値の短絡評価を適用します。位置引数だけを受け入れ、型引数は指定できません。アサーションのエラーを含め、Errorはcatchできます。`catch`で捕捉して抑制したアサーションは、テストランナーの失敗にはなりません。言語に自動的な深い等値比較は追加しません。配列の長さと要素は明示的に比較してください。このモジュールは、通常の単独実行用の`build`/`run`出力でも使え、配備先にテスト用ランタイムは不要です。

テストの検出、実行、一時的なテストデータ、複数ケース、想定するエラー、コマンドラインの終了コードは、[Rukbatプログラムのテスト](TESTING.md)を参照してください。
