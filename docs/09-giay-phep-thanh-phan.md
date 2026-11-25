# 09 — Giấy phép thành phần (Component licences)

Mọi gói NuGet/npm và image phải có ở đây trước khi dùng (từ Phase 1 có phép thử đối chiếu `Directory.Packages.props`).

## Chạy thật

| Thành phần | Phiên bản | Giấy phép | Dùng cho |
|---|---|---|---|
| PostgreSQL (image `postgres:16.16-alpine`) | 16.16 | PostgreSQL License | CSDL |
| Valkey (image `valkey/valkey:8.1.12-alpine3.24`) | 8.1.12 | BSD-3-Clause | Đệm, giới hạn tốc độ |
| Caddy (image `caddy:2.10.2-alpine`) | 2.10.2 | Apache-2.0 | TLS, reverse proxy |

## Chỉ trong dev / kiểm thử (không nằm trong image chạy thật)

| Thành phần | Phiên bản | Giấy phép | Dùng cho |
|---|---|---|---|
| OpenID Foundation conformance suite | release-v5.3.4 | MIT | Đối chứng tuân thủ OIDC |
| MongoDB (image `mongo:6.0.13`) | 6.0.13 | SSPL-1.0 | CSDL riêng của bộ kiểm thử tuân thủ (chạy nguyên bản, không phân phối) |
| Keycloak | 26.4.7 | Apache-2.0 | IdP SAML/OIDC đối chứng |
| osixia/openldap (OpenLDAP) | 1.5.0 | MIT (image) / OpenLDAP Public License | Thư mục khách cho Connect |
| Mailpit | 1.30.0 | MIT | Máy nhận thư dev |

## NuGet (`Directory.Packages.props`)

| Gói | Phiên bản | Giấy phép | Dùng cho |
|---|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.15 | MIT | ORM |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 | MIT | Migrations (design time only) |
| Microsoft.EntityFrameworkCore.Relational | 10.0.15 | MIT | ORM |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.4 | PostgreSQL | PostgreSQL provider |
| EFCore.NamingConventions | 10.0.1 | Apache-2.0 | snake_case names |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.15 | MIT | Data Protection key ring in PostgreSQL |
| MediatR | 12.5.0 | Apache-2.0 | Application features (12.5.0 = last Apache-2.0) |
| FluentValidation | 12.2.0 | Apache-2.0 | Validation |
| FluentValidation.DependencyInjectionExtensions | 12.2.0 | Apache-2.0 | Validation DI |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 | JSON logs |
| Swashbuckle.AspNetCore | 10.3.1 | MIT | OpenAPI |
| Microsoft.NET.Test.Sdk | 18.12.0 | MIT | Tests |
| xunit | 2.9.3 | Apache-2.0 | Tests |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | Tests |
| FluentAssertions | 7.2.0 | Apache-2.0 | Tests (7.2.0 = last Apache-2.0) |
| Testcontainers.PostgreSql | 4.17.0 | MIT | Integration tests |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.15 | MIT | Integration tests |

## npm (`web/package.json`, cổng tài khoản/quản trị)

| Gói | Phiên bản | Giấy phép | Dùng cho |
|---|---|---|---|
| @tanstack/react-query | 5.108.3 | MIT | Chạy trong trình duyệt |
| antd | 5.29.3 | MIT | Chạy trong trình duyệt |
| i18next | 27.2.4 | MIT | Chạy trong trình duyệt |
| react | 18.3.1 | MIT | Chạy trong trình duyệt |
| react-dom | 18.3.1 | MIT | Chạy trong trình duyệt |
| react-i18next | 18.0.3 | MIT | Chạy trong trình duyệt |
| react-router-dom | 6.30.6 | MIT | Chạy trong trình duyệt |
| @types/node | 24.19.2 | MIT | Chỉ khi build/kiểm thử |
| @types/react | 18.3.31 | MIT | Chỉ khi build/kiểm thử |
| @types/react-dom | 18.3.7 | MIT | Chỉ khi build/kiểm thử |
| @vitejs/plugin-react | 6.3.0 | MIT | Chỉ khi build/kiểm thử |
| jsdom | 31.0.1 | MIT | Chỉ khi build/kiểm thử |
| typescript | 5.9.3 | Apache-2.0 | Chỉ khi build/kiểm thử |
| vite | 8.3.8 | MIT | Chỉ khi build/kiểm thử |
| vitest | 5.1.1 | MIT | Chỉ khi build/kiểm thử |
