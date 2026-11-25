using System.Net;

namespace BlueID.Application.Common;

/// <summary>An expected failure with a stable code (docs/05 section 12); the message is localised from "error.&lt;code&gt;".</summary>
public class AppException(HttpStatusCode status, string code, IReadOnlyDictionary<string, object?>? args = null)
    : Exception(code)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Args { get; } = args;

    public static AppException NotFound(string code) => new(HttpStatusCode.NotFound, code);
    public static AppException Conflict(string code) => new(HttpStatusCode.Conflict, code);
    public static AppException Forbidden(string code = "forbidden") => new(HttpStatusCode.Forbidden, code);
    public static AppException Unprocessable(string code) => new(HttpStatusCode.UnprocessableEntity, code);
}

public sealed record FieldError(string Field, string Code, IReadOnlyDictionary<string, object?>? Args = null);

public sealed class ValidationFailedException(IReadOnlyList<FieldError> errors)
    : AppException(HttpStatusCode.BadRequest, "validation_failed")
{
    public IReadOnlyList<FieldError> Errors { get; } = errors;
}

/// <summary>Opaque cursor pages; the order always ends with the unique key (lesson 9).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, long? Count);
