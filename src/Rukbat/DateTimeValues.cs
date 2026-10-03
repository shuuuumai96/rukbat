namespace Rukbat;

internal static class DateTimeValues
{
    public static readonly ValueType Timestamp = new("datetime.Timestamp");
    public static readonly ValueType Date = new("datetime.Date");
    public static readonly ValueType Duration = new("datetime.Duration");
    public static readonly ValueType Zone = new("datetime.Zone");
    public static readonly IReadOnlyList<ValueType> All = [Timestamp, Date, Duration, Zone];
    public static bool Contains(ValueType type) => All.Contains(type);
    public static bool Ordered(ValueType type) => type == Timestamp || type == Date || type == Duration;
    public static string RuntimeName(ValueType type) => "Rukbat" + type.Name.Split('.')[1];
    public static string Description(ValueType type) => type == Timestamp ? "An immutable UTC instant, with 100-nanosecond precision."
        : type == Date ? "An immutable Gregorian calendar date, without a time or time zone."
        : type == Duration ? "An immutable signed elapsed time, with 100-nanosecond precision."
        : "A validated Windows time zone. Use datetime.utc, datetime.zone(id), or datetime.local_zone().";

    internal sealed record Operation(string Name, ValueType[] Parameters, ValueType Result, string Expression);
    public static readonly IReadOnlyList<Operation> Operations =
    [
        new("now", [], Timestamp, "[RukbatDateTime]::Now()"),
        new("parse", [ValueType.String], Timestamp, "[RukbatDateTime]::Parse($values[0])"),
        new("stringify", [Timestamp], ValueType.String, "[RukbatDateTime]::Stringify($values[0])"),
        new("format", [Timestamp, ValueType.String, Zone], ValueType.String, "[RukbatDateTime]::Format($values[0], $values[1], $values[2])"),
        new("parse_local", [ValueType.String, ValueType.String, Zone], Timestamp, "[RukbatDateTime]::ParseLocal($values[0], $values[1], $values[2])"),
        new("zone", [ValueType.String], Zone, "[RukbatDateTime]::Zone($values[0])"),
        new("local_zone", [], Zone, "[RukbatDateTime]::LocalZone()"),
        new("zone_id", [Zone], ValueType.String, "[RukbatDateTime]::ZoneId($values[0])"),
        new("create_date", [ValueType.Int, ValueType.Int, ValueType.Int], Date, "[RukbatDateTime]::CreateDate($values[0], $values[1], $values[2])"),
        new("date", [Timestamp, Zone], Date, "[RukbatDateTime]::Date($values[0], $values[1])"),
        new("parse_date", [ValueType.String], Date, "[RukbatDateTime]::ParseDate($values[0])"),
        new("stringify_date", [Date], ValueType.String, "[RukbatDateTime]::StringifyDate($values[0])"),
        new("format_date", [Date, ValueType.String], ValueType.String, "[RukbatDateTime]::FormatDate($values[0], $values[1])"),
        new("at", [Date, ValueType.Int, ValueType.Int, ValueType.Int, Zone], Timestamp, "[RukbatDateTime]::At($values[0], $values[1], $values[2], $values[3], $values[4])"),
        new("add_days", [Date, ValueType.Int], Date, "[RukbatDateTime]::AddDays($values[0], $values[1])"),
        new("add_months", [Date, ValueType.Int], Date, "[RukbatDateTime]::AddMonths($values[0], $values[1])"),
        new("milliseconds", [ValueType.Int], Duration, "[RukbatDateTime]::Duration($values[0], [TimeSpan]::TicksPerMillisecond)"),
        new("seconds", [ValueType.Int], Duration, "[RukbatDateTime]::Duration($values[0], [TimeSpan]::TicksPerSecond)"),
        new("minutes", [ValueType.Int], Duration, "[RukbatDateTime]::Duration($values[0], [TimeSpan]::TicksPerMinute)"),
        new("hours", [ValueType.Int], Duration, "[RukbatDateTime]::Duration($values[0], [TimeSpan]::TicksPerHour)"),
        new("days", [ValueType.Int], Duration, "[RukbatDateTime]::Duration($values[0], [TimeSpan]::TicksPerDay)"),
        new("add", [Timestamp, Duration], Timestamp, "[RukbatDateTime]::Add($values[0], $values[1])"),
        new("between", [Timestamp, Timestamp], Duration, "[RukbatDateTime]::Between($values[0], $values[1])"),
        new("total_seconds", [Duration], ValueType.Float, "[RukbatDateTime]::TotalSeconds($values[0])"),
        new("from_unix_seconds", [ValueType.Int], Timestamp, "[RukbatDateTime]::FromUnixSeconds($values[0])"),
        new("from_unix_milliseconds", [ValueType.Int], Timestamp, "[RukbatDateTime]::FromUnixMilliseconds($values[0])"),
        new("to_unix_seconds", [Timestamp], ValueType.Int, "[RukbatDateTime]::ToUnixSeconds($values[0])"),
        new("to_unix_milliseconds", [Timestamp], ValueType.Int, "[RukbatDateTime]::ToUnixMilliseconds($values[0])")
    ];
}
