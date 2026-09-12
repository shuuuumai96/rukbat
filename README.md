# Auvil

**Auvil（オーヴィル）** は、PowerShellへコンパイルする静的型付きの小さな言語です。
変数の `$` をなくし、普通の関数呼び出しとファイル単位のモジュールを使えます。
ソースは `.auv`、生成物は実行に必要なAuvilモジュールを含む1つの `.ps1` です。

## 書き方

`math.auv`:

```text
function add(a: int, b: int): int {
    return a + b
}
```

`hello.auv`:

```text
import math

name: string = "Taro"
age: int = 30
result: int = math.add(age, 2)

print(name)
print(result)

if result >= 20 {
    print("adult")
}

year: string = powershell<string>("Get-Date", Format: "yyyy")
print(year)
```

`powershell<string>(...)` はPowerShellのコマンドを呼び、結果が1つの文字列であることを実行時に検証します。
戻り値が不要なら `powershell<void>(...)` を使います。

## 実行

ビルドには .NET 8 SDK、生成スクリプトの実行には PowerShell 7.4以降が必要です。
初回の依存パッケージ復元にはネットワーク接続が必要です。

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --no-restore --project src/Auvil -- build examples/hello.auv
pwsh -NoProfile -File examples/hello.ps1
```

Windowsではリポジトリのラッパーも使えます。

```powershell
.\auvil build examples/hello.auv
# このディレクトリをPATHに追加した場合:
auvil build examples/hello.auv
# build専用の短縮コマンドも利用できます:
.\build examples/hello.auv
```

出力先はエントリーファイルと同じディレクトリの同名 `.ps1` です。
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
- モジュールごとに変数・関数を分離し、共通の依存モジュールは1回だけ初期化します。
- Auvilの関数は明示的な `return` の値だけを返します。途中の式の値は捨てます。
- `print(value)` は画面出力です。関数の戻り値に混ざりません。
- PowerShellとの接続は `powershell<T>(...)` に集約します。

**初期プロトタイプのPowerShellスーパーセット方針から変更しています。**
以前の `$name: string` やPowerShellのコマンド記法をそのまま書いた `.auv` は受け付けません。
既存 `.ps1` の構文互換性より、Auvilとしての書き味と動作の一貫性を優先します。

現在の型は `int`、`string`、`bool`、戻り値用の `void` です。
型推論・オブジェクト型・union型・配列・パイプライン解析・ループ・パッケージ管理は実装していません。
詳細な構文、モジュールの規則、制限は [言語仕様](docs/language.md) を参照してください。

## 実装と検証

`Lexer → Parser → AST → ModuleLoader / TypeChecker → Emitter` の構成です。
独自の字句解析と再帰下降パーサーで構文を解析し、AST上で名前と型を検査します。
生成PowerShellの構文検証には公式 `System.Management.Automation` を使います。
regex置換や入力スクリプトの実行には依存しません。

```powershell
dotnet test
```

テストには構文・型・診断位置・CLIに加え、生成した `.ps1` を実際に `pwsh` で実行する検証を含みます。
`pwsh` をPATHに用意してください。実行テストを黙ってスキップすることはありません。
明示return、スコープ、PowerShellとの型境界、共有モジュールの初期化、ソースファイルなしでの実行を検証します。
