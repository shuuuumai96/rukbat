# Rukbat Architecture

Rukbatは、ソースを検証してからPowerShellを生成します。

```text
Source
  ↓
Lexer
  ↓
Parser
  ↓
Syntax Tree / AST
  ↓
Module loading and name resolution
  ↓
Type checking
  ↓
PowerShell generation
```

各段階の責務は分けます。

- Lexerは文字列、コメント、演算子をトークンへ分割する。
- Parserは構文木を作る。型検査や名前解決はしない。
- ModuleLoaderはimportを解決し、依存順序と循環を検査する。
- TypeCheckerは名前、スコープ、再宣言、型、暗黙の数値拡張、nullableのフロー絞り込み、関数呼び出し、制御文の意味を検査する。
- Emitterは検証済みの構文木と型情報から、単体で実行できるWindows PowerShell 5.1互換の `.ps1` を生成する。意味の推論や名前解決はしない。
- Compilerは各段階を順に呼び、生成物をPowerShell構文として検証する。
- `rukbat check` は ModuleLoader と TypeChecker までで停止し、EmitterもPowerShellパーサーも呼ばない。
- Formatterは構文を検証した後、コメントを保持したまま字句列を正規化する。型検査やimport解決は行わない。

生成スクリプトには、Rukbatの値コピー・配列境界・エラー伝播・PowerShell境界を実現する小さなPowerShellコードを含めます。標準ライブラリの実装はimportされた機能群だけを含めます。実行先にRukbatコンパイラや別のRukbatランタイムは必要ありません。
