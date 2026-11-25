using BlueID.Application.Directory;
using BlueID.Application.Security;
using BlueID.Domain;
using BlueID.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueID.Application.Seeding;

public sealed record SeedOptions(string PlatformDomain, string? AdminUpn, string? AdminPassword, string? SampleUserPassword);

/// <summary>
/// Platform tenant + administrator, and the sample tenants of PROMPT 9 (one English, one Vietnamese, names from many
/// countries, departments and managers, one disabled user). Idempotent: tenants that exist are left alone.
/// </summary>
public sealed class SampleDataSeeder(BlueIdDbContext db, IDirectoryWriter writer, ILogger<SampleDataSeeder> log)
{
    private static readonly string[] Given = ["Lan", "Minh", "Hà", "Tuấn", "Ngọc", "Khánh", "María", "José", "Søren", "Ingrid", "Kenji", "Yuki", "Wei",
        "Priya", "Aarav", "Amara", "Olusegun", "Fatima", "Omar", "Zoë", "Chloé", "Łukasz", "Agnieszka", "Dmitri", "Olga", "Emma", "Liam", "Mateo",
        "Valentina", "Elif", "Ahmet", "Sione", "Aroha", "Nguyệt", "Sơn", "Thảo", "Björn", "Mei", "Haruto", "Ana", "Lucas", "Noor", "Yara", "Ivan", "Sofia"];
    private static readonly string[] Family = ["Nguyễn", "Trần", "Lê", "Phạm", "García", "Martínez", "Jensen", "Lindqvist", "Sato", "Tanaka", "Wang",
        "Sharma", "Okafor", "Haddad", "Müller", "Dubois", "Kowalski", "Ivanova", "Smith", "Rossi", "Yılmaz", "O'Brien", "Silva", "Tupou", "Ngata", "Kim"];
    private static readonly string[] DepartmentsVi = ["Kế toán", "Kinh doanh", "Kỹ thuật", "Nhân sự", "Ban giám đốc"];
    private static readonly string[] DepartmentsEn = ["Finance", "Sales", "Engineering", "People", "Leadership"];

    public async Task SeedPlatformAsync(SeedOptions o, CancellationToken ct)
    {
        await AdminRoles.EnsureAsync(db, ct);
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Type == TenantType.Platform, ct))
        {
            return;
        }

        if (string.IsNullOrEmpty(o.AdminUpn) || string.IsNullOrEmpty(o.AdminPassword))
        {
            log.LogWarning("BID_PLATFORM_ADMIN_UPN / BID_PLATFORM_ADMIN_PASSWORD not set: platform tenant not created");
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var platform = await writer.CreateTenantAsync("BlueID Platform", "platform", TenantType.Platform, "en", "UTC", Actor.System, ct);
        await writer.AddDomainAsync(platform.Id, o.PlatformDomain, verified: true, isDefault: true, Actor.System, ct);
        var admin = await writer.CreateUserAsync(platform.Id, new NewUser(o.AdminUpn, "Platform Administrator", Password: o.AdminPassword, MustChangePassword: true), Actor.System, ct);
        await writer.AssignAdminRoleAsync(platform.Id, admin.Id, AdminRoles.PlatformAdmin, Actor.System, ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Platform tenant created with administrator {Upn}", admin.UserPrincipalName);
    }

    public async Task SeedSampleTenantsAsync(SeedOptions o, CancellationToken ct)
    {
        var password = o.SampleUserPassword ?? throw new InvalidOperationException("BID_SEED_USER_PASSWORD is required for sample data.");
        await SeedTenantAsync("Blue Mail Test", "bluemail", "bluemail.test", "en", "Europe/London", DepartmentsEn, 40, 1, password, ct);
        await SeedTenantAsync("Công ty Acme", "acme", "acme.test", "vi", "Asia/Ho_Chi_Minh", DepartmentsVi, 45, 7, password, ct);
    }

    private async Task SeedTenantAsync(string name, string slug, string domain, string locale, string timeZone, string[] departments, int users, int seed,
        string password, CancellationToken ct)
    {
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug && t.DeletedAt == null, ct))
        {
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tenant = await writer.CreateTenantAsync(name, slug, TenantType.Customer, locale, timeZone, Actor.System, ct);
        await writer.AddDomainAsync(tenant.Id, domain, verified: true, isDefault: true, Actor.System, ct);
        var rnd = new Random(seed);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var created = new List<User>();
        for (var i = 0; i < users; i++)
        {
            string given, family, local;
            do
            {
                given = Given[rnd.Next(Given.Length)];
                family = Family[rnd.Next(Family.Length)];
                local = Slug(given) + "." + Slug(family);
            }
            while (!used.Add(local));

            // The first user of each tenant is the administrator, with a well-known name for manual testing.
            if (i == 0)
            {
                local = "admin";
                given = locale == "vi" ? "Quản" : "Alex";
                family = locale == "vi" ? "Trị" : "Admin";
            }

            // Vietnamese family names come first ("Nguyễn Lan"); IndexOf is -1 for names not in the list (BUG-006).
            var display = locale == "vi" && Array.IndexOf(Family, family) is >= 0 and < 4 ? $"{family} {given}" : $"{given} {family}";
            var dept = departments[i % departments.Length];
            var user = await writer.CreateUserAsync(tenant.Id, new NewUser($"{local}@{domain}", display, given, family,
                JobTitle: i == 0 ? "Administrator" : null, Department: dept, Locale: locale, TimeZone: timeZone, Password: password), Actor.System, ct);
            created.Add(user);
        }

        await writer.AssignAdminRoleAsync(tenant.Id, created[0].Id, AdminRoles.GlobalAdmin, Actor.System, ct);
        // Managers: the first person of each department manages the others in it.
        for (var i = departments.Length; i < created.Count; i++)
        {
            created[i].ManagerId = created[i % departments.Length].Id;
        }

        await db.SaveChangesAsync(ct);
        // One disabled account (PROMPT 9).
        await writer.SetUserEnabledAsync(tenant.Id, created[^1].Id, false, "admin_action", Actor.System, ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Sample tenant {Slug} seeded with {Count} users", slug, created.Count);
    }

    private static string Slug(string s)
    {
        var norm = s.Replace("Đ", "D", StringComparison.Ordinal).Replace("đ", "d", StringComparison.Ordinal).Replace("Ł", "L", StringComparison.Ordinal)
            .Replace("ł", "l", StringComparison.Ordinal).Replace("ø", "o", StringComparison.Ordinal).Replace("ı", "i", StringComparison.Ordinal)
            .Normalize(System.Text.NormalizationForm.FormD);
        return new string(norm.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
    }
}
