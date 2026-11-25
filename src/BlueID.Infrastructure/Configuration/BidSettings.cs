using Microsoft.Extensions.Configuration;

namespace BlueID.Infrastructure.Configuration;

/// <summary>Process settings from BID_* environment variables (PROMPT 0.1). Missing required values stop the start.</summary>
public sealed record BidSettings(
    string DbConnection,
    int DbMaxPool,
    string Issuer,
    string[] TrustedProxies)
{
    public static BidSettings Load(IConfiguration c)
    {
        var db = c["BID_DB_CONNECTION"];
        if (string.IsNullOrWhiteSpace(db))
        {
            var host = c["BID_DB_HOST"] ?? "127.0.0.1";
            var port = c["BID_DB_PORT"] ?? c["BID_DEV_PG_PORT"] ?? "5432";
            var user = Required(c, "BID_DB_USER");
            var pass = Required(c, "BID_DB_PASSWORD");
            var name = c["BID_DB_NAME"] ?? "blueid";
            db = $"Host={host};Port={port};Database={name};Username={user};Password={pass}";
        }

        var issuer = Required(c, "BID_ISSUER").TrimEnd('/');
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
        {
            throw new InvalidOperationException("BID_ISSUER must be an absolute http(s) URL without query or fragment.");
        }

        return new BidSettings(
            db,
            int.TryParse(c["BID_DB_MAX_POOL"], out var pool) ? pool : 50,
            issuer,
            (c["BID_TRUSTED_PROXIES"] ?? "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,127.0.0.1/32,::1/128")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Required(IConfiguration c, string key) =>
        c[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"{key} is required.");
}
