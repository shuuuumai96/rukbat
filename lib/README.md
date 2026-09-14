# Auvilの旧プロトタイプライブラリ

このディレクトリは、正式な標準モジュール導入前の互換資料です。v0.1の新しいコードでは `import files` と `import json` を使ってください。正式なAPIと動作は [`docs/STDLIB.md`](../docs/STDLIB.md) に定義しています。

以下は旧プロトタイプの説明です。既存のPowerShellコマンドを、Auvilの型付き関数から使う小さなライブラリです。
現在はファイル一覧・テキスト保存・文字列配列のJSON化を提供します。

リポジトリ直下のプログラムから使う場合:

```text
import "./lib/files.auv" as files
import "./lib/json.auv" as json

names: string[] = files.list_files(".", "*.md")
files.write_text("files.json", json.encode_strings(names))
```

自分のプロジェクトへlibディレクトリをコピーしても使えます。importのパスは、それを書いたソースファイルが基準です。コンパイル時にはライブラリの `.auv` が必要ですが、実行時には生成された `.ps1` だけで動きます。グローバルな標準ライブラリ探索やパッケージ管理は行いません。

| モジュール | 関数 | 動作 |
|---|---|---|
| `files.auv` | `list_files(directory: string, pattern: string): string[]` | ディレクトリ直下のファイル名を、Get-ChildItemのFilterで絞り込む |
| `files.auv` | `write_text(path: string, text: string): void` | UTF-8・末尾改行なしで保存する。既存ファイルは上書き |
| `json.auv` | `encode_strings(values: string[]): string` | ConvertTo-Jsonで文字列配列をコンパクトなJSON配列へ変換する |

ファイル操作のパスにはLiteralPathを使うため、ディレクトリ名や保存先に `[`・`]` が含まれていてもワイルドカードとして解釈しません。相対パスは実行時のカレントディレクトリが基準です。

`list_files` は再帰探索せず、ディレクトリ自体も結果に含めません。通常は隠しファイルを含めず、列挙順序は保証しません。patternには通常 `*` や `*.txt` を指定し、絞り込みの細部はPowerShellのファイルシステムFilterに従います。

`write_text` は親ディレクトリを自動作成しません。参照・保存に失敗するとErrorが伝播するため、必要なら呼び出し側でtry/catchを使ってください。上書きは原子的な保存ではなく、保存途中の障害で既存の内容を保つことは保証しません。Windows PowerShell 5.1ではUTF-8 BOMが付き、PowerShell 7では付きません。

`encode_strings` は空配列を `[]`、1要素も `["value"]` として返します。文字列の引用符や制御文字のエスケープはConvertTo-Jsonに任せます。現時点での対象は文字列配列で、任意のstructのJSON化は未対応です。

これらを組み合わせた実行例は `examples/file-browser` です。引数指定・絞り込み・JSON出力・エラー時の終了コードまでを利用できます。
