using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlueID.Infrastructure.Persistence;

/// <summary>Enums are stored as SNAKE_UPPER text (ACTIVE, LDAP_SYNC), the spelling of PROMPT 4 and of the API.</summary>
public static class EnumConversions
{
    public static PropertyBuilder<TEnum> UpperEnum<TEnum>(this PropertyBuilder<TEnum> p) where TEnum : struct, Enum =>
        p.HasConversion(new UpperEnumConverter<TEnum>());

    public static PropertyBuilder<TEnum?> UpperEnum<TEnum>(this PropertyBuilder<TEnum?> p) where TEnum : struct, Enum =>
        p.HasConversion(new UpperEnumConverter<TEnum>()!);

    public static string ToUpperName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());
}

public sealed class UpperEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => EnumConversions.ToUpperName(v),
    s => Parse(s)) where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ByName =
        Enum.GetValues<TEnum>().ToDictionary(v => EnumConversions.ToUpperName(v), v => v, StringComparer.Ordinal);

    private static TEnum Parse(string s) =>
        ByName.TryGetValue(s, out var v) ? v : throw new InvalidOperationException($"Unknown {typeof(TEnum).Name} value '{s}'.");
}
