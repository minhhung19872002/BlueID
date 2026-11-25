using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueID.Application.Common;
using BlueID.Infrastructure.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging;

namespace BlueID.Application.Web;

/// <summary>Response envelope of BlueID's own APIs (PROMPT 5, docs/05 8.1). SCIM and OIDC use their standards' shapes.</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, string? Message, IReadOnlyList<ApiError> Errors);

public sealed record ApiError(string Code, string? Field, string Message);

public static class ApiResponses
{
    public static ApiResponse<T> Ok<T>(T data) => new(true, data, null, []);

    public static ApiResponse<object> Error(HttpContext http, string code, IReadOnlyDictionary<string, object?>? args = null, IReadOnlyList<FieldError>? fields = null)
    {
        // The error middleware runs outside request localisation; the request's culture stays on the context.
        var culture = http.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture ?? CultureInfo.CurrentUICulture;
        string Text(string c, IReadOnlyDictionary<string, object?>? a) =>
            JsonStringLocalizerFactory.Format(JsonStringLocalizerFactory.Get("error." + c, culture), culture, a);
        var errors = fields is { Count: > 0 }
            ? fields.Select(f => new ApiError(f.Code, f.Field, Text(f.Code, f.Args))).ToList()
            : [new ApiError(code, null, Text(code, args))];
        return new ApiResponse<object>(false, null, Text(code, args), errors);
    }

    public static Task WriteErrorAsync(HttpContext http, HttpStatusCode status, string code, IReadOnlyDictionary<string, object?>? args = null,
        IReadOnlyList<FieldError>? fields = null)
    {
        http.Response.StatusCode = (int)status;
        http.Response.ContentType = "application/json; charset=utf-8";
        return http.Response.WriteAsync(JsonSerializer.Serialize(Error(http, code, args, fields), WebJson.Options));
    }
}

public static class WebJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        o.Converters.Add(new CleanStringConverter());
        return o;
    }
}

/// <summary>
/// U+0000 and broken UTF-16 never get past the door (PROMPT 7, lesson 10): NULs are dropped, lone surrogates (what
/// invalid UTF-8 decodes to after replacement is U+FFFD and is fine) fail the request.
/// </summary>
public sealed class CleanStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is null)
        {
            return null;
        }

        if (!Infrastructure.Text.Identifiers.IsWellFormed(s))
        {
            throw new JsonException("invalid_characters");
        }

        return s.Contains('\0', StringComparison.Ordinal) ? s.Replace("\0", "", StringComparison.Ordinal) : s;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

/// <summary>Maps exceptions to the error envelope.</summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (ValidationFailedException ex) when (!http.Response.HasStarted)
        {
            await ApiResponses.WriteErrorAsync(http, ex.Status, ex.Code, ex.Args, ex.Errors);
        }
        catch (AppException ex) when (!http.Response.HasStarted)
        {
            await ApiResponses.WriteErrorAsync(http, ex.Status, ex.Code, ex.Args);
        }
        catch (BadHttpRequestException ex) when (!http.Response.HasStarted)
        {
            await ApiResponses.WriteErrorAsync(http, (HttpStatusCode)ex.StatusCode, "bad_request");
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            // Client went away.
        }
        catch (Exception ex) when (!http.Response.HasStarted)
        {
            log.LogError(ex, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
            await ApiResponses.WriteErrorAsync(http, HttpStatusCode.InternalServerError, "internal_error");
        }
    }
}

/// <summary>Refuses query strings and form fields that carry U+0000 or lone surrogates with a 400.</summary>
public sealed class InputFilterMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        static bool Bad(string? v) => v is not null && (v.Contains('\0', StringComparison.Ordinal) || !Infrastructure.Text.Identifiers.IsWellFormed(v));
        if (http.Request.Query.Any(q => q.Value.Any(Bad)) || http.Request.Path.Value is { } p && Bad(p))
        {
            await ApiResponses.WriteErrorAsync(http, HttpStatusCode.BadRequest, "invalid_characters");
            return;
        }

        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            if (form.Any(f => f.Value.Any(Bad)))
            {
                await ApiResponses.WriteErrorAsync(http, HttpStatusCode.BadRequest, "invalid_characters");
                return;
            }
        }

        await next(http);
    }
}

/// <summary>
/// Strict security headers (PROMPT 7). The CSP has no inline or external scripts. A response that needs style tags
/// (the portal: Ant Design injects its CSS at run time) sets <see cref="StyleNonceKey"/> and gets
/// <c>style-src 'self' 'nonce-…'</c> for that response only, never 'unsafe-inline'.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, string contentSecurityPolicy)
{
    public const string StyleNonceKey = "bid.csp.style-nonce";

    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Content-Security-Policy"] = http.Items[StyleNonceKey] is string nonce
                ? contentSecurityPolicy.Replace("style-src 'self'", $"style-src 'self' 'nonce-{nonce}'", StringComparison.Ordinal)
                : contentSecurityPolicy;
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (http.Request.IsHttps)
            {
                h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            return Task.CompletedTask;
        });
        return next(http);
    }
}
