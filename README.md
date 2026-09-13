# Auvil

**Auvil（オーヴィル）** は、PowerShellへコンパイルする静的型付きの小さな言語です。
変数の `$` をなくし、普通の関数呼び出しとファイル単位のモジュールを使えます。
ソースは `.auv`、生成物は実行に必要なAuvilモジュールを含む1つの `.ps1` です。

## 書き方

`math.auv`:

```text
fn add(a: int, b: int): int {
    return a + b
}
```

`hello.auv`:

```text
import math

struct User {
    name: string
    age: int

    fn is_adult(): bool {
        return self.age >= 20
    }
}

user: User = User(
    name: "Taro",
    age: 30
)

result: int = math.add(user.age, 2)
print(user.name)
print(result)

if user.is_adult() {
    print("adult")
}

year: string = powershell<string>("Get-Date", Format: "yyyy")
print(year)
```

`powershell<string>(...)` はPowerShellのコマンドを呼び、結果が1つの文字列であることを実行時に検証します。
戻り値が不要なら `powershell<void>(...)` を使います。

## 実行

ビルドには .NET 8 SDK、生成スクリプトの実行には Windows PowerShell 5.1 以降が必要です。
初回の依存パッケージ復元にはネットワーク接続が必要です。

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --no-restore --project src/Auvil -- run examples/hello.auv
```

Windowsではリポジトリのラッパーも使えます。

```powershell
.\auvil run examples/import-data.auv
# このディレクトリをPATHに追加した場合:
auvil run examples/import-data.auv
# .ps1への変換だけ行う場合:
.\auvil build examples/hello.auv
# build専用の短縮コマンドも利用できます:
.\build examples/hello.auv
```

`run` は型検査・コンパイルに成功した場合だけ、生成した `.ps1` を `powershell -NoLogo -NoProfile -File` で実行します。
実行時は呼び出し元のカレントディレクトリと標準入出力を引き継ぎ、PowerShellプロセスの終了コードを返します。
コンパイルエラー時は、古い `.ps1` が残っていても実行しません。PATH上に Windows PowerShell の `powershell` が必要です。

`auvil run input.auv -- 引数...` で、`--` より後ろを実行するプログラムへ渡せます。
Auvil側は `args(): string[]` で取得します。引数がなければ空配列です。
生成済みの `.ps1` を直接実行する場合は `powershell -File input.ps1 引数...` です。

`build` と `run` の出力先はエントリーファイルと同じディレクトリの同名 `.ps1` です。
成功時は上書きします。構文・型・importエラー時は出力せず、既存の出力も変更しません。
古い出力が残る場合があるため、自動化ではコンパイラの終了コードを確認してください。
成功は `0`、コンパイルエラーは `1`、CLI引数・入出力エラーは `2` です。
エラーは `ファイル(行,列): error AUVxxxx: 内容` の形式です。import先のエラーにはそのファイルの位置を表示します。
コンパイル時にAuvilやPowerShellのユーザーコードを実行することはありません。

配布用CLI:

```powershell
dotnet publish src/Auvil -c Release -o artifacts/auvil
.\artifacts\auvil\auvil.exe build examples/hello.auv
```

## 方針と現在の範囲

- 読み書きしやすい一貫した構文を優先します。
- `import math` は読み込み元と同じディレクトリの `math.auv` を読み込みます。
- `import "./models/user.auv" as model` でディレクトリをまたいで読み込めます。パスはimportを書いたファイルが基準です。
- モジュールごとに変数・関数を分離し、共通の依存モジュールは1回だけ初期化します。
- Auvilの関数は明示的な `return` の値だけを返します。途中の式の値は捨てます。
- `print(value)` は画面出力です。関数の戻り値に混ざりません。
- PowerShellとの接続は `powershell<T>(...)` に集約します。

**初期プロトタイプのPowerShellスーパーセット方針から変更しています。**
以前の `$name: string` やPowerShellのコマンド記法をそのまま書いた `.auv` は受け付けません。
既存 `.ps1` の構文互換性より、Auvilとしての書き味と動作の一貫性を優先します。

現在の型は `int`、`string`、`bool`、戻り値用の `void`、組み込みの `Error`、ユーザー定義の `struct` と型付き配列 `T[]` です。
structは名前付き引数で生成し、フィールド操作と内部の `fn` メソッドを利用できます。
代入・引数・戻り値ではネストしたフィールドまで値コピーします。メソッドの `self` は呼び出した変数を更新します。
`examples/value-semantics.auv` でコピー元が変わらない動作を確認できます。
通常関数とstructメソッドは `fn` で定義します。`func` と `function` は受け付けません。
配列リテラル `[1, 2]`、添字の読み書き、`.length`、`for item in items`、`break`、`continue` を使えます。
`powershell<User[]>` で外部の複数オブジェクトを型検査して取り込めます。配列もネストした要素まで値コピーします。
型推論付き宣言は `name := "Taro"` と書きます。`:=` は新規宣言だけに使え、既存変数への代入には使えません。
汎用オブジェクト型・union型・パイプライン解析・パッケージ管理は実装していません。
詳細な構文、モジュールの規則、制限は [言語仕様](docs/LANGUAGE.md) を参照してください。

## ディレクトリで処理を分ける

```text
app/
├── main.auv
├── models/
│   └── user.auv
└── services/
    └── users.auv
```

`main.auv`:

```text
import "./models/user.auv" as model
import "./services/users.auv" as users

user: model.User = users.load()
print(user.name)
```

`services/users.auv` からは `import "../models/user.auv" as model` で共通の型を使えます。
文字列パスには `.auv` と `as 別名` が必要です。区切りにはWindowsでも `/` を使います。
`import math` と `import math as calc` も使えます。
同じファイルは別名や異なる相対パスで参照しても、正規化した絶対パスで識別して1回だけ初期化します。
生成物は従来どおり1つの `.ps1` です。

動作するサンプル:

```powershell
.\auvil.cmd run examples/app/main.auv
```

## 作れるCLIの例: ファイル一覧をJSONへ出力

`examples/file-browser` は、引数で指定したディレクトリ直下のファイルを絞り込み、名前の一覧をJSON配列で出力するCLIです。
エントリーファイル、引数の検査、一覧取得、共通ライブラリを別モジュールに分けています。

```powershell
# README.mdなど、直下のMarkdownファイル名を標準出力へ
.\auvil.cmd run examples/file-browser/main.auv -- . --pattern '*.md'
# JSONファイルへ保存
.\auvil.cmd run examples/file-browser/main.auv -- . --pattern '*.md' --output files.json
# ヘルプ
.\auvil.cmd run examples/file-browser/main.auv -- --help
```

```json
["README.md"]
```

最初の引数にディレクトリを指定します。`--pattern` は省略すると `*` です。PowerShellのファイルシステムFilterを利用し、サブディレクトリを再帰的には調べません。通常は隠しファイルを含めず、名前の並び順は保証しません。
一致するファイルが0件なら `[]`、1件でもJSON配列になります。通常の標準出力にはJSONだけを出します。
`--output` の指定時は標準出力を空にして保存します。テキストのエンコーディングとBOM方針は、標準ライブラリ仕様では未確定です。
パスは実行時のカレントディレクトリが基準です。空白のあるパスやパターンは引用符で囲んでください。
引数の不足・不明なオプション・ディレクトリの参照失敗・保存失敗は、説明を標準エラーへ出し、終了コード1になります。成功とヘルプ表示は0です。

`lib/files.auv` はファイル一覧取得とテキスト保存、`lib/json.auv` は文字列配列のJSON化を型付き関数として提供します。別のAuvilプログラムからも相対importで利用できます。
これらはリポジトリに含めた小さなライブラリです。自分のプロジェクトへコピーして使うこともでき、コンパイル後は生成された一つの `.ps1` に含まれます。詳しいAPIは [ライブラリの説明](lib/README.md) を参照してください。

## 配列・ループと外部データ

`examples/arrays.auv` は配列の更新とループ、`examples/import-data.auv` はJSONからのstruct配列の取り込みを実行します。

```powershell
.\auvil.cmd run examples/import-data.auv
```

```text
struct User {
    name: string
    age: int
}

users: User[] = powershell<User[]>(
    "ConvertFrom-Json",
    '[{"name":"Taro","age":30},{"name":"Hanako","age":25}]'
)
for user in users {
    print(user.name)
}
```

フィールドの欠損・null・型違いは、取り込み時に位置とフィールド名を含むエラーになります。
配列は固定長です。追加・削除や、structのPowerShellへの送出は未対応です。

## エラーから回復する

`try / catch / throw` と、`message: string` を持つ組み込みの `Error` を使えます。

```text
try {
    throw Error(message: "処理に失敗しました")
} catch err {
    print(err.message)
}
```

PowerShellのエラーや型の取り込み失敗も同じcatchで扱います。
`throw err` で再送出すると最初の発生位置を保ちます。
未処理のエラーはAuvilのファイル・行と説明を標準エラーへ出し、終了コード1で停止します。
`powershell<T>` で直接呼ぶ外部実行ファイルは終了コード0を成功、それ以外をErrorとします。
初期仕様はcatch一つに限定し、型別catch・finally・throws宣言はありません。

```powershell
.\auvil.cmd run examples/errors.auv
```

## 実装と検証

`Lexer → Parser → AST → ModuleLoader / TypeChecker → Emitter` の構成です。
独自の字句解析と再帰下降パーサーで構文を解析し、AST上で名前と型を検査します。
生成PowerShellの構文検証には公式 `System.Management.Automation` を使います。
regex置換や入力スクリプトの実行には依存しません。

```powershell
dotnet test
```

テストには構文・型・診断位置・CLIに加え、生成した `.ps1` を実際に Windows PowerShell 5.1 と PowerShell 7 で実行する検証を含みます。
`powershell` と `pwsh` をPATHに用意してください。実行テストを黙ってスキップすることはありません。
structの値コピーとself更新、明示return、スコープ、PowerShellとの型境界、共有モジュールの初期化、ソースファイルなしでの実行を検証します。
