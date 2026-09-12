# Auvil

`.auv` の明示的な型注釈をチェックし、通常の `.ps1` に変換する C# / .NET 8 CLI。
コンパイル中に入力スクリプトを実行することはありません。

言語名は **Auvil（オーヴィル）**、ソース拡張子は **`.auv`** です。
`hello.auv` をコンパイルすると `hello.ps1` を生成します。

## 実行

.NET 8 SDK が必要です。初回は NuGet から依存パッケージを復元します。

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --project src/Auvil -- build examples/hello.auv
pwsh -NoProfile -File examples/hello.ps1
dotnet test
```

Windows 用の短縮コマンドもあります。

```powershell
.\auvil build examples/hello.auv
# このディレクトリを PATH に追加した場合:
auvil build examples/hello.auv
```

出力先は入力ファイルと同じディレクトリの同名 `.ps1` です。
成功時は既存出力を上書きします。型・構文エラー時は出力せず、既存出力も変更しません。
古い出力が残る場合があるため、自動実行では必ず終了コードを確認してください。
終了コードは成功 `0`、コンパイルエラー `1`、引数・入出力エラー `2` です。
診断は `ファイル(行,列): error AUVxxxx: 内容` の形式で標準エラーに出します。

配布用ビルド:

```powershell
dotnet publish src/Auvil -c Release -o artifacts/auvil
.\artifacts\auvil\auvil.exe build examples/hello.auv
```

## 構文と変換

```powershell
$name: string = "Taro"
$age: int = 30
function Add($a: int, $b: int): int {
    return $a + $b
}
```

概ね次の PowerShell に変換します。

```powershell
[string]$name = "Taro"
[int]$age = 30
function Add {
    [OutputType([int])]
    param([int]$a, [int]$b)
    return $a + $b
}
```

対応型は `string`、`int`、`bool`（大文字小文字を区別しません）。
変数宣言は `$name: 型 = 式`、引数は `$name: 型`、戻り値は `): 型 {` です。
変数とコロンは隣接させ、コロンと型の間には空白を入れてください。
`$env:PATH` や `$script:name` などの通常のスコープ付き変数と区別するため、
`$name:int` は型注釈として解釈しません。

型注釈は省略できます。注釈のない PowerShell は原文のまま出力します。
PowerShell の標準構文を土台に追加構文だけを処理するスーパーセット設計です。
対応する標準構文の基準は、使用している PowerShell 7.4 パーサーです。

## 設計

- `Syntax.cs`: 公式 PowerShell トークナイザーでコメント・文字列・コードを区別。
  型注釈を独立した構文ノードとして抽出し、位置を保ったまま注釈を空白にして公式 AST を構築。
- `TypeChecker.cs`: 明示型のスコープ表を作り、AST 上の代入、引数の既定値、明示的な `return`、
  同一スコープ内の単純な関数呼び出しの位置引数・完全名の名前付き引数を検査。
- `Emitter.cs`: ソース位置に基づく編集で型を変換。戻り値注釈のある関数はヘッダー引数を
  本体の `param` に移し、`OutputType` 属性を付与。
- `Compiler.cs`: 各段階を実行し、生成した PowerShell の構文も再検証。
- `Program.cs`: CLI とファイル入出力。コンパイラ本体は文字列を入出力する独立 API。

regex 置換は使用していません。標準 PowerShell の文法は公式の
[System.Management.Automation](https://www.nuget.org/packages/System.Management.Automation/7.4.19)
に委譲し、拡張構文・型検査・出力処理を分離しています。

## 検査範囲と制限

明示型と、リテラル・明示型変数・括弧・限定的な算術式の型を比較します。
たとえば `$age: int = "30"` は、PowerShell 自体が暗黙変換できてもコンパイルエラーです。
注釈はスコープ全体に適用します。同一スコープの矛盾した再宣言もエラーです。
型なしのローカル変数・引数は外側の同名変数を隠します。

次は実装していません。

- 型なし変数の型推論、オブジェクト型、union 型。
- パイプライン解析、コマンド出力の型決定、暗黙の関数出力の解析。
- 全経路での戻り値の存在、到達可能性、数値のオーバーフローや型昇格の解析。
- 動的呼び出し、スプラッティング、引数名の省略、完全な PowerShell 引数バインド。
- 複合代入、動的スコープや実行順序の完全なモデル化。

解析対象外の式は「型不明」として許可します。完全な静的型安全性を保証するものではありません。
`OutputType` は戻り値のメタデータであり、実行時に戻り値を強制する仕組みではありません。
`return` の明らかな型不一致はコンパイラで検出しますが、暗黙の出力や戻り値の欠落は検査しません。
変数・引数の `[型]` には PowerShell 本来の実行時変換が適用されます。

テストには正常変換、型エラー、元ソースの診断位置、スコープ、通常構文の保持、CLI の出力制御を含みます。
