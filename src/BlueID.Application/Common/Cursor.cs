using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace BlueID.Application.Common;

/// <summary>Opaque keyset cursor: the sort key and the id of the last row (base64url JSON). Products must not parse it.</summary>
public static class Cursor
{
    public sealed record Position(string Key, Guid Id);

    public static string Encode(string key, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Position(key, id))));

    public static Position? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Position>(Base64Url.DecodeFromChars(cursor)) ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new AppException(System.Net.HttpStatusCode.BadRequest, "cursor_invalid");
        }
    }
}
