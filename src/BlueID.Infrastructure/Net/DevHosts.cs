using System.Net;
using System.Net.Sockets;

namespace BlueID.Infrastructure.Net;

/// <summary>
/// Development only: "id.blue.test=127.0.0.1,sample.blue.test=127.0.0.1" (BID_DEV_HOSTS) resolves the dev test domains
/// for host-run processes without editing the machine's hosts file. Other names use normal DNS. Callers apply it only in
/// the Development environment.
/// </summary>
public static class DevHosts
{
    public static IReadOnlyDictionary<string, IPAddress> Parse(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && IPAddress.TryParse(p[1], out _))
            .ToDictionary(p => p[0].ToLowerInvariant(), p => IPAddress.Parse(p[1]), StringComparer.Ordinal);

    public static void Apply(SocketsHttpHandler handler, IReadOnlyDictionary<string, IPAddress> map)
    {
        if (map.Count == 0)
        {
            return;
        }

        handler.ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host.ToLowerInvariant();
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (map.TryGetValue(host, out var ip))
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct);
                }
                else
                {
                    await socket.ConnectAsync(ctx.DnsEndPoint, ct);
                }

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
    }
}
