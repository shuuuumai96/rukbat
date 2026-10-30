namespace Rukbat
{
    internal static class LogValues
    {
        public static readonly ValueType Level = new("log.Level");

        internal sealed record LevelConstant(string Name, string Tag, string Label, int Rank)
        {
            public string Documentation => $"The {Label} logging severity. Read-only.";
        }

        // Binding, generation, filtering, and editor members share one severity catalog.
        public static readonly IReadOnlyList<LevelConstant> Levels =
        [
            new("level_debug", "debug", "DEBUG", 0),
            new("level_info", "info", "INFO", 1),
            new("level_warn", "warn", "WARN", 2),
            new("level_error", "error", "ERROR", 3)
        ];
        public static LevelConstant DefaultLevel => Levels[1];

        internal sealed record Operation(string Name, LevelConstant? Level = null);
        public static readonly IReadOnlyList<Operation> Operations =
        [
            new("create"),
            new("debug", Levels[0]),
            new("info", Levels[1]),
            new("warn", Levels[2]),
            new("error", Levels[3]),
            new("enabled")
        ];

        public const string Structures = "struct Logger { stderr_level: Level?; file: string?; file_level: Level }";
    }
}
