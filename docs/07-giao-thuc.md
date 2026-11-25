# 07 — Giao thức và đặc tả (Protocol coverage)

Mỗi dòng: chuẩn, phase làm, trạng thái, bằng chứng. Trạng thái: ⬜ chưa làm · 🟨 đang làm · ✅ đã làm, có bằng chứng.

| Chuẩn | Phase | Trạng thái | Bằng chứng |
|---|---|---|---|
| OAuth 2.0 (RFC 6749), Bearer (RFC 6750) | 2 | ✅ | `Phase2Tests`, `Phase2NegativeTests`; bộ kiểm thử OpenID |
| PKCE (RFC 7636), `S256` bắt buộc | 2 | ✅ | `Missing_pkce_…`, `Plain_pkce_is_refused`, `Wrong_code_verifier_is_refused`; ngoại lệ D14 |
| Refresh token xoay vòng, phát hiện dùng lại | 2 | ✅ | `Refresh_token_reuse_…`, `Parallel_refresh_…` (BUG-010) |
| Client Credentials | 2 | ✅ | `Client_credentials_gives_an_application_token` |
| `private_key_jwt` (RFC 7523) | 2 | ✅ | `Private_key_jwt_works_…` (`typ: client-authentication+jwt`, `jti` một lần) |
| Resource Indicators (RFC 8707) | 2 | ✅ | `Resource_rules_of_the_contract` |
| JWT access token (RFC 9068) | 2 | ✅ | `Code_flow_with_pkce_…`, `Product_api_accepts_only_its_own_good_tokens` |
| Introspection (RFC 7662), Revocation (RFC 7009) | 2 | ✅ | `Code_reuse_…` (introspect), `Userinfo_answers_401_…` (revoke) |
| OAuth AS Metadata (RFC 8414) | 2 | ✅ | `Discovery_lists_only_…`; Config OP |
| OIDC Core 1.0, Discovery 1.0 | 2 | ✅ | Bộ kiểm thử OpenID: Basic OP, Config OP, Form Post OP (bảng dưới) |
| OIDC RP-Initiated Logout 1.0 | 2 | ✅ | Bộ kiểm thử OpenID (bảng dưới); `Logout_redirects_only_with_a_valid_id_token_hint` |
| OIDC Back-Channel Logout 1.0 | 2 | ✅ | Bộ kiểm thử OpenID (bảng dưới); `Sign_out_reaches_the_product_over_the_back_channel` |
| OAuth 2.0 Form Post Response Mode | 2 | ✅ | Bộ kiểm thử OpenID: Form Post OP |
| AMR values (RFC 8176) | 3 | ⬜ | |
| TOTP (RFC 6238), HOTP (RFC 4226) | 3 | ⬜ | |
| WebAuthn Level 2/3, CBOR (RFC 8949), COSE (RFC 9052/9053) | 3 | ⬜ | Trình xác thực ảo của Chrome |
| SET (RFC 8417), Push (RFC 8935), Poll (RFC 8936), Subject Identifiers (RFC 9493) | 5 | ⬜ | |
| Device Authorization (RFC 8628) | 6 | ⬜ | |
| Token Exchange (RFC 8693) | 6 | ⬜ | |
| PAR (RFC 9126) | 6 | ⬜ | |
| OAuth for Native Apps (RFC 8252) | 2 | 🟨 | Redirect URI scheme riêng khớp chính xác; cổng loopback tuỳ ý (RFC 8252 7.3) chưa làm — Phase 6 |
| SAML 2.0 SP (Web Browser SSO, Redirect + POST binding) | 9 | ⬜ | Keycloak đối chứng; mẫu XSW1–8 |
| SCIM 2.0 (RFC 7643, 7644) | 10 | ⬜ | Máy khách SCIM độc lập |
| SAML 2.0 IdP | 11 | ⬜ | |
| IDNA2008 / UTS #46 (A-label, `IdnMapping` trên ICU), địa chỉ UTF-8 (RFC 6531), Unicode NFC | 1 | ✅ | `IdentifierTests` (đối chứng Punycode với bộ mã hoá của Python) |
| UTS #39 (đồng hình), cảnh báo tên miền trộn hệ chữ | 4 | ⬜ | |
| Deprecation (RFC 9745), Sunset (RFC 8594) | khi cần | ⬜ | |

**Không hỗ trợ (có phép thử khẳng định bị từ chối):** implicit, hybrid trả thẻ ở front-channel, ROPC.

## Bộ kiểm thử tuân thủ OpenID (Phase 2, 2026-11-25)

`registry.gitlab.com/openid/conformance-suite:release-v5.3.4`, máy khách tĩnh (`client_secret_basic`), BlueID dev qua Caddy
`https://id.blue.test:54443`. Chạy lại: `sh scripts/conformance/run.sh` → `python scripts/conformance/summary.py <plan>`.

| Hồ sơ | Kế hoạch | Kết quả |
|---|---|---|
| Basic OP | `oidcc-basic-certification-test-plan[server_metadata=discovery][client_registration=static_client]` | 21 PASSED · 5 REVIEW · 5 WARNING · 4 SKIPPED · **0 FAILED** |
| Config OP | `oidcc-config-certification-test-plan` | 1 PASSED · **0 FAILED** |
| Form Post OP | `oidcc-formpost-basic-certification-test-plan[…]` | 21 PASSED · 5 REVIEW · 5 WARNING · 4 SKIPPED · **0 FAILED** |
| RP-Initiated Logout | `oidcc-rp-initiated-logout-certification-test-plan[response_type=code][client_registration=static_client]` | 3 PASSED · 8 REVIEW · **0 FAILED** |
| Back-Channel Logout | `oidcc-backchannel-rp-initiated-logout-certification-test-plan[…]` | 2 PASSED · **0 FAILED** |

**REVIEW** = bài đạt về máy, cần người xem ảnh chụp do bộ kiểm thử lưu (trang đăng nhập lại cho `prompt=login`/`max_age=1`;
trang lỗi của BlueID cho redirect URI sai, thiếu `response_type`, `post_logout_redirect_uri` sai; trang "đã đăng xuất" khi
không có `id_token_hint` hợp lệ). Đây là cách chứng nhận OpenID vận hành; ảnh nộp kèm khi chứng nhận chính thức.

**WARNING** (có chủ ý, không sửa):

| Bài | Cảnh báo | Lý do |
|---|---|---|
| `oidcc-server` (và các bài dùng luồng chuẩn) | id_token có claim không xin: `tid`, `acct`, `ver`, `oi_au_id`, `oi_tkn_id` | `tid`/`acct`/`ver` là hợp đồng docs/05 5.3 (sản phẩm cần biết tenant ngay từ id_token); `oi_*` là của OpenIddict (docs/05 ghi rõ không được dựa vào) |
| `oidcc-scope-email`, `oidcc-alternate-happy-flow` | `email` có trong id_token | Hợp đồng 1.0 đặt hồ sơ vào id_token khi phạm vi cho phép; bỏ đi là thay đổi phá vỡ. OIDC Core 5.4 cho phép |
| `oidcc-scope-profile` | userinfo thiếu một số claim chuẩn của `profile` (`nickname`, `picture`, `website`, `gender`, `birthdate`…) | BlueID không lưu các thuộc tính đó (đặc tả mục 4) |
| `oidcc-claims-essential` | Không có `name` khi xin qua tham số `claims` | Tham số `claims` không hỗ trợ (`claims_parameter_supported: false`); `name` có qua phạm vi `profile` |

**SKIPPED**: `oidcc-scope-address`, `oidcc-scope-phone`, `oidcc-scope-all` (BlueID không có phạm vi `address`/`phone`),
`oidcc-unsigned-request-object-…` (BlueID trả `request_not_supported` — được phép, OIDC Core 6.1).

Lỗi BlueID mà bộ kiểm thử tìm ra và đã sửa: BUG-012 … BUG-017 (docs/08).
