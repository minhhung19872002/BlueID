using System.Text.Json;
using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Features.Users;
using BlueID.Application.Security;
using BlueID.Application.Web;
using BlueID.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Features.Me;

public sealed record MeDto(UserDto User, string TenantName, string TenantDefaultLocale, bool IsPlatform, IReadOnlyList<string> Permissions);

public sealed record GetMe : IRequest<MeDto>;

public sealed class GetMeHandler(BlueIdDbContext db, ICurrentUser me, PermissionService permissions) : IRequestHandler<GetMe, MeDto>
{
    public async Task<MeDto> Handle(GetMe r, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me.UserId, ct) ?? throw AppException.NotFound("user_not_found");
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == me.TenantId, ct);
        var perms = await permissions.GetAsync(me.UserId, ct);
        return new MeDto(UserDto.From(user), tenant.Name, tenant.DefaultLocale, tenant.Type == Domain.TenantType.Platform, perms.Order(StringComparer.Ordinal).ToList());
    }
}

/// <summary>What a person may change about themselves in Phase 1: display settings shared by every product (PROMPT 6.7).</summary>
public sealed record UpdateMySettings(string? Locale, string? TimeZone, string? HourCycle) : IRequest<UserDto>;

public sealed class UpdateMySettingsHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<UpdateMySettings, UserDto>
{
    public async Task<UserDto> Handle(UpdateMySettings r, CancellationToken ct) =>
        UserDto.From(await writer.UpdateUserAsync(me.TenantId, me.UserId, new UserPatch(Locale: r.Locale, TimeZone: r.TimeZone, HourCycle: r.HourCycle), me.Actor, ct));
}

public sealed record SessionDto(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? Ip, string? UserAgent, IReadOnlyList<string> Amr, bool Current);

public sealed record ListMySessions : IRequest<IReadOnlyList<SessionDto>>;

public sealed class ListMySessionsHandler(BlueIdDbContext db, ICurrentUser me, TimeProvider clock) : IRequestHandler<ListMySessions, IReadOnlyList<SessionDto>>
{
    public async Task<IReadOnlyList<SessionDto>> Handle(ListMySessions r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Sessions.AsNoTracking().Where(s => s.UserId == me.UserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt).ThenBy(s => s.Id).ToListAsync(ct);
        return rows.Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.Ip, s.UserAgent,
            JsonSerializer.Deserialize<string[]>(s.Amr) ?? [], s.Id == me.SessionId)).ToList();
    }
}

public sealed record RevokeMySession(Guid SessionId) : IRequest<int>;

public sealed class RevokeMySessionHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<RevokeMySession, int>
{
    public async Task<int> Handle(RevokeMySession r, CancellationToken ct)
    {
        var n = await writer.RevokeSessionsAsync(me.TenantId, me.UserId, [r.SessionId], "user_action", me.Actor, ct);
        return n == 0 ? throw AppException.NotFound("session_not_found") : n;
    }
}
