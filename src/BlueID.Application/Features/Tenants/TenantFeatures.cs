using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Web;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Features.Tenants;

public sealed record TenantDto(Guid Id, string Name, string Slug, TenantStatus Status, TenantType Type, string DefaultLocale, string DefaultTimeZone,
    IReadOnlyList<string> Domains, long Users);

/// <summary>Platform administrators only (guarded by platform permissions, and the caller must belong to the platform tenant).</summary>
public sealed record ListTenants : IRequest<IReadOnlyList<TenantDto>>;

public sealed class ListTenantsHandler(BlueIdDbContext db, ICurrentUser me) : IRequestHandler<ListTenants, IReadOnlyList<TenantDto>>
{
    public async Task<IReadOnlyList<TenantDto>> Handle(ListTenants r, CancellationToken ct)
    {
        await PlatformGuard.EnsureAsync(db, me, ct);
        var tenants = await db.Tenants.IgnoreQueryFilters().Where(t => t.DeletedAt == null).OrderBy(t => t.Name).ThenBy(t => t.Id).AsNoTracking().ToListAsync(ct);
        var domains = await db.Domains.IgnoreQueryFilters().Where(d => d.DeletedAt == null).AsNoTracking()
            .Select(d => new { d.TenantId, d.Domain }).ToListAsync(ct);
        var counts = await db.Users.IgnoreQueryFilters().Where(u => u.DeletedAt == null).GroupBy(u => u.TenantId)
            .Select(g => new { g.Key, N = g.LongCount() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return tenants.Select(t => new TenantDto(t.Id, t.Name, t.Slug, t.Status, t.Type, t.DefaultLocale, t.DefaultTimeZone,
            domains.Where(d => d.TenantId == t.Id).Select(d => d.Domain).Order(StringComparer.Ordinal).ToList(), counts.GetValueOrDefault(t.Id))).ToList();
    }
}

public sealed record CreateTenant(string Name, string Slug, string Domain, string DefaultLocale, string DefaultTimeZone,
    string AdminUserPrincipalName, string AdminDisplayName, string AdminPassword) : IRequest<TenantDto>;

/// <summary>Creates a customer tenant with its first (verified by the platform operator) domain and a global administrator.</summary>
public sealed class CreateTenantHandler(BlueIdDbContext db, IDirectoryWriter writer, ICurrentUser me) : IRequestHandler<CreateTenant, TenantDto>
{
    public async Task<TenantDto> Handle(CreateTenant r, CancellationToken ct)
    {
        await PlatformGuard.EnsureAsync(db, me, ct);
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Slug ?? "", "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$"))
        {
            throw new ValidationFailedException([new FieldError("slug", "slug_invalid")]);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tenant = await writer.CreateTenantAsync(r.Name, r.Slug!, TenantType.Customer, r.DefaultLocale, r.DefaultTimeZone, me.Actor, ct);
        var domain = await writer.AddDomainAsync(tenant.Id, r.Domain, verified: true, isDefault: true, me.Actor, ct);
        var admin = await writer.CreateUserAsync(tenant.Id, new NewUser(r.AdminUserPrincipalName, r.AdminDisplayName, Password: r.AdminPassword, MustChangePassword: true), me.Actor, ct);
        await writer.AssignAdminRoleAsync(tenant.Id, admin.Id, Security.AdminRoles.GlobalAdmin, me.Actor, ct);
        await tx.CommitAsync(ct);
        return new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.Status, tenant.Type, tenant.DefaultLocale, tenant.DefaultTimeZone, [domain.Domain], 1);
    }
}

internal static class PlatformGuard
{
    /// <summary>Platform permissions are only honoured for members of the platform tenant (a customer GLOBAL_ADMIN never gets them).</summary>
    public static async Task EnsureAsync(BlueIdDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var tenantId = me.TenantId;
        var isPlatform = await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId && t.Type == TenantType.Platform, ct);
        if (!isPlatform)
        {
            throw AppException.Forbidden();
        }
    }
}
