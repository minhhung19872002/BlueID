using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Web;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Features.Users;

public sealed record UserDto(
    Guid Id, Guid TenantId, string UserPrincipalName, string DisplayName, string? GivenName, string? FamilyName, string? Mail,
    string? JobTitle, string? Department, string? Office, AccountType AccountType, UserSource Source, UserStatus Status,
    string? Locale, string? TimeZone, string? HourCycle, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastSignInAt)
{
    public static UserDto From(User u) => new(u.Id, u.TenantId, u.UserPrincipalName, u.DisplayName, u.GivenName, u.FamilyName, u.Mail,
        u.JobTitle, u.Department, u.Office, u.AccountType, u.Source, u.Status, u.Locale, u.TimeZone, u.HourCycle, u.CreatedAt, u.UpdatedAt, u.LastSignInAt);
}

/// <summary>Users of the caller's tenant (tenant filter), ordered by display name then id; search by prefix of name or sign-in name.</summary>
public sealed record ListUsers(string? Search, int Top, string? Cursor, bool Count) : IRequest<Page<UserDto>>;

public sealed class ListUsersHandler(BlueIdDbContext db) : IRequestHandler<ListUsers, Page<UserDto>>
{
    public async Task<Page<UserDto>> Handle(ListUsers r, CancellationToken ct)
    {
        var top = Math.Clamp(r.Top, 1, 999);
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(r.Search))
        {
            var s = Infrastructure.Text.Identifiers.CleanText(r.Search).Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            q = q.Where(u => EF.Functions.ILike(u.DisplayName, s) || EF.Functions.ILike(u.UserPrincipalName, s));
        }

        // Count runs on exactly the filtered set the page comes from (lesson 9).
        long? count = r.Count ? await q.LongCountAsync(ct) : null;
        if (Cursor.Decode(r.Cursor) is { } pos)
        {
            // Translated to SQL "display_name > @key": the database collation, the same one ORDER BY uses.
#pragma warning disable CA1309
            q = q.Where(u => string.Compare(u.DisplayName, pos.Key) > 0 || (u.DisplayName == pos.Key && u.Id > pos.Id));
#pragma warning restore CA1309
        }

        var rows = await q.OrderBy(u => u.DisplayName).ThenBy(u => u.Id).Take(top + 1).ToListAsync(ct);
        var next = rows.Count > top ? Cursor.Encode(rows[top - 1].DisplayName, rows[top - 1].Id) : null;
        return new Page<UserDto>(rows.Take(top).Select(UserDto.From).ToList(), next, count);
    }
}

public sealed record GetUser(Guid Id) : IRequest<UserDto>;

public sealed class GetUserHandler(BlueIdDbContext db) : IRequestHandler<GetUser, UserDto>
{
    public async Task<UserDto> Handle(GetUser r, CancellationToken ct) =>
        UserDto.From(await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == r.Id, ct) ?? throw AppException.NotFound("user_not_found"));
}

public sealed record CreateUser(string UserPrincipalName, string DisplayName, string? GivenName, string? FamilyName, string? JobTitle,
    string? Department, string? Locale, string? TimeZone, string Password, bool MustChangePassword) : IRequest<UserDto>;

public sealed class CreateUserHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<CreateUser, UserDto>
{
    public async Task<UserDto> Handle(CreateUser r, CancellationToken ct) =>
        UserDto.From(await writer.CreateUserAsync(me.TenantId,
            new NewUser(r.UserPrincipalName, r.DisplayName, r.GivenName, r.FamilyName, r.JobTitle, r.Department, r.Locale, r.TimeZone, r.Password, r.MustChangePassword),
            me.Actor, ct));
}

public sealed record UpdateUser(Guid Id, UserPatch Patch) : IRequest<UserDto>;

public sealed class UpdateUserHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<UpdateUser, UserDto>
{
    public async Task<UserDto> Handle(UpdateUser r, CancellationToken ct) => UserDto.From(await writer.UpdateUserAsync(me.TenantId, r.Id, r.Patch, me.Actor, ct));
}

public sealed record SetUserEnabled(Guid Id, bool Enabled) : IRequest<Unit>;

public sealed class SetUserEnabledHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<SetUserEnabled, Unit>
{
    public async Task<Unit> Handle(SetUserEnabled r, CancellationToken ct)
    {
        if (!r.Enabled && r.Id == me.UserId)
        {
            // An administrator cannot lock themselves out from this screen.
            throw AppException.Unprocessable("cannot_disable_self");
        }

        await writer.SetUserEnabledAsync(me.TenantId, r.Id, r.Enabled, "admin_action", me.Actor, ct);
        return Unit.Value;
    }
}

public sealed record RevokeUserSessions(Guid Id) : IRequest<int>;

public sealed class RevokeUserSessionsHandler(IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<RevokeUserSessions, int>
{
    public Task<int> Handle(RevokeUserSessions r, CancellationToken ct) => writer.RevokeSessionsAsync(me.TenantId, r.Id, null, "admin_action", me.Actor, ct);
}
