namespace Rukbat
{
    internal sealed record StandardConstant(string Name, ValueType Type, object Definition);
    internal sealed record StandardConstantGroup(string Description, IReadOnlyDictionary<string, StandardConstant> Values);

    internal static class StandardConstants
    {
        private static StandardConstantGroup Group(string description, IEnumerable<StandardConstant> values) =>
            new(description, values.ToDictionary(value => value.Name, StringComparer.Ordinal));

        private static readonly IReadOnlyDictionary<string, StandardConstantGroup> groups =
            new Dictionary<string, StandardConstantGroup>(StringComparer.Ordinal)
            {
                ["log"] = Group("logging level", LogValues.Levels.Select(value =>
                    new StandardConstant(value.Name, LogValues.Level, value))),
                ["http"] = Group("HTTP constant", HttpValues.Constants.Select(value =>
                    new StandardConstant(value.Name, value.Type, value))),
                ["encoding"] = Group("encoding value", EncodingValues.All.Select(value =>
                    new StandardConstant(value.Name, ValueType.Encoding, value))),
                ["hash"] = Group("hash algorithm", HashValues.All.Select(name =>
                    new StandardConstant(name, HashValues.Algorithm, name))),
                ["datetime"] = Group("datetime constant", DateTimeValues.Zones.Select(value =>
                    new StandardConstant(value.Name, DateTimeValues.Zone, value)))
            };

        public static StandardConstantGroup? ForModule(string moduleName) => groups.GetValueOrDefault(moduleName);
    }
}
