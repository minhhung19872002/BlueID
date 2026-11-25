using System.Text.Json;
using BlueID.Application.Auth;
using BlueID.Domain;
using BlueID.Infrastructure.Audit;
using BlueID.Infrastructure.Localization;
using BlueID.Infrastructure.Security;
using BlueID.Infrastructure.Text;
using Microsoft.AspNetCore.Identity;

namespace BlueID.UnitTests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_has_algorithm_prefix_and_verifies()
    {
        var h = Pbkdf2PasswordHasher.Hash("correct horse battery staple");
        h.Should().StartWith("$pbkdf2-sha512$i=210000$");
        Pbkdf2PasswordHasher.Verify(h, "correct horse battery staple").Should().Be(PasswordVerificationResult.Success);
        Pbkdf2PasswordHasher.Verify(h, "correct horse battery stapl").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Same_password_gets_a_different_salt()
    {
        Pbkdf2PasswordHasher.Hash("same-password-123").Should().NotBe(Pbkdf2PasswordHasher.Hash("same-password-123"));
    }

    [Fact]
    public void Composed_and_decomposed_unicode_are_the_same_password()
    {
        var composed = "Mật-khẩu-dài-2026";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        Pbkdf2PasswordHasher.Verify(Pbkdf2PasswordHasher.Hash(composed), decomposed).Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Lower_iteration_count_asks_for_rehash()
    {
        // A valid hash at 100 000 iterations, built the same way the hasher does.
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("legacy-password-1"u8.ToArray(), salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA512, 64);
        var stored = $"$pbkdf2-sha512$i=100000${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";
        Pbkdf2PasswordHasher.Verify(stored, "legacy-password-1").Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("$pbkdf2-sha512$i=1$AAAA$AAAA")]
    [InlineData("$pbkdf2-sha512$i=999999999$AAAAAAAAAAAAAAAAAAAAAA$AAAA")]
    [InlineData("$pbkdf2-sha512$i=210000$!!!$???")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$abc$def")]
    public void Malformed_or_tampered_hashes_fail_without_throwing(string stored)
    {
        Pbkdf2PasswordHasher.Verify(stored, "anything").Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Password_rules()
    {
        PasswordService.Validate("short").Should().Be("password_too_short");
        PasswordService.Validate("twelve-chars").Should().BeNull();
        // Grapheme clusters, not UTF-16 units: 11 emoji are 11 characters.
        PasswordService.Validate(string.Concat(Enumerable.Repeat("👍🏽", 11))).Should().Be("password_too_short");
    }

    [Fact]
    public void Soft_lockout_delay_grows_and_is_capped()
    {
        SignInService.DelayAfter(4).Should().Be(TimeSpan.Zero);
        SignInService.DelayAfter(5).Should().Be(TimeSpan.FromSeconds(1));
        SignInService.DelayAfter(8).Should().Be(TimeSpan.FromSeconds(8));
        SignInService.DelayAfter(500).Should().Be(SignInService.MaxDelay);
    }
}

public sealed class IdentifierTests
{
    [Theory]
    [InlineData("Lan@ACME.test", "Lan@acme.test")]
    [InlineData("  lan@acme.test  ", "lan@acme.test")]
    [InlineData("lan@bücher.example", "lan@xn--bcher-kva.example")]
    [InlineData("nguyễn@công-ty.vn", "nguyễn@xn--cng-ty-ixa.vn")]
    [InlineData("lan@acme.test.", "lan@acme.test")]
    public void Addresses_are_normalised(string input, string expected)
    {
        Identifiers.TryNormalizeAddress(input, out var a).Should().BeTrue();
        a.Should().Be(expected);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@acme.test")]
    [InlineData("lan@")]
    [InlineData("la n@acme.test")]
    [InlineData("lan..x@acme.test")]
    [InlineData("lan@localhost")]
    [InlineData("lan@exa mple.com")]
    public void Bad_addresses_are_refused(string input)
    {
        Identifiers.TryNormalizeAddress(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Decomposed_local_part_is_stored_composed()
    {
        Identifiers.TryNormalizeAddress("nguyễn@acme.test", out var a).Should().BeTrue();
        a.Should().Be("nguyễn@acme.test");
    }

    [Fact]
    public void Nul_and_controls_are_removed_and_lone_surrogates_refused()
    {
        Identifiers.CleanText("Lan\0 Nguyễn\u0007").Should().Be("Lan Nguyễn");
        var act = () => Identifiers.CleanText("bad\uD800text");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Domains_display_as_unicode()
    {
        Identifiers.DomainForDisplay("xn--bcher-kva.example").Should().Be("bücher.example");
    }
}

public sealed class AuditHashTests
{
    [Fact]
    public void Hash_ignores_json_key_order_and_whitespace_but_not_values()
    {
        var a = new AuditLog { Action = "user.updated", NewValue = """{"b":1,"a":"x"}""", OccurredAt = DateTimeOffset.UnixEpoch, Result = AuditResult.Success };
        var b = new AuditLog { Action = "user.updated", NewValue = """{ "a": "x", "b": 1 }""", OccurredAt = DateTimeOffset.UnixEpoch, Result = AuditResult.Success };
        var c = new AuditLog { Action = "user.updated", NewValue = """{"a":"y","b":1}""", OccurredAt = DateTimeOffset.UnixEpoch, Result = AuditResult.Success };
        AuditWriter.ComputeHash(a).Should().Equal(AuditWriter.ComputeHash(b));
        AuditWriter.ComputeHash(a).Should().NotEqual(AuditWriter.ComputeHash(c));
    }

    [Fact]
    public void Hash_depends_on_the_previous_row()
    {
        var row = new AuditLog { Action = "x", OccurredAt = DateTimeOffset.UnixEpoch };
        var h1 = AuditWriter.ComputeHash(row);
        row.PrevHash = [1];
        AuditWriter.ComputeHash(row).Should().NotEqual(h1);
    }
}

public sealed class LocalisationTests
{
    [Fact]
    public void Every_language_has_exactly_the_english_keys()
    {
        var en = JsonStringLocalizerFactory.Table("en").Keys.ToHashSet();
        foreach (var lang in JsonStringLocalizerFactory.Languages)
        {
            JsonStringLocalizerFactory.Table(lang).Keys.Should().BeEquivalentTo(en, $"language {lang} must match en.json");
        }

        JsonStringLocalizerFactory.Languages.Should().Contain(["en", "vi"]);
    }

    [Fact]
    public void Every_error_code_thrown_in_code_has_a_message()
    {
        var root = RepoRoot();
        var codes = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f),
                @"(?:AppException\.(?:NotFound|Conflict|Forbidden|Unprocessable)\(|new FieldError\(""[^""]+"", |WriteErrorAsync\([^,]+, [^,]+, |ApiResponses\.Error\([^,]+, |\(""ux_[a-z_]+"", )""([a-z_]+)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet();
        codes.Should().NotBeEmpty();
        var en = JsonStringLocalizerFactory.Table("en");
        codes.Where(c => !en.ContainsKey("error." + c)).Should().BeEmpty();
    }

    internal static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "BlueID.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

public sealed class LicenceListTests
{
    [Fact]
    public void Every_package_is_listed_in_docs_09()
    {
        var root = LocalisationTests.RepoRoot();
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var doc = File.ReadAllText(Path.Combine(root, "docs", "09-giay-phep-thanh-phan.md"));
        var missing = props.Descendants("PackageVersion")
            .Select(e => (Name: e.Attribute("Include")!.Value, Version: e.Attribute("Version")!.Value))
            .Where(p => !doc.Contains($"| {p.Name} | {p.Version} |", StringComparison.Ordinal))
            .ToList();
        missing.Should().BeEmpty("every NuGet package (name and version) must be listed in docs/09");
        var npm = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "web", "package.json"))).RootElement;
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            foreach (var p in npm.GetProperty(section).EnumerateObject())
            {
                doc.Should().Contain($"| {p.Name} | {p.Value.GetString()!.TrimStart('^', '~')} |", $"npm package {p.Name} must be listed in docs/09");
            }
        }
    }
}
