using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueID.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OAuthCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "apps");

            migrationBuilder.EnsureSchema(
                name: "oauth");

            migrationBuilder.EnsureSchema(
                name: "keys");

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    publisher = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    multi_tenant = table.Column<bool>(type: "boolean", nullable: false),
                    first_party = table.Column<bool>(type: "boolean", nullable: false),
                    require_par = table.Column<bool>(type: "boolean", nullable: false),
                    backchannel_logout_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    conformance_test_client = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "oauth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications1", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logout_deliveries",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logout_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resources",
                schema: "apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    app_roles = table.Column<string>(type: "jsonb", nullable: false),
                    license_skus = table.Column<string>(type: "jsonb", nullable: false),
                    event_endpoint = table.Column<string>(type: "text", nullable: true),
                    event_auth = table.Column<string>(type: "jsonb", nullable: false),
                    token_lifetime_overrides = table.Column<string>(type: "jsonb", nullable: false),
                    group_claims = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scopes",
                schema: "oauth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session_clients",
                schema: "auth",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_clients", x => new { x.session_id, x.client_id });
                });

            migrationBuilder.CreateTable(
                name: "signing_keys",
                schema: "keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    private_key_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_jwk = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retire_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signing_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authorizations",
                schema: "oauth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_authorizations_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "oauth",
                        principalTable: "applications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "tokens",
                schema: "oauth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_tokens_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "oauth",
                        principalTable: "applications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tokens_authorizations_authorization_id",
                        column: x => x.authorization_id,
                        principalSchema: "oauth",
                        principalTable: "authorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_applications_tenant_id",
                schema: "apps",
                table: "applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_applications_client_id",
                schema: "apps",
                table: "applications",
                column: "client_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_applications_client_id",
                schema: "oauth",
                table: "applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authorizations_application_id_status_subject_type",
                schema: "oauth",
                table: "authorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_consents_tenant_id",
                schema: "apps",
                table: "consents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_consents_live",
                schema: "apps",
                table: "consents",
                columns: new[] { "tenant_id", "application_id", "user_id" },
                unique: true,
                filter: "revoked_at IS NULL AND deleted_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_logout_deliveries_status_next_attempt_at",
                schema: "auth",
                table: "logout_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_logout_deliveries_session_client",
                schema: "auth",
                table: "logout_deliveries",
                columns: new[] { "session_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resources_tenant_id",
                schema: "apps",
                table: "resources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_resources_identifier",
                schema: "apps",
                table: "resources",
                column: "identifier",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scopes_name",
                schema: "oauth",
                table: "scopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_signing_keys_kid",
                schema: "keys",
                table: "signing_keys",
                column: "kid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_signing_keys_one_active",
                schema: "keys",
                table: "signing_keys",
                column: "algorithm",
                unique: true,
                filter: "status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "ux_signing_keys_one_next",
                schema: "keys",
                table: "signing_keys",
                columns: new[] { "algorithm", "status" },
                unique: true,
                filter: "status = 'NEXT'");

            migrationBuilder.CreateIndex(
                name: "ix_tokens_application_id_status_subject_type",
                schema: "oauth",
                table: "tokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_tokens_authorization_id",
                schema: "oauth",
                table: "tokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_tokens_reference_id",
                schema: "oauth",
                table: "tokens",
                column: "reference_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applications",
                schema: "apps");

            migrationBuilder.DropTable(
                name: "consents",
                schema: "apps");

            migrationBuilder.DropTable(
                name: "logout_deliveries",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "resources",
                schema: "apps");

            migrationBuilder.DropTable(
                name: "scopes",
                schema: "oauth");

            migrationBuilder.DropTable(
                name: "session_clients",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "signing_keys",
                schema: "keys");

            migrationBuilder.DropTable(
                name: "tokens",
                schema: "oauth");

            migrationBuilder.DropTable(
                name: "authorizations",
                schema: "oauth");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "oauth");
        }
    }
}
