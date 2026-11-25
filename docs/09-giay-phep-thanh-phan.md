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
