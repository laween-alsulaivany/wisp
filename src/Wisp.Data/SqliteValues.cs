using System.Data;
using System.Globalization;
using Dapper;

namespace Wisp.Data;

internal static class SqliteValues
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    static SqliteValues() => SqlMapper.AddTypeHandler(new TimestampHandler());

    internal static DynamicParameters Parameters(object values)
    {
        var parameters = new DynamicParameters();
        foreach (var property in values.GetType().GetProperties())
        {
            var value = property.GetValue(values);
            parameters.Add(property.Name, value switch
            {
                bool flag => flag ? 1 : 0,
                DateTimeOffset timestamp => Format(timestamp),
                Enum kind => kind.ToString(),
                _ => value
            });
        }

        return parameters;
    }

    internal static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private sealed class TimestampHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) =>
            DateTimeOffset.ParseExact((string)value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = Format(value);
        }
    }
}
