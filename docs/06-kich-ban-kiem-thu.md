# 06 — Kịch bản kiểm thử (Test scenarios)

Trạng thái: ✅ đạt · ❌ không đạt · ⏭️ bỏ qua có lý do.

## Phase 0 — Hạ tầng dev

Chạy: `sh scripts/dev-phase0-check.sh` (dev stack đã `up`).

| Mã | Kịch bản | Mong đợi | Kết quả (2026-11-25) | |
|---|---|---|---|---|
| P0-01 | PostgreSQL | 16.x, có `citext`, `pg_trgm` | `16:citext,pg_trgm` | ✅ |
| P0-02 | Valkey | `PONG` | Như mong đợi | ✅ |
| P0-03 | Máy nhận thư | Thư gửi SMTP (tiêu đề tiếng Việt) tìm thấy qua API | Như mong đợi | ✅ |
| P0-04 | Keycloak OIDC | Discovery realm `doitac`, issuer `http://idp.doitac.test:54080/realms/doitac` | Như mong đợi | ✅ |
| P0-05 | Keycloak SAML | Metadata có `IDPSSODescriptor` | Như mong đợi | ✅ |
| P0-06 | OpenLDAP | 10 000 người, 8 nhóm | Như mong đợi | ✅ |
| P0-07 | Caddy | TLS `id.blue.test:54443` (502 khi BlueID chưa chạy) | 502 | ✅ |
| P0-08 | Tên trong mạng | `id.blue.test` → 10.78.0.20 từ container bộ kiểm thử | Như mong đợi | ✅ |
| P0-09 | Bộ kiểm thử OpenID | API trả 200; có đủ 5 kế hoạch của đặc tả mục 7 | Như mong đợi | ✅ |
