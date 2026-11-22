namespace Rukbat
{
    // Central catalog of module names, standard operation IDs, resource names, and operator tokens
    // shared across CodeGeneration so choices are defined in one place rather than scattered literals.
    internal static class RuntimeCatalog
    {
        public const char OperationSeparator = '.';
        public const string SelfIdentifier = "self";

        public static class Modules
        {
            public const string Fs = "fs";
            public const string Path = "path";
            public const string Csv = "csv";
            public const string Json = "json";
            public const string Env = "env";
            public const string Process = "process";
            public const string Text = "text";
            public const string Test = "test";
            public const string Encoding = "encoding";
            public const string Math = "math";
            public const string DateTime = "datetime";
            public const string Array = "array";
            public const string Zip = "zip";
            public const string Hash = "hash";
            public const string Http = "http";
            public const string Cli = "cli";
            public const string Log = "log";
            public const string Re = "re";
        }

        public static class Operations
        {
            public const string JsonParse = "json.parse";
            public const string JsonParseStrict = "json.parse_strict";
            public const string JsonAs = "json.as";
            public const string JsonStringify = "json.stringify";
            public const string JsonSet = "json.set";
            public const string JsonSetAt = "json.set_at";
            public const string JsonAppend = "json.append";

            public const string CsvRecords = "csv.records";
            public const string CsvOpenRecords = "csv.__open_records";
            public const string CsvRows = "csv.rows";
            public const string CsvOpenRows = "csv.__open_rows";
            public const string CsvParseTable = "csv.parse_table";
            public const string CsvTable = "csv.table";
            public const string CsvRow = "csv.row";
            public const string CsvStringify = "csv.stringify";
            public const string CsvStringifyTable = "csv.stringify_table";
            public const string CsvHasColumn = "csv.has_column";

            public const string FsStat = "fs.stat";
            public const string FsReadText = "fs.read_text";
            public const string FsLines = "fs.lines";
            public const string FsOpenLines = "fs.__open_lines";
            public const string FsWriteText = "fs.write_text";
            public const string FsWalkFiles = "fs.walk_files";
            public const string FsOpenWalk = "fs.__open_walk";
            public const string FsTryLock = "fs.try_lock";
            public const string FsOpenLock = "fs.__open_lock";
            public const string FsStageText = "fs.stage_text";
            public const string FsOpenStage = "fs.__open_stage";
            public const string FsWriteChunk = "fs.write_chunk";
            public const string FsCommit = "fs.commit";
            public const string FsMoveFilesBatch = "fs.move_files_batch";
            public const string FsRecoverFileMoves = "fs.recover_file_moves";
            public const string FsCreateTempFile = "fs.create_temp_file";
            public const string FsCreateTempDir = "fs.create_temp_dir";

            public const string EncodingParse = "encoding.parse";
            public const string EncodingFromCodePage = "encoding.from_code_page";
            public const string EncodingName = "encoding.name";

            public const string ZipOptions = "zip.options";
            public const string ZipList = "zip.list";
            public const string ZipExtract = "zip.extract";

            public const string ProcessOptions = "process.options";
            public const string ProcessRun = "process.run";
            public const string ProcessRunToFiles = "process.run_to_files";

            public const string ReFind = "re.find";
            public const string ReFindAll = "re.find_all";

            public const string LogCreate = "log.create";
            public const string TestAssert = "test.assert";
            public const string TestFail = "test.fail";

            public const string TextLength = "text.length";
            public const string TextSlice = "text.slice";
            public const string TextIndexOf = "text.index_of";
            public const string TextUpper = "text.upper";
            public const string TextLower = "text.lower";
            public const string TextTrim = "text.trim";
            public const string TextJoin = "text.join";
            public const string TextParseEnum = "text.parse_enum";
            public const string TextFromEnum = "text.from_enum";

            public const string PathJoin = "path.join";

            public const string ArraySort = "array.sort";
            public const string ArraySortDescending = "array.sort_descending";
            public const string ArraySortBy = "array.sort_by";
            public const string ArraySortByDescending = "array.sort_by_descending";
        }

        public static class Native
        {
            public const string CoreBundle = "CoreNative";
            public const string DateTime = "DateTime";
            public const string FileScopes = "FileScopes";
            public const string MoveBatch = "MoveBatch";
            public const string TemporaryEntries = "TemporaryEntries";
            public const string FileText = "FileText";
            public const string ProcessJob = "ProcessJob";
            public const string Hash = "Hash";
            public const string Regex = "Regex";
            public const string Csv = "Csv";
            public const string Json = "Json";
            public const string JsonProbeType = "RukbatJsonValue";
            public const string Text = "Text";
            public const string Array = "Array";
            public const string CodePageCodec = "CodePageCodec";
            public const string CodePageCodecType = "RukbatCodePageCodec";
            public const string Log = "Log";
            public const string Zip = "Zip";
            public const string ZipAssembly = "System.IO.Compression";
            public const string Http = "Http";
            public const string HttpAssembly = "System.Net.Http";
        }

        public static class Resources
        {
            public const string EntryContext = "EntryContextRuntime";
            public const string Core = "CoreRuntime";
            public const string Dictionary = "DictionaryRuntime";
            public const string IntegerDivision = "IntegerDivisionRuntime";
            public const string PowerShell = "PowerShellRuntime";
            public const string JsonDecode = "JsonDecodeRuntime";
            public const string JsonEncode = "JsonEncodeRuntime";
            public const string PathContext = "PathContextRuntime";
            public const string PathValue = "PathValueRuntime";
            public const string FileTextHelpers = "FileTextHelpers";
            public const string Files = "FilesRuntime";
            public const string Directories = "DirectoriesRuntime";
            public const string FileScopes = "FileScopesRuntime";
            public const string Path = "PathRuntime";
            public const string Csv = "CsvRuntime";
            public const string Cli = "CliRuntime";
            public const string CliHelpers = "CliHelpers";
            public const string JsonValue = "JsonValueRuntime";
            public const string Environment = "EnvironmentRuntime";
            public const string Process = "ProcessRuntime";
            public const string Hash = "HashRuntime";
            public const string Regex = "RegexRuntime";
            public const string Log = "LogRuntime";
            public const string LogHelpers = "LogHelpers";
            public const string Zip = "ZipRuntime";
            public const string Http = "HttpRuntime";
            public const string Text = "TextRuntime";
            public const string Array = "ArrayRuntime";
            public const string Math = "MathRuntime";
        }

        public static class Operators
        {
            public const string Add = "+";
            public const string Subtract = "-";
            public const string Multiply = "*";
            public const string Divide = "/";
            public const string Modulo = "%";
            public const string Not = "!";
            public const string Equal = "==";
            public const string NotEqual = "!=";
            public const string Less = "<";
            public const string LessOrEqual = "<=";
            public const string Greater = ">";
            public const string GreaterOrEqual = ">=";
            public const string And = "&&";
            public const string Or = "||";
        }

        public static readonly IReadOnlySet<string> NativeTextOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.TextLength, Operations.TextSlice, Operations.TextIndexOf,
            Operations.TextUpper, Operations.TextLower, Operations.TextTrim
        };

        public static readonly IReadOnlySet<string> NativeArrayOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.ArraySort, Operations.ArraySortDescending,
            Operations.ArraySortBy, Operations.ArraySortByDescending
        };

        public static readonly IReadOnlySet<string> FileTextOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.FsReadText, Operations.FsLines, Operations.FsOpenLines,
            Operations.CsvRecords, Operations.CsvOpenRecords, Operations.CsvRows, Operations.CsvOpenRows
        };

        public static readonly IReadOnlySet<string> FileScopeOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.FsWriteText, Operations.FsWalkFiles, Operations.FsOpenWalk,
            Operations.FsTryLock, Operations.FsOpenLock,
            Operations.FsStageText, Operations.FsOpenStage, Operations.FsWriteChunk, Operations.FsCommit
        };

        public static readonly IReadOnlySet<string> MoveBatchOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.FsMoveFilesBatch, Operations.FsRecoverFileMoves
        };

        public static readonly IReadOnlySet<string> TemporaryEntryOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.FsCreateTempFile, Operations.FsCreateTempDir
        };

        public static readonly IReadOnlySet<string> EncodingSelectorModules = new HashSet<string>(StringComparer.Ordinal)
        {
            Modules.Fs, Modules.Csv, Modules.Process, Modules.Zip, Modules.Hash, Modules.Http
        };

        public static readonly IReadOnlySet<string> EncodingSelectorOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.EncodingParse, Operations.EncodingFromCodePage
        };

        public static readonly IReadOnlySet<string> PathContextModules = new HashSet<string>(StringComparer.Ordinal)
        {
            Modules.Fs, Modules.Path, Modules.Csv, Modules.Process, Modules.Zip, Modules.Hash, Modules.Http, Modules.Log
        };

        public static readonly IReadOnlySet<string> JsonDecodeOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.JsonParse, Operations.JsonParseStrict, Operations.JsonAs
        };

        public static readonly IReadOnlySet<string> JsonMutationOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.JsonSet, Operations.JsonSetAt, Operations.JsonAppend
        };

        public static readonly IReadOnlySet<string> ReadOnlyStandardCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.ZipList, Operations.ZipExtract,
            Operations.ProcessRun, Operations.ProcessRunToFiles,
            Operations.TextJoin, Operations.PathJoin,
            Operations.CsvStringify, Operations.CsvRow,
            Operations.FsMoveFilesBatch,
            Operations.JsonSet, Operations.JsonSetAt, Operations.JsonAppend
        };

        // Subset of DirectStandardCalls that still forward through $__rukbat_standard at runtime.
        public static readonly IReadOnlySet<string> DelegatingDirectStandardCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            Operations.ReFind, Operations.ReFindAll,
            Operations.CsvStringifyTable, Operations.CsvHasColumn
        };

        public static readonly IReadOnlySet<string> IntegerDivisionOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            Operators.Divide, Operators.Modulo
        };

        public static readonly IReadOnlySet<string> EqualityOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            Operators.Equal, Operators.NotEqual
        };
    }
}
