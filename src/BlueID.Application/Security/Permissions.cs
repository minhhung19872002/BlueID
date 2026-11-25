using System.Text.Json;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueID.Application.Security;

/// <summary>Permission codes checked by endpoints. Built-in admin roles (PROMPT 4 policy.admin_roles) grant sets of them.</summary>
public static class Permissions
{
    public const string UsersRead = "directory.users.read";
    public const string UsersWrite = "directory.users.write";
    public const string UsersSecurity = "directory.users.security";
    public const string AuditRead = "audit.read";
    public const string SignInLogsRead = "signins.read";
    public const string RolesRead = "roles.read";
    public const string PlatformTenantsRead = "platform.tenants.read";
    public const string PlatformTenantsWrite = "platform.tenants.write";

    public static readonly IReadOnlyList<string> All =
        [UsersRead, UsersWrite, UsersSecurity, AuditRead, SignInLogsRead, RolesRead, PlatformTenantsRead, PlatformTenantsWrite];
}

public static class AdminRoles
{
    public const string GlobalAdmin = "GLOBAL_ADMIN";
    public const string UserAdmin = "USER_ADMIN";
    public const string GroupsAdmin = "GROUPS_ADMIN";
    public const string HelpdeskAdmin = "HELPDESK_ADMIN";
    public const string SecurityAdmin = "SECURITY_ADMIN";
    public const string SecurityReader = "SECURITY_READER";
    public const string AppAdmin = "APP_ADMIN";
    public const string BillingAdmin = "BILLING_ADMIN";
    public const string AuditReader = "AUDIT_READER";
    public const string GuestInviter = "GUEST_INVITER";
    /// <summary>Platform tenant only: operators of the service.</summary>
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    /// <summary>Built-in role → permissions. Product roles (MAIL_ADMIN…) are registered by resources in Phase 4.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> BuiltIn = new Dictionary<string, string[]>
    {
        [GlobalAdmin] = [Permissions.UsersRead, Permissions.UsersWrite, Permissions.UsersSecurity, Permissions.AuditRead, Permissions.SignInLogsRead, Permissions.RolesRead],
        [UserAdmin] = [Permissions.UsersRead, Permissions.UsersWrite],
        [GroupsAdmin] = [Permissions.UsersRead],
        [HelpdeskAdmin] = [Permissions.UsersRead, Permissions.UsersSecurity],
        [SecurityAdmin] = [Permissions.UsersRead, Permissions.UsersSecurity, Permissions.SignInLogsRead, Permissions.AuditRead, Permissions.RolesRead],
        [SecurityReader] = [Permissions.UsersRead, Permissions.SignInLogsRead, Permissions.AuditRead, Permissions.RolesRead],
        [AppAdmin] = [Permissions.UsersRead],
        [BillingAdmin] = [Permissions.UsersRead],
        [AuditReader] = [Permissions.AuditRead],
        [GuestInviter] = [Permissions.UsersRead],
        [PlatformAdmin] = [Permissions.PlatformTenantsRead, Permissions.PlatformTenantsWrite, Permissions.UsersRead, Permissions.UsersWrite,
            Permissions.UsersSecurity, Permissions.AuditRead, Permissions.SignInLogsRead, Permissions.RolesRead],
    };

    /// <summary>Makes policy.admin_roles match the built-in table (idempotent, run at start).</summary>
    public static async Task EnsureAsync(BlueIdDbContext db, CancellationToken ct)
    {
        var existing = await db.AdminRoles.ToDictionaryAsync(r => r.Code, ct);
        foreach (var (code, perms) in BuiltIn)
        {
            var json = JsonSerializer.Serialize(perms);
            if (existing.TryGetValue(code, out var row))
            {
                row.Permissions = json;
                row.BuiltIn = true;
            }
            else
            {
                db.AdminRoles.Add(new AdminRole { Code = code, Permissions = json, BuiltIn = true });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Permissions of a user in their tenant (tenant-scoped assignments only in Phase 1; org units come in Phase 4).</summary>
public sealed class PermissionService(BlueIdDbContext db)
{
    public async Task<IReadOnlySet<string>> GetAsync(Guid userId, CancellationToken ct)
    {
        var codes = await db.AdminRoleAssignments.Where(a => a.UserId == userId && a.Scope == AdminScope.Tenant)
            .Select(a => a.RoleCode).Distinct().ToListAsync(ct);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in codes)
        {
            if (AdminRoles.BuiltIn.TryGetValue(c, out var perms))
            {
                set.UnionWith(perms);
            }
        }

        return set;
    }
}
