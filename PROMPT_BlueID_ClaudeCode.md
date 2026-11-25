# **BlueID** — DỊCH VỤ ĐĂNG NHẬP VÀ DANH BẠ DÙNG CHUNG CHO BỘ SẢN PHẨM BLUE

> Đặc tả để Claude Code xây dựng BlueID từ đầu. Đọc hết trước khi viết dòng mã đầu tiên. Làm tuần tự theo mục 13,
> không nhảy phase.
>
> BlueID là **nền móng của mọi sản phẩm Blue** (BlueMail, BlueChat, BlueDrive, BlueOffice…): BlueID hỏng là mọi sản phẩm
> hỏng; BlueID lộ là mọi sản phẩm lộ. Mô hình tham chiếu: **Microsoft Entra ID** (đăng nhập, danh bạ, khách mời, ứng
> dụng, báo sự kiện truy cập liên tục) — tham chiếu về chức năng, không sao chép nhận diện.

---

## 0. VAI TRÒ, MỤC TIÊU, NGUYÊN TẮC

Bạn là kỹ sư chính xây dựng **BlueID** — nhà cung cấp danh tính (Identity Provider) và danh bạ tổ chức nhiều khách hàng
(multi-tenant), chạy dạng dịch vụ đám mây **và** bản cài riêng. Các sản phẩm khác **không bao giờ** đọc mã hay CSDL của
BlueID; chúng chỉ nói chuyện với BlueID qua **chuẩn mở** (OIDC, OAuth 2.0, SCIM, SET) và qua **API danh bạ** có tài liệu.

**Nguyên tắc không thương lượng:**

1. **Hợp đồng trước, mã sau.** Những gì sản phẩm khác dựa vào — thông tin trong thẻ (claims), API danh bạ, định dạng sự
   kiện, phạm vi quyền — được viết thành tài liệu `docs/05-hop-dong-tich-hop.md` **trước** khi cài đặt, có phiên bản, và
   **không được đổi kiểu phá vỡ** khi đã phát hành (chỉ thêm; bỏ thì qua chu kỳ báo trước).
2. **Chuẩn mở, không tự chế giao thức.** Mọi luồng đăng nhập theo đúng RFC/đặc tả; không có luồng "tiện tay" riêng.
3. **Không tự viết mật mã, không tự viết lõi OAuth/OIDC.** Lõi máy chủ OAuth/OIDC dùng **OpenIddict** (Apache-2.0) trên
   **ASP.NET Core Identity** (của nền tảng .NET) — đây là ngoại lệ có chủ ý so với hướng "tự viết" của BlueMail/BlueChat,
   vì một lỗi nhỏ ở đây là lỗ hổng cho cả bộ sản phẩm. Mọi phép tính mật mã dùng `System.Security.Cryptography`. Phần
   **tự viết**: danh bạ, tenant, xác thực nhiều lớp (TOTP, WebAuthn), SCIM, SAML (trên `SignedXml` của .NET), sự kiện,
   chính sách truy cập, giao diện, gói tích hợp.
4. **An toàn mặc định**: PKCE bắt buộc, không luồng implicit, không luồng password (ROPC), khớp **chính xác** redirect
   URI, thẻ ngắn hạn, refresh token xoay vòng có phát hiện dùng lại, khoá ký xoay định kỳ.
5. **Thu hồi phải có tác dụng ngay.** Khoá tài khoản, đổi mật khẩu, thu hồi phiên → BlueID **báo cho mọi sản phẩm** trong
   vài giây (mục 2.4), không đợi thẻ hết hạn.
6. **Không stub, không mock trong mã bàn giao, không TODO.** Gặp điểm cần quyết định thì kỹ sư chính **tự quyết định**
   theo hướng an toàn hơn, ghi vào `docs/00-quyet-dinh-ky-thuat.md` (bối cảnh → quyết định → bằng chứng → hệ quả) rồi
   làm tiếp; không dừng chờ.
7. **Quốc tế ngay từ đầu** (mục 0.2); `tenant_id` trong mọi bảng nghiệp vụ.
8. **Mọi thông báo lỗi đăng nhập không làm lộ thông tin**: không phân biệt "sai mật khẩu" với "không có tài khoản"; thời
   gian trả lời như nhau.

### 0.1. Định danh

| Hạng mục | Giá trị |
|---|---|
| Tên | **BlueID** |
| Solution | `BlueID.slnx`, namespace `BlueID.*` |
| Docker compose project | `blueid` |
| CSDL | `blueid` (schema: `tenant`, `directory`, `auth`, `oauth`, `apps`, `events`, `federation`, `policy`, `audit`, `hangfire`) |
| Biến môi trường | tiền tố `BID_` |
| Issuer | **một issuer cho mỗi bản triển khai**, ví dụ `https://id.example.com`; tenant nằm trong claim `tid` |
| Tên miền dev | `id.blue.test`; tenant `bluemail.test`, `acme.test`; ứng dụng mẫu `sample.blue.test` |

### 0.2. Quốc tế hoá
- Giao diện đăng nhập, trang tài khoản, trang quản trị, thư hệ thống: tiếng Anh (gốc) + tiếng Việt; thêm ngôn ngữ bằng tệp
  dịch; ngôn ngữ theo `ui_locales` của yêu cầu OIDC → cài đặt người dùng → mặc định tenant → `Accept-Language`.
- RTL, ngày giờ theo múi giờ IANA của người dùng, `InvariantCulture` ở máy chủ, lỗi API có `code` ổn định.
- **Tên người**: `display_name` là chính; `given_name`/`family_name` không bắt buộc, không giả định thứ tự.
- **Email và tên miền quốc tế hoá** (IDNA2008/UTS #46; địa chỉ ngoài ASCII theo RFC 6531): lưu dạng chuẩn hoá, so sánh
  không phân biệt hoa thường ở phần tên miền, **cảnh báo tên miền trộn nhiều hệ chữ**.
- Tên đăng nhập và tên hiển thị chuẩn hoá Unicode NFC; chống mạo danh bằng ký tự đồng hình khi tạo tài khoản.
- Vùng lưu dữ liệu theo tenant; xuất/xoá dữ liệu cá nhân (GDPR và luật tương tự).
- WCAG 2.1 AA cho mọi trang.

---

## 1. STACK

| Thành phần | Công nghệ | Ghi chú |
|---|---|---|
| Backend | **.NET 10**, ASP.NET Core | Clean Architecture + MediatR **12.5.0** |
| OAuth 2.0 / OIDC | **OpenIddict** (bản ổn định mới nhất, ghim phiên bản) | Lưu trữ qua EF Core |
| Người dùng, mật khẩu | **ASP.NET Core Identity** | Bộ băm mật khẩu cắm qua `IPasswordHasher` theo ghi chú ngay dưới bảng |
| CSDL | **PostgreSQL 16** | |
| Bộ đệm, giới hạn tốc độ, phiên | **Valkey** | |
| Việc nền | Hangfire (PostgreSQL) | Gửi sự kiện, thư, đồng bộ |
| SAML | `System.Security.Cryptography.Xml` (`SignedXml`) của .NET + bộ phân tích tự viết có kiểm chặt | Mục 7 |
| Giao diện | **Trang đăng nhập và đồng ý**: Razor Pages dựng phía máy chủ (không cần JavaScript để đăng nhập được, nhanh, khó bị tấn công hơn SPA). **Trang tài khoản và trang quản trị**: React 18 + TypeScript + Vite + Ant Design 5 | |
| Log | Serilog (JSON) | Không ghi mật khẩu, mã OTP, thẻ, bí mật |
| Kiểm thử | xUnit, FluentAssertions **7.2.0**, Testcontainers, Playwright (kể cả **trình xác thực WebAuthn ảo** của Chrome), k6, **bộ kiểm thử tuân thủ OpenID của OpenID Foundation** (chạy trong container) | |
| Triển khai | Docker Compose, Caddy | |

> ⚠️ **Argon2id**: .NET chưa có sẵn Argon2. Phase 0 chọn một trong hai và ghi quyết định vào `docs/00`: (a) dùng **PBKDF2-HMAC-SHA512**
> có sẵn trong .NET với số vòng theo khuyến nghị OWASP hiện hành (không cần thư viện ngoài, đúng nguyên tắc "không tự viết
> mật mã"); (b) thêm một thư viện Argon2id đã được kiểm toán. **Mặc định đề xuất (a)**, và thiết kế định dạng băm có tiền
> tố thuật toán để đổi về sau mà không bắt người dùng đặt lại mật khẩu (băm lại khi họ đăng nhập lần sau).

---

## 2. KIẾN TRÚC

```
 Người dùng (trình duyệt)                 Sản phẩm Blue (BlueMail, BlueChat, …)        Hệ thống của khách
   │ đăng nhập, đồng ý, tài khoản           │ OIDC/OAuth2, API danh bạ, nhận sự kiện      │ SAML/OIDC IdP, SCIM, AD/LDAP
   ▼                                        ▼                                             ▼
 ┌────────────────────────────────────────────────────────────────────────────────────────────────┐
 │ BlueID.Web  — trang đăng nhập/đồng ý/MFA (Razor), endpoint OIDC/OAuth (OpenIddict),              │
 │               SAML SP, OIDC RP tới IdP của khách, SCIM server                                    │
 │ BlueID.Api  — API danh bạ (/directory/v1), API quản trị, API tài khoản của tôi                    │
 │ BlueID.Worker — gửi sự kiện (SET) cho sản phẩm, gửi thư, dọn phiên, xoay khoá, đồng bộ            │
 └────────────────────────────────────────────────────────────────────────────────────────────────┘
        PostgreSQL · Valkey · (khoá ký mã hoá khi lưu)
 BlueID.Connect — tác tử cài trong mạng của khách: đọc AD/LDAP → đẩy lên qua SCIM; xác thực chuyển tiếp (Phase 10)
```

- Các nút BlueID **không giữ trạng thái** trong bộ nhớ; chạy nhiều nút sau bộ cân bằng tải.
- **Sản phẩm kiểm thẻ ngoại tuyến** bằng khoá công khai (JWKS) — BlueID ngừng vài phút thì người đã đăng nhập vẫn dùng
  được tới khi thẻ hết hạn; chỉ đăng nhập mới và làm mới thẻ bị ảnh hưởng.

### 2.1. Mô hình tenant
- Một tenant = một tổ chức khách hàng. Mọi người dùng, nhóm, ứng dụng, chính sách thuộc một tenant.
- **Tên miền đã xác minh** thuộc tenant (bản ghi TXT). Một tên miền chỉ thuộc một tenant (`UNIQUE`). Sản phẩm (ví dụ
  BlueMail) đọc danh sách tên miền đã xác minh từ BlueID rồi tự thêm cấu hình riêng của mình (MX, DKIM…).
- **Tenant nền tảng**: tenant đặc biệt của chủ dịch vụ, chứa quản trị viên nền tảng.

### 2.2. Tài nguyên (resource) và đích của thẻ (audience)
- Mỗi sản phẩm đăng ký là một **tài nguyên** có định danh riêng (ví dụ `https://api.mail.example.com`), khai báo:
  **phạm vi quyền** (scopes, ví dụ `mail.read`, `mail.send`), **vai trò ứng dụng** (app roles, ví dụ `Mail.Admin`,
  `Chat.ComplianceReader`) và **giấy phép** (license) mà nó kiểm.
- Thẻ truy cập cấp cho **đúng một** tài nguyên (RFC 8707 Resource Indicators): thẻ của BlueMail không dùng được ở
  BlueChat. Sản phẩm **phải** kiểm `aud`.

### 2.3. Thẻ và claims — hợp đồng với mọi sản phẩm (ghi đầy đủ trong `docs/05`)

**ID token** (cho ứng dụng biết ai vừa đăng nhập) và **access token** dạng JWT theo **RFC 9068**, ký `RS256` (mặc định) và
`ES256`; `kid` trỏ tới JWKS.

| Claim | Ý nghĩa |
|---|---|
| `iss`, `aud`, `exp`, `iat`, `nbf`, `jti` | Chuẩn |
| `sub` | Mã người dùng **ổn định vĩnh viễn** (UUID), không bao giờ đổi, không tái sử dụng — sản phẩm dùng làm khoá |
| `tid` | Mã tenant |
| `sid` | Mã phiên đăng nhập (cho đăng xuất kênh sau) |
| `name`, `preferred_username`, `email`, `email_verified`, `locale`, `zoneinfo` | Hồ sơ (chỉ khi phạm vi cho phép) |
| `acct` | `member` hoặc `guest` |
| `home_tid` | Tenant gốc của khách mời |
| `amr`, `acr`, `auth_time` | Đã xác thực bằng gì (pwd, otp, hwk/passkey…), mức đảm bảo, lúc nào |
| `roles` | Vai trò ứng dụng của **chính tài nguyên này** đã gán cho người dùng (trực tiếp hoặc qua nhóm) |
| `scp` | Phạm vi được cấp (thẻ theo người dùng) |
| `lic` | Giấy phép sản phẩm này đã gán cho người dùng (ví dụ `mail.standard`) |
| `groups` / `hasgroups` | Mã nhóm (khi tài nguyên yêu cầu); quá **200** nhóm thì không đưa vào thẻ mà đặt `hasgroups: true` để sản phẩm gọi API |
| `xms_cc` (tương đương) | Máy khách hỗ trợ "thách thức lại" khi phiên bị thu hồi (mục 2.4) |
| `client_id`, `azp` | Ứng dụng đã xin thẻ |
| `idtyp` | `user` hoặc `app` (thẻ của ứng dụng chạy nền qua client credentials) |

**Thời hạn mặc định** (cấu hình theo tenant): access token 15 phút; refresh token xoay vòng, hết hạn sau 14 ngày không
dùng, tối đa 90 ngày; phiên đăng nhập trình duyệt theo chính sách.

### 2.4. Sự kiện và thu hồi liên tục

- BlueID phát **Security Event Token** (SET, **RFC 8417**) — JWT ký bằng khoá của BlueID — tới **endpoint nhận sự kiện**
  mà mỗi tài nguyên đăng ký (đẩy qua HTTPS, **RFC 8935**); kèm endpoint **kéo** (RFC 8936) làm dự phòng.
- Loại sự kiện (phiên bản hoá, ghi trong `docs/05`): `user.created`, `user.updated`, `user.disabled`, `user.enabled`,
  `user.deleted`, `user.restored`, `user.sessions_revoked`, `user.credentials_changed`, `user.license_changed`,
  `group.created`, `group.updated`, `group.deleted`, `group.membership_changed`, `tenant.suspended`, `tenant.resumed`,
  `tenant.domain_verified`, `tenant.domain_removed`, `app.consent_revoked`, `role.assignment_changed`.
- **Giao ít nhất một lần, theo thứ tự trong từng đối tượng** (`subject`): hàng đợi trong PostgreSQL với `SKIP LOCKED`,
  thử lại giãn dần, mỗi sự kiện có `jti` để bên nhận chống trùng; lỗi kéo dài thì báo quản trị viên nền tảng.
- **Mục tiêu**: từ lúc quản trị viên bấm "khoá tài khoản" tới lúc mọi sản phẩm đã nhận sự kiện: **dưới 5 giây** ở p95.
- **Thách thức lại** (tương tự Continuous Access Evaluation): sản phẩm đã nhận sự kiện thu hồi thì từ chối thẻ cũ của người
  đó với lỗi `insufficient_claims` kèm lý do; máy khách hỗ trợ thì chuyển người dùng đi đăng nhập lại.
- **Đăng xuất kênh sau** (OIDC Back-Channel Logout 1.0): khi người dùng đăng xuất hoặc phiên bị thu hồi, BlueID gọi
  endpoint đăng xuất của mọi ứng dụng đã dùng phiên đó.

---

## 3. GIAO THỨC VÀ ENDPOINT

| Chuẩn | Ghi chú |
|---|---|
| OAuth 2.0 (RFC 6749), Bearer (RFC 6750) | |
| **Authorization Code + PKCE** (RFC 7636) | **Bắt buộc PKCE** cho mọi máy khách, kể cả máy khách bí mật |
| Refresh token xoay vòng, phát hiện dùng lại → thu hồi cả chuỗi | |
| **Client Credentials** | Cho dịch vụ gọi dịch vụ (sản phẩm gọi API danh bạ) |
| Xác thực máy khách: `client_secret_basic`, **`private_key_jwt`** (RFC 7523, ưu tiên cho sản phẩm Blue) | |
| **Device Authorization** (RFC 8628) | Cho ứng dụng không có trình duyệt tiện dùng |
| Ứng dụng gốc / desktop (RFC 8252): redirect loopback và scheme riêng | |
| **Token Exchange** (RFC 8693) | Sản phẩm A gọi sản phẩm B **thay mặt** người dùng (ví dụ BlueMail lưu tệp vào BlueDrive), có danh sách cho phép |
| Resource Indicators (RFC 8707), JWT access token (RFC 9068) | |
| Introspection (RFC 7662), Revocation (RFC 7009) | |
| **PAR** (RFC 9126) | Tuỳ chọn theo ứng dụng; bắt buộc cho ứng dụng nhạy cảm |
| OIDC Core 1.0, Discovery 1.0, Back-Channel Logout 1.0, RP-Initiated Logout 1.0 | |
| Response mode `query` và `form_post` | |
| **Không hỗ trợ**: implicit, hybrid trả token ở front-channel, ROPC (password grant) | Có phép thử khẳng định bị từ chối |

**Giao thức cũ (IMAP, SMTP, CalDAV…)** không làm được OAuth trong mọi phần mềm, nên BlueID cung cấp **mật khẩu ứng dụng**:
người dùng tạo trong trang tài khoản, gắn với **một tài nguyên** (ví dụ chỉ BlueMail), xem được lần dùng cuối, thu hồi
được; sản phẩm kiểm qua **API nội bộ** `POST /internal/v1/app-passwords/verify` (xác thực bằng `private_key_jwt`, giới hạn
tốc độ, trả `sub`/`tid`). Chính sách tenant có thể tắt mật khẩu ứng dụng. Sản phẩm hỗ trợ được thì dùng
`XOAUTH2`/`OAUTHBEARER` với thẻ BlueID.

---

## 4. MÔ HÌNH DỮ LIỆU POSTGRESQL

Mọi bảng: `id uuid`, `tenant_id`, `created_at`, `created_by`, `updated_at`, `updated_by`, `deleted_at`. Chỉ mục duy nhất
có `WHERE deleted_at IS NULL`. Luật "chỉ một …" là ràng buộc duy nhất ở CSDL.

```
tenant.tenants            -- name, slug, status(ACTIVE|SUSPENDED|TRIAL|DELETED), type(PLATFORM|CUSTOMER),
                          -- default_locale, default_time_zone, data_region, branding(jsonb: logo, màu, ảnh nền,
                          -- chữ trên trang đăng nhập, liên kết điều khoản/riêng tư), settings(jsonb)
tenant.domains            -- domain (duy nhất toàn hệ thống), verification_token, verified_at, is_default,
                          -- federation_id null (đăng nhập qua IdP của khách cho tên miền này)
tenant.subscriptions      -- product_sku (ví dụ mail.standard, chat.standard), seats, valid_from, valid_to, status
tenant.license_assignments-- user_id | group_id, product_sku   UNIQUE(user_id, product_sku)

directory.users           -- user_principal_name (tên đăng nhập, duy nhất toàn hệ thống, dạng email),
                          -- display_name, given_name, family_name, mail (địa chỉ chính), job_title, department,
                          -- office, mobile_phone, business_phone, manager_id, employee_id, photo_key,
                          -- account_type(MEMBER|GUEST), home_tenant_id null, invitation_state, source(LOCAL|SCIM|
                          -- LDAP_SYNC|JIT), external_id, status(ACTIVE|DISABLED|DELETED), locale, time_zone,
                          -- hour_cycle, usage_location, password_last_changed_at, last_sign_in_at,
                          -- security_stamp (đổi là thẻ/phiên cũ mất hiệu lực), deleted_restore_until
directory.user_addresses  -- user_id, address (duy nhất toàn hệ thống), type(PRIMARY|ALIAS)
directory.groups          -- name, description, mail null (duy nhất), type(SECURITY|DISTRIBUTION|MAIL_SECURITY),
                          -- membership(ASSIGNED|DYNAMIC), dynamic_rule null, visibility(PUBLIC|PRIVATE),
                          -- source, external_id
directory.group_members   -- group_id, member_user_id | member_group_id   (lồng nhóm tối đa N cấp, chống vòng)
directory.group_owners    -- group_id, user_id
directory.org_units       -- parent_id, name   (đơn vị tổ chức để áp chính sách, quản trị theo phạm vi)
directory.change_log      -- (cho truy vấn delta) object_type, object_id, change, change_seq   (seq tăng dần toàn tenant)

auth.passwords            -- user_id, hash (có tiền tố thuật toán), created_at, must_change
auth.mfa_methods          -- user_id, type(TOTP|WEBAUTHN|EMAIL_OTP|RECOVERY_CODES), name, secret_encrypted |
                          -- credential_id + public_key + sign_count + aaguid + transports, is_default,
                          -- created_at, last_used_at
auth.sessions             -- user_id, created_at, last_seen_at, ip, user_agent, device_label, amr(jsonb),
                          -- revoked_at, revoke_reason   (phiên trình duyệt của BlueID)
auth.sign_in_logs         -- tenant_id, user_id null, upn_attempted_hash, client_id, resource, ip, user_agent,
                          -- country (nếu có nguồn), result, failure_code, mfa_used, conditional_access(jsonb),
                          -- risk(jsonb), occurred_at
auth.lockouts             -- user_id | ip, until, failures
auth.app_passwords        -- user_id, resource_id, name, hash, last_used_at, last_used_ip, revoked_at
auth.email_tokens         -- user_id, purpose(VERIFY|RESET|INVITE|OTP), token_hash, expires_at, used_at

oauth.*                   -- bảng của OpenIddict (applications, authorizations, scopes, tokens), tách schema
apps.resources            -- identifier (URI), name, owner_tenant_id (PLATFORM cho sản phẩm Blue), scopes(jsonb),
                          -- app_roles(jsonb), license_skus(jsonb), event_endpoint, event_auth, logout_endpoint,
                          -- token_lifetime_overrides(jsonb), group_claims(NONE|SECURITY|ALL)
apps.applications         -- client_id, owner_tenant_id, name, logo_key, type(CONFIDENTIAL|PUBLIC), multi_tenant,
                          -- redirect_uris(jsonb), post_logout_redirect_uris(jsonb), allowed_grants(jsonb),
                          -- requested_permissions(jsonb), credentials(jsonb: chứng chỉ/khoá công khai, bí mật
                          -- đã băm, hạn dùng), require_par, first_party
apps.service_principals   -- application_id, tenant_id, enabled, assignment_required   (ứng dụng trong một tenant)
apps.consents             -- tenant_id, application_id, user_id null (null = quản trị viên đồng ý cho cả tenant),
                          -- resource_id, scopes(jsonb), granted_at, revoked_at
apps.app_role_assignments -- resource_id, role, principal(user_id | group_id | service_principal_id)
apps.exchange_rules       -- từ_resource, tới_resource, scopes được phép đổi   (Token Exchange)

policy.admin_roles        -- code (GLOBAL_ADMIN, USER_ADMIN, GROUPS_ADMIN, HELPDESK_ADMIN, SECURITY_ADMIN,
                          -- SECURITY_READER, APP_ADMIN, BILLING_ADMIN, AUDIT_READER, GUEST_INVITER, + vai trò quản trị
                          -- do sản phẩm đăng ký: MAIL_ADMIN, CHAT_ADMIN…), permissions(jsonb)
policy.admin_role_assignments -- role_code, user_id | group_id, scope(TENANT|ORG_UNIT), scope_id
policy.password_policies  -- tenant_id, min_length, require_classes, blocklist_enabled, max_age_days, history
policy.access_policies    -- tenant_id, name, state(ON|OFF|REPORT_ONLY), conditions(jsonb), grant(jsonb), session(jsonb)
policy.named_locations    -- tenant_id, name, ip_ranges(jsonb), trusted
policy.cross_tenant       -- tenant_id, partner_tenant_id | DEFAULT, inbound(jsonb), outbound(jsonb)

federation.identity_providers -- tenant_id, type(SAML|OIDC), name, metadata(xml|json), signing_certs(jsonb),
                          -- issuer, endpoints(jsonb), attribute_mapping(jsonb), jit_provisioning, domains(jsonb)
federation.scim_tokens    -- tenant_id, name, token_hash, last_used_at, revoked_at
federation.connect_agents -- tenant_id, name, public_key, last_heartbeat_at, version, status

events.outbox             -- tenant_id, resource_id, subject_type, subject_id, event_type, payload(jsonb),
                          -- subject_seq, created_at
events.deliveries         -- outbox_id, resource_id, status, attempts, next_attempt_at, last_error, delivered_at
                          -- UNIQUE(outbox_id, resource_id)

keys.signing_keys         -- kid, algorithm, private_key_encrypted, public_jwk, status(NEXT|ACTIVE|RETIRING|RETIRED),
                          -- activated_at, retire_at

audit.audit_logs          -- append-only, chuỗi băm (prev_hash, hash); mọi thay đổi quản trị, đổi chính sách, cấp/thu
                          -- quyền, xuất dữ liệu, đăng nhập của quản trị viên
```

---

## 5. API

- **API danh bạ** `/directory/v1` (cho sản phẩm và ứng dụng bên thứ ba, xác thực bằng thẻ BlueID có phạm vi
  `directory.read`, `directory.readwrite`…): người dùng, nhóm, thành viên nhóm (kể cả bắc cầu qua nhóm lồng), quản lý trực
  tiếp và cấp dưới, ảnh, tên miền, giấy phép; **truy vấn delta** (`?deltaToken=`) để sản phẩm đồng bộ phần thay đổi; lọc,
  chọn trường, phân trang bằng con trỏ, sắp theo khoá duy nhất ở cuối.
- **API quản trị** `/admin/v1`: mọi thao tác của trang quản trị, kiểm quyền theo vai trò quản trị + phạm vi đơn vị tổ chức.
- **API tài khoản của tôi** `/me/v1`.
- **API nội bộ** `/internal/v1` (chỉ cho tài nguyên đã đăng ký, `private_key_jwt`): kiểm mật khẩu ứng dụng, tra nhanh
  người dùng theo địa chỉ thư (cho máy chủ thư nhận thư), kiểm trạng thái phiên.
- **SCIM 2.0** `/scim/v2` (RFC 7643, 7644): Users, Groups, PATCH, filter, ServiceProviderConfig, Schemas, ResourceTypes.
- Khuôn trả `{ success, data, message, errors }` cho API riêng; SCIM và OIDC theo đúng khuôn của chuẩn.
- OpenAPI đầy đủ. Phép thử quét: mọi endpoint có `[RequirePermission]`, `[RequireScope]`, `[AllowAnonymous]` (chỉ cho
  endpoint giao thức công khai) hoặc tên trong danh sách tự canh kèm lý do.

---

## 6. CHỨC NĂNG

### 6.1. Đăng nhập
- Trang đăng nhập hai bước: nhập tên đăng nhập → **tìm nơi đăng nhập theo tên miền** (home realm discovery): tên miền liên
  kết IdP của khách thì chuyển sang đó; không thì hỏi mật khẩu/passkey.
- **Passkey/WebAuthn** đăng nhập không mật khẩu (WebAuthn Level 2/3): tự viết phần kiểm phía máy chủ — đọc CBOR,
  `clientDataJSON`, `authenticatorData`, chữ ký ES256/RS256/EdDSA* (*EdDSA chỉ khi .NET hỗ trợ sẵn), kiểm `rpId`, `origin`,
  `challenge`, cờ UP/UV, bộ đếm; attestation `none` mặc định, `packed` tuỳ chọn theo chính sách. Kiểm bằng **trình xác
  thực ảo của Chrome** qua Playwright.
- Mật khẩu: băm theo mục 1; chính sách độ dài (mặc định ≥ 12), **danh sách mật khẩu phổ biến/đã lộ** đóng gói sẵn (không
  gọi dịch vụ ngoài), lịch sử N mật khẩu; đổi thuật toán băm khi đăng nhập thành công.
- **Xác thực nhiều lớp**: TOTP (RFC 6238, kèm mã QR), passkey, mã một lần qua email (chỉ khi chính sách cho), mã khôi
  phục; đăng ký phương thức ngay sau lần đăng nhập đầu nếu chính sách bắt buộc; "nhớ thiết bị này N ngày" theo chính sách.
  **SMS: ngoài phạm vi** (kém an toàn, cần nhà cung cấp ngoài).
- **Quên mật khẩu tự phục vụ**: xác minh qua phương thức đã đăng ký; thư đặt lại có thẻ dùng một lần, băm khi lưu, hết hạn
  ngắn; không lộ việc tài khoản có tồn tại.
- Chống dò: giới hạn theo tài khoản (khoá mềm tăng dần, không khoá cứng để kẻ xấu không khoá được người khác) và theo IP;
  phát hiện dò hàng loạt (nhiều tài khoản từ một IP) → thử thách thêm; trả lời đồng thời gian.
- **Thông báo** cho người dùng khi đăng nhập từ thiết bị/vị trí mới, đổi mật khẩu, thêm/bớt phương thức MFA.
- Trang đồng ý (consent) cho ứng dụng bên thứ ba: hiện tên ứng dụng, nhà phát hành, quyền xin; sản phẩm Blue (first-party)
  không hỏi đồng ý.
- Trang chọn tài khoản khi trình duyệt có nhiều phiên; chuyển tenant cho người là khách ở nhiều tenant.
- Thương hiệu tenant trên trang đăng nhập (logo, màu, ảnh nền, lời chào) sau khi biết tên miền.

### 6.2. Phiên và đăng xuất
- Cookie phiên `Secure; HttpOnly; SameSite=Lax`, gắn `security_stamp`; đổi mật khẩu/thu hồi → mọi phiên và refresh token
  mất hiệu lực, phát sự kiện.
- Đăng xuất do ứng dụng khởi tạo, đăng xuất kênh sau tới mọi ứng dụng của phiên.
- Người dùng xem và thu hồi phiên/thiết bị; quản trị viên thu hồi mọi phiên của một người.

### 6.3. Danh bạ
- Người dùng: tạo, sửa, khoá/mở, xoá mềm (khôi phục trong 30 ngày, sau đó xoá hẳn — trừ khi sản phẩm báo đang giữ pháp lý
  thì giữ hồ sơ tối thiểu), đặt lại mật khẩu, buộc đăng ký lại MFA, gán giấy phép, đổi tên đăng nhập (giữ địa chỉ cũ làm
  bí danh), ảnh; nhập/xuất CSV với bảng lỗi sửa tại chỗ; thao tác hàng loạt có trần.
- **Địa chỉ**: địa chỉ chính và bí danh **duy nhất toàn hệ thống**, chỉ thuộc tên miền đã xác minh của tenant.
- Nhóm: bảo mật / phân phối (có địa chỉ thư) / bảo mật có thư; chủ nhóm; nhóm lồng (có giới hạn, chống vòng); **nhóm động**
  theo luật thuộc tính (ví dụ `department eq "Kế toán"`), tính lại khi thuộc tính đổi; nhóm công khai/riêng.
- Đơn vị tổ chức, quản lý trực tiếp, sơ đồ tổ chức trong trang tài khoản.
- **Gán giấy phép** theo người hoặc theo nhóm; không vượt số ghế đã mua; phát sự kiện `user.license_changed` để sản phẩm
  tạo/khoá tài nguyên (ví dụ BlueMail tạo hộp thư).

### 6.4. Khách mời (B2B)
- Mời theo email; người được mời chấp nhận bằng tài khoản BlueID ở tenant của họ (nếu có) hoặc bằng **mã một lần qua email**.
- Khách có `acct=guest`, thấy danh bạ giới hạn theo chính sách; hết hạn và rà soát định kỳ.
- **Cài đặt truy cập liên tenant**: cho/không cho người tenant khác vào; tin MFA của tenant gốc hay bắt làm lại.

### 6.5. Ứng dụng và quyền
- Sản phẩm Blue đăng ký sẵn (seed) là tài nguyên + ứng dụng first-party với `private_key_jwt`.
- Quản trị viên tenant đăng ký ứng dụng của họ hoặc thêm ứng dụng nhiều tenant của bên thứ ba; **đồng ý của quản trị viên**
  cho cả tenant; chặn người dùng tự đồng ý (chính sách); "bắt buộc gán" (chỉ người được gán mới vào được ứng dụng).
- Gán **vai trò ứng dụng** cho người/nhóm; vai trò hiện trong claim `roles` của thẻ cho đúng tài nguyên.
- **Vai trò quản trị** của BlueID (mục 4) và vai trò quản trị do sản phẩm đăng ký; gán theo tenant hoặc theo đơn vị tổ chức.
- **Bí mật và chứng chỉ ứng dụng** có hạn dùng, cảnh báo trước khi hết.

### 6.6. Chính sách truy cập có điều kiện (mức cơ bản)
- Điều kiện: người/nhóm/vai trò, ứng dụng, vị trí mạng đặt tên (dải IP), loại máy khách (trình duyệt/ứng dụng/giao thức cũ),
  mức rủi ro đăng nhập cơ bản.
- Kết quả: chặn, bắt MFA, bắt passkey (chống lừa đảo), thời lượng phiên, không cho "nhớ thiết bị".
- Chế độ **chỉ báo cáo** để thử chính sách trước khi bật; công cụ "nếu người này đăng nhập ứng dụng kia thì chính sách nào
  áp dụng".
- **Chống tự khoá**: không cho lưu chính sách chặn chính quản trị viên đang thao tác; luôn có tài khoản khẩn cấp ngoài phạm
  vi chính sách.

### 6.7. Trang "Tài khoản của tôi"
Hồ sơ, ngôn ngữ, múi giờ, 12/24 giờ (sản phẩm đọc qua claims/API để mọi sản phẩm cùng một cài đặt), **thông tin bảo mật**
(phương thức MFA, passkey, mật khẩu), thiết bị và phiên, hoạt động đăng nhập gần đây, ứng dụng đã cấp quyền (thu hồi), mật
khẩu ứng dụng, nhóm của tôi, tải dữ liệu của tôi.

### 6.8. Trang quản trị
Tổng quan tenant; tên miền (xác minh, liên kết IdP); người dùng; nhóm; khách mời; ứng dụng và đồng ý; vai trò quản trị;
giấy phép và số ghế; chính sách mật khẩu, MFA, truy cập có điều kiện, vị trí mạng; liên kết IdP, SCIM, tác tử Connect;
**nhật ký đăng nhập** (lọc, xem chi tiết chính sách đã áp), **nhật ký kiểm toán**, báo cáo (độ phủ MFA, tài khoản không
hoạt động, đăng nhập thất bại nhiều, ứng dụng có bí mật sắp hết hạn), thương hiệu, cài đặt tenant. **Quản trị nền tảng**:
tenant, gói dịch vụ, tài nguyên (sản phẩm), sức khoẻ hệ thống gửi sự kiện, xoay khoá ký.

### 6.9. Liên kết IdP của khách (đăng nhập một lần vào BlueID)
- **SAML 2.0 SP** (Web Browser SSO, HTTP-Redirect và HTTP-POST): kiểm chữ ký bằng `SignedXml` **có rào chống tấn công bọc
  chữ ký (XML Signature Wrapping)**: chỉ chấp nhận chữ ký trên đúng phần tử Assertion/Response được tham chiếu bằng ID, cấm
  DTD/thực thể ngoài, kiểm `Audience`, `Recipient`, `NotOnOrAfter`, `InResponseTo`, chống phát lại; bộ mẫu tấn công XSW làm
  phép thử.
- **OIDC RP** tới IdP của khách (Microsoft Entra ID, Google Workspace, Keycloak, Okta…).
- Ánh xạ thuộc tính, **tạo tài khoản khi đăng nhập lần đầu** (JIT) hoặc chỉ cho người đã có; chính sách MFA: tin MFA của
  IdP (qua `amr`/`AuthnContext`) hay bắt làm lại.

### 6.10. Nhận người dùng từ hệ thống của khách
- **SCIM 2.0 server**: IdP của khách (Entra ID, Okta, Google…) đẩy người dùng/nhóm vào BlueID; thẻ SCIM theo tenant.
- **BlueID Connect** (tác tử .NET cài trong mạng khách): đọc Active Directory/LDAP (`System.DirectoryServices.Protocols`),
  đẩy thay đổi lên qua SCIM có chữ ký, chạy theo lịch và theo thay đổi; tuỳ chọn **xác thực chuyển tiếp** (BlueID hỏi tác
  tử kiểm mật khẩu với AD qua kênh đi ra từ mạng khách — không mở cổng vào mạng khách).

### 6.11. BlueID làm IdP cho ứng dụng bên thứ ba (Phase 11)
Khách dùng tài khoản BlueID đăng nhập các phần mềm khác: BlueID làm **SAML 2.0 IdP** và OIDC provider cho ứng dụng do quản
trị viên thêm; tuỳ chọn đẩy người dùng sang ứng dụng đó qua SCIM (máy khách SCIM).

---

## 7. BẢO MẬT VÀ VẬN HÀNH

- Mục tiêu đánh giá: **OWASP ASVS mức 2** (ghi bảng đối chiếu trong `docs/10-asvs.md`), và **qua bộ kiểm thử tuân thủ OpenID**
  cho các hồ sơ Basic OP, Config OP, Form Post OP, RP-Initiated Logout, Back-Channel Logout (chạy trong CI).
- HTTPS bắt buộc, HSTS, CSP chặt trên trang đăng nhập (không script ngoài), chống nhúng khung (clickjacking), chống CSRF trên
  mọi biểu mẫu, chống chuyển hướng mở (chỉ redirect URI đã đăng ký, khớp chính xác).
- **Khoá ký**: tạo khoá kế tiếp trước, công bố trong JWKS ít nhất 48 giờ trước khi dùng, giữ khoá cũ trong JWKS tới khi mọi
  thẻ do nó ký hết hạn; khoá riêng mã hoá khi lưu bằng khoá chủ từ biến môi trường; tuỳ chọn HSM/KMS: ngoài phạm vi bản này.
- Bí mật (TOTP, bí mật ứng dụng của IdP khách) mã hoá khi lưu; bí mật ứng dụng chỉ lưu băm.
- IP người dùng chỉ lấy từ proxy tin cậy.
- Lọc ký tự U+0000 và UTF-8 hỏng ở cửa vào.
- **Sẵn sàng cao**: mục tiêu 99,95 %; nhiều nút không trạng thái; `/health`, `/health/ready`; đóng máy êm; sao lưu và phục
  hồi đã thử thật; quy trình khôi phục khi mất khoá chủ (ghi `docs/03`).
- **Tài khoản khẩn cấp** của mỗi tenant và của nền tảng, ngoài mọi chính sách truy cập, đăng nhập bị cảnh báo.
- Hiệu năng (một máy 8 nhân/16 GB, chốt ở Phase 0): cấp thẻ qua refresh ≥ 500 lượt/giây; trang đăng nhập p95 < 300 ms; API
  danh bạ p95 < 200 ms với tenant 100.000 người dùng.

---

## 8. GÓI TÍCH HỢP CHO SẢN PHẨM (để BlueMail, BlueChat, BlueDrive dùng)

- **`BlueID.AspNetCore`** (NuGet nội bộ): cấu hình sẵn kiểm thẻ (issuer, `aud`, khoá xoay, độ lệch đồng hồ), thuộc tính
  `[RequireRole]`/`[RequireScope]`/`[RequireLicense]`, bộ nhận sự kiện SET (kiểm chữ ký, chống trùng theo `jti`, xếp thứ tự
  theo đối tượng, gọi `IBlueIdEventHandler` của sản phẩm), bộ đệm "người dùng đã bị thu hồi" để từ chối thẻ cũ, máy khách
  API danh bạ có truy vấn delta, máy khách Token Exchange.
- **Mẫu cho ứng dụng web**: khuyến nghị mô hình **BFF** (máy chủ của sản phẩm giữ thẻ, trình duyệt chỉ có cookie) theo hướng
  dẫn OAuth cho ứng dụng trình duyệt; tài liệu và ứng dụng mẫu đầy đủ (`samples/`).
- **`@blueid/client`** (TypeScript, tự viết): cho ứng dụng SPA/PWA không có BFF — Authorization Code + PKCE, làm mới thẻ,
  xử lý thách thức lại.
- **`docs/05-hop-dong-tich-hop.md`**: claims, phạm vi, vai trò, sự kiện, API, mã lỗi, ví dụ yêu cầu/trả lời, chính sách thay
  đổi phiên bản — **đây là tài liệu duy nhất các sản phẩm khác cần đọc**.
- **Ứng dụng mẫu** `samples/SampleProduct`: một sản phẩm giả đăng nhập qua BlueID, đọc danh bạ, nhận sự kiện, ngắt phiên khi
  tài khoản bị khoá — dùng làm phép thử đầu-cuối của toàn bộ hợp đồng.

---

## 9. DOCKER, DỮ LIỆU MẪU

- `docker-compose.yml`: `postgres`, `valkey`, `web`, `api`, `worker`, `caddy`. `docker-compose.dev.yml`: + ứng dụng mẫu, +
  bộ kiểm thử tuân thủ OpenID, + một IdP SAML và một IdP OIDC đối chứng (ví dụ Keycloak — **chỉ trong kiểm thử**), + OpenLDAP
  có dữ liệu mẫu cho Connect, + máy nhận thư dev (để đọc thư đặt lại mật khẩu, lời mời).
- Ghim phiên bản image; kịch bản triển khai dọn image cũ.
- Dữ liệu mẫu: tenant nền tảng + hai tenant khách (một `en`, một `vi`), mỗi tenant 30–50 người dùng tên nhiều nước không
  trùng, cơ cấu phòng ban và quản lý, nhóm bảo mật/phân phối/động, khách mời, ứng dụng mẫu, tài nguyên của BlueMail và BlueChat
  đã đăng ký sẵn, một người đã có passkey ảo, một người bị khoá; mốc thời gian tính từ ngày nạp.

---

## 10. KIỂM THỬ

- **Mỗi lỗi sửa kèm phép thử đỏ trước, xanh sau.**
- **Bộ kiểm thử tuân thủ OpenID** (OpenID Foundation) chạy trong CI với các hồ sơ ở mục 7 — đây là đối chứng chính, không
  dùng phép thử tự viết thay thế.
- **Phép thử âm** cho từng điều cấm: implicit, ROPC, thiếu PKCE, PKCE `plain`, redirect URI lệch một ký tự, dùng lại mã
  uỷ quyền, dùng lại refresh token (cả chuỗi bị thu hồi), thẻ sai `aud`, thẻ hết hạn, thẻ ký bằng khoá lạ, `alg: none`,
  đổi `alg` RS256→HS256.
- **SAML**: bộ mẫu tấn công bọc chữ ký (XSW1–XSW8), XXE, phát lại, chữ ký trên phần tử sai; đối chứng với IdP thật trong
  kiểm thử.
- **WebAuthn**: trình xác thực ảo của Chrome (đăng ký, đăng nhập, bộ đếm lùi, sai origin/rpId).
- **SCIM**: đối chứng bằng một máy khách SCIM độc lập (kịch bản tạo/sửa/PATCH/xoá người và nhóm như IdP lớn gửi).
- **Sự kiện**: khoá tài khoản → ứng dụng mẫu nhận và ngắt phiên trong < 5 giây; sập Worker giữa chừng → không mất sự kiện,
  không đảo thứ tự trong một đối tượng; bên nhận trả lỗi → thử lại đúng lịch.
- **Đồng thời**: ba yêu cầu thật sự song song tạo cùng địa chỉ/tên đăng nhập → đúng một thành công; đổi refresh token song
  song → chỉ một lượt thắng.
- **Truy cập chéo tenant** trên mọi endpoint; quản trị viên theo đơn vị tổ chức không thao tác được ngoài phạm vi.
- **Giới hạn tốc độ và chống dò** (k6): dò mật khẩu hàng loạt bị chặn mà người dùng thật vẫn đăng nhập được.
- **Fuzz**: bộ đọc CBOR/WebAuthn, SAML, SCIM filter, JWT.
- **E2E giao diện** ở 1366×768 và 375×812, bằng bàn phím, đo tương phản bằng máy, tiếng Anh/tiếng Việt/RTL.
- **Tải**: theo mục 7.

---

## 11. QUY ƯỚC CODE
- API: `Application/Features/<Tên>/`; không logic trong Controller; ngoại lệ xử lý tập trung.
- Mọi ghi danh bạ đi qua **một** dịch vụ (`IDirectoryWriter`) — ghi `change_log` và `events.outbox` **trong cùng giao
  dịch** với thay đổi (outbox), không phát sự kiện ngoài giao dịch.
- Mọi so sánh bí mật bằng so sánh thời gian hằng (`CryptographicOperations.FixedTimeEquals`).
- Không chuỗi hiển thị trong mã; màu từ token; không `any`.
- Migration có tên nghĩa, không sửa migration đã commit; phép thử bảo đảm mọi lớp migration có `[Migration]`.
- Sau mỗi phase: build sạch, mọi phép thử xanh, cập nhật `README.md`, `CLAUDE.md`, `docs/`, commit `phase-N: …`.

---

## 12. NGOÀI PHẠM VI
SMS/gọi thoại làm MFA; HSM/KMS cho khoá ký; tên miền đăng nhập riêng theo tenant (chỉ có thương hiệu); quản lý thiết bị (MDM);
phát hiện rủi ro bằng học máy; thanh toán và hoá đơn (chỉ có số ghế giấy phép); liên kết với Microsoft Entra ở chiều ngược lại
(Entra tin BlueID). Không viết interface hay bảng chờ sẵn cho những thứ này.

---

## 13. THỨ TỰ THỰC HIỆN

| Phase | Nội dung | Nghiệm thu |
|---|---|---|
| **0** | Hạ tầng; chọn thuật toán băm (mục 1); bộ kiểm thử tuân thủ OpenID, IdP đối chứng, OpenLDAP, máy nhận thư chạy trong dev; **viết `docs/05-hop-dong-tich-hop.md` bản đầu** (claims, sự kiện, API), đối chiếu với những gì sản phẩm hiện có (BlueMail) đang dùng | Kỹ sư chính tự duyệt hợp đồng, ghi biên bản duyệt trong `docs/00` |
| **1** | Nền móng: solution, Docker, PostgreSQL, tenant, người dùng, mật khẩu, trang đăng nhập Razor, phiên, nhật ký kiểm toán, đa ngôn ngữ, khung React quản trị | Đăng nhập bằng mật khẩu; truy cập chéo tenant bị chặn |
| **2** | Lõi OAuth/OIDC (OpenIddict): Authorization Code + PKCE, refresh xoay vòng, client credentials, `private_key_jwt`, resource indicators, JWT access token, introspection, revocation, discovery, JWKS và xoay khoá, đăng xuất (RP-initiated, back-channel), đồng ý; seed tài nguyên BlueMail, BlueChat; ứng dụng mẫu đăng nhập được | **Qua bộ kiểm thử tuân thủ OpenID** các hồ sơ ở mục 7; mọi phép thử âm đạt |
| **3** | MFA: TOTP, passkey/WebAuthn, mã email, mã khôi phục, đăng ký phương thức, quên mật khẩu tự phục vụ, chính sách mật khẩu, chống dò, thông báo bảo mật | |
| **4** | Danh bạ: tên miền (xác minh TXT), người dùng đầy đủ, địa chỉ/bí danh, nhóm (bảo mật, phân phối, lồng, động), đơn vị tổ chức, quản lý, ảnh, giấy phép và số ghế, vai trò ứng dụng và vai trò quản trị, **API danh bạ + truy vấn delta** | |
| **5** | **Sự kiện SET** (đẩy + kéo), thách thức lại, gói `BlueID.AspNetCore`, `@blueid/client`, mẫu BFF, ứng dụng mẫu hoàn chỉnh; **mật khẩu ứng dụng + API nội bộ kiểm** | Khoá tài khoản → ứng dụng mẫu ngắt phiên < 5 giây; đây là điểm BlueMail và BlueChat bắt đầu tích hợp được |
| **6** | Khách mời B2B, cài đặt truy cập liên tenant, Token Exchange giữa sản phẩm, Device Authorization, PAR | |
| **7** | Trang "Tài khoản của tôi" đầy đủ; trang quản trị đầy đủ; thương hiệu; nhật ký đăng nhập; báo cáo | |
| **8** | Truy cập có điều kiện, vị trí mạng, chế độ chỉ báo cáo, công cụ "điều gì sẽ xảy ra", tài khoản khẩn cấp | |
| **9** | Liên kết IdP của khách: SAML SP, OIDC RP, tìm nơi đăng nhập theo tên miền, JIT | Đăng nhập qua IdP đối chứng; bộ mẫu tấn công XSW bị chặn hết |
| **10** | SCIM 2.0 server; BlueID Connect (đồng bộ AD/LDAP, xác thực chuyển tiếp) | Đồng bộ OpenLDAP mẫu 10.000 người đúng và chạy lại không trùng |
| **11** | BlueID làm SAML IdP / OIDC provider cho ứng dụng bên thứ ba, máy khách SCIM đẩy đi | |
| **12** | Hoàn thiện: sẵn sàng cao, hiệu năng, ASVS L2, sao lưu/phục hồi thử thật, xuất/xoá dữ liệu cá nhân, tài liệu bàn giao | `docker compose up -d` lên hệ thống đầy đủ có dữ liệu mẫu |

### 13.1. Tài liệu `docs/`
`00-quyet-dinh-ky-thuat.md` · `01-huong-dan-su-dung.md` · `02-huong-dan-quan-tri.md` · `03-sao-luu-phuc-hoi-va-khoa.md` ·
`04-cai-dat.md` · **`05-hop-dong-tich-hop.md`** · `06-kich-ban-kiem-thu.md` · `07-giao-thuc.md` (bảng RFC/đặc tả: mục nào đã
làm, bằng chứng) · `08-so-loi.md` · `09-giay-phep-thanh-phan.md` · `10-asvs.md`.

---

## 14. BÀI HỌC TỪ CÁC DỰ ÁN TRƯỚC — ĐỪNG LẶP LẠI

1. **Luật "một … một" là ràng buộc duy nhất ở CSDL**, phép thử đồng thời gửi yêu cầu **thật sự song song**: một tên đăng
   nhập, một địa chỉ, một tên miền một tenant, một giấy phép một người một sản phẩm, một refresh token một lượt đổi.
2. **Khoá một tài khoản phải khoá luôn cái thẻ đang cầm** — đó chính là lý do có mục 2.4. Đo bằng thời gian, không bằng lời.
3. **`[Authorize]` không phải là canh quyền** — mọi endpoint phải nói rõ nó đòi quyền gì.
4. **Công tắc được lưu không có nghĩa là có ai đọc nó** — mọi chính sách có chỗ đọc; phép thử quét.
5. **Cấu hình đổi trên màn hình phải có tác dụng ngay**, không đợi khởi động lại.
6. **Tự kiểm bằng mã của mình không chứng minh được gì** — bộ kiểm thử tuân thủ OpenID, trình xác thực ảo của Chrome, IdP
   đối chứng, máy khách SCIM độc lập.
7. **Giao thức phải trả lời sai một cách ồn ào** — yêu cầu sai chuẩn bị từ chối với mã lỗi đúng chuẩn, không "chịu khó" đoán.
8. **Hai lối tới cùng một luật phải cho cùng một câu trả lời** — đăng nhập web, mật khẩu ứng dụng, đăng nhập qua IdP khách
   đều đi qua cùng kiểm tra trạng thái tài khoản, tenant và chính sách.
9. **Bộ đếm và danh sách chạy trên cùng một tập dòng; phân trang sắp theo khoá duy nhất ở cuối.**
10. **Ký tự U+0000 và UTF-8 hỏng sẽ đến** — lọc ở cửa vào.
11. **Giờ hiện cho người dùng là giờ của người dùng.**
12. **Dữ liệu mẫu là một phần của sản phẩm.**
13. **Triển khai xong phải kiểm chính thứ vừa sửa trên máy chủ thật.**
14. **Mỗi lượt triển khai dọn image cũ.**
15. **Đổi hợp đồng là làm hỏng sản phẩm khác mà không ai thấy** — mọi thay đổi claims, sự kiện, API đi qua `docs/05` và có
    phép thử hợp đồng.

## 15. LƯU Ý CUỐI
1. **Không stub.** Gặp điểm cần quyết định thì tự quyết, ghi `docs/00`, làm tiếp.
2. **BlueID hỏng là cả bộ sản phẩm hỏng** — sẵn sàng cao và thu hồi tức thì quan trọng hơn mọi tính năng mới.
3. **Không tự viết mật mã, không tự chế giao thức.**
4. **Hợp đồng tích hợp là sản phẩm**, ngang hàng với mã.
5. Mỗi phase xong: đối chiếu với tài liệu này và với `docs/07-giao-thuc.md`.
