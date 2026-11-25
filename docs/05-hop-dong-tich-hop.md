# 05 — Hợp đồng tích hợp BlueID (Integration contract)

**Phiên bản hợp đồng: 1.1** · Ngày: 2026-11-25 · Trạng thái: **đã duyệt** (biên bản: `docs/00` D8)

Đây là **tài liệu duy nhất** một sản phẩm (BlueMail, BlueChat, BlueDrive, BlueOffice, ứng dụng bên thứ ba) cần đọc để
tích hợp với BlueID. Sản phẩm không đọc mã hay CSDL của BlueID; mọi thứ ở đây là chuẩn mở (OIDC, OAuth 2.0, SCIM, SET)
hoặc API có tài liệu.

Từ khoá **PHẢI / KHÔNG ĐƯỢC / NÊN / CÓ THỂ** hiểu theo RFC 2119.

---

## 1. Chính sách phiên bản và thay đổi

| Thứ | Cách đánh phiên bản | Thay đổi được phép trong cùng phiên bản lớn |
|---|---|---|
| Hợp đồng này | `MAJOR.MINOR` ở đầu tài liệu | MINOR: chỉ **thêm** (claim mới, trường JSON mới, loại sự kiện mới, endpoint mới, mã lỗi mới) |
| API danh bạ / quản trị / tài khoản / nội bộ | trong đường dẫn: `/directory/v1`, `/internal/v1`… | Thêm trường, thêm tham số tuỳ chọn, thêm giá trị enum (xem dưới) |
| Sự kiện | hậu tố trong định danh loại: `…/user.disabled/v1` | Thêm trường vào payload |
| Claims trong thẻ | theo phiên bản hợp đồng | Thêm claim |

Quy tắc cho **bên dùng** (sản phẩm) — bắt buộc để BlueID thêm được mà không làm vỡ ai:

1. **PHẢI bỏ qua** claim, trường JSON, tiêu đề HTTP, loại sự kiện mà mình không biết.
2. **PHẢI chịu được** giá trị enum mới (ví dụ `status` mới): coi như giá trị "khác", không ném lỗi.
3. **KHÔNG ĐƯỢC** dựa vào thứ tự trường JSON, thứ tự claim, độ dài cố định của mã (`id`, `jti`, con trỏ phân trang).
4. Mã (`sub`, `tid`, `id` nhóm…) là **chuỗi mờ** (hiện là UUID dạng chữ thường); so sánh nguyên chuỗi.

Thay đổi phá vỡ (bỏ/đổi nghĩa/đổi kiểu): chỉ ở **phiên bản lớn mới** chạy song song với bản cũ ít nhất **12 tháng**;
bản cũ trả tiêu đề `Deprecation` (RFC 9745) và `Sunset` (RFC 8594) ngay từ khi bản mới ra; lịch ghi ở mục 14.

Mọi thay đổi hợp đồng đi kèm phép thử hợp đồng (`tests/BlueID.ContractTests`) và một dòng ở mục 14.

---

## 2. Issuer, discovery, endpoint

- **Một issuer cho mỗi bản triển khai**, ví dụ `https://id.example.com/` (dev: `https://id.blue.test:54443/`); giá trị
  chính xác (kèm `/` cuối) là `issuer` trong discovery và là `iss` của mọi thẻ. Tenant
  **không** nằm trong issuer mà trong claim `tid`.
- Discovery: `{issuer}/.well-known/openid-configuration` (OIDC Discovery 1.0) và
  `{issuer}/.well-known/oauth-authorization-server` (RFC 8414) — cùng nội dung.
- Sản phẩm **NÊN** đọc endpoint từ discovery; đường dẫn dưới đây là cố định trong v1 để cấu hình tĩnh khi cần:

| Mục đích | Đường dẫn |
|---|---|
| Authorization | `/connect/authorize` |
| Pushed Authorization Request (RFC 9126) | `/connect/par` |
| Token | `/connect/token` |
| UserInfo | `/connect/userinfo` |
| JWKS | `/.well-known/jwks.json` |
| Introspection (RFC 7662) | `/connect/introspect` |
| Revocation (RFC 7009) | `/connect/revoke` |
| End session (RP-Initiated Logout 1.0) | `/connect/logout` |
| Device authorization (RFC 8628) | `/connect/device` (người dùng nhập mã ở `/device`) |
| Kéo sự kiện (RFC 8936) | `/events/v1/poll` |
| API danh bạ | `/directory/v1` |
| API nội bộ | `/internal/v1` |
| SCIM 2.0 (nhận người dùng từ hệ thống khách) | `/scim/v2/{tenantId}` |

---

## 3. Tài nguyên (resource), ứng dụng, đích của thẻ

### 3.1. Đăng ký một sản phẩm

Mỗi sản phẩm là **một tài nguyên** (resource server) cộng **một hoặc nhiều ứng dụng** (OAuth client). Sản phẩm Blue
được BlueID đăng ký sẵn (seed) với `first_party = true` (không hỏi đồng ý). Khai báo của tài nguyên:

```json
{
  "identifier": "https://api.mail.example.com",
  "name": "BlueMail",
  "scopes": [ { "value": "mail.read", "description": "Read your mail", "adminConsentRequired": false } ],
  "appRoles": [ { "value": "Mail.Admin", "displayName": "Mail administrator", "allowedMemberTypes": ["User", "Application"] } ],
  "licenseSkus": ["mail.standard", "mail.premium"],
  "groupClaims": "NONE | SECURITY | ALL",
  "eventEndpoint": "https://api.mail.example.com/blueid/events",
  "eventAuth": { "type": "none | bearer", "token": "…" },
  "eventTypes": ["user.*", "group.*", "tenant.*"],
  "backchannelLogoutUri": "https://mail.example.com/blueid/backchannel-logout",
  "adminRoles": [ { "code": "MAIL_ADMIN", "displayName": "Mail administrator" } ]
}
```

| Sản phẩm | `identifier` (production) | dev |
|---|---|---|
| BlueID (API danh bạ, nội bộ) | `{issuer}/directory` | `https://id.blue.test:54443/directory` |
| BlueMail | `https://api.mail.<domain>` | `https://api.mail.blue.test` |
| BlueChat | `https://api.chat.<domain>` | `https://api.chat.blue.test` |
| Ứng dụng mẫu | — | `https://sample.blue.test:54443/api` |

### 3.2. Đích của thẻ (RFC 8707)

- Máy khách **PHẢI** gửi `resource=<identifier>` ở yêu cầu authorize **và** token. Thiếu `resource` khi có phạm vi của
  tài nguyên → `invalid_target`. Phạm vi chỉ OIDC (`openid profile email offline_access`) không cần `resource`.
- Một access token chỉ có **đúng một** `aud` (một tài nguyên). Muốn gọi tài nguyên khác: dùng refresh token xin thẻ mới
  với `resource` khác, hoặc Token Exchange (mục 9).
- Phạm vi viết **không có tiền tố** tài nguyên (`mail.read`), vì `resource` đã xác định tài nguyên. Phạm vi của hai tài
  nguyên khác nhau trong cùng một yêu cầu → `invalid_scope`.
- Sản phẩm **PHẢI** kiểm `aud` chứa đúng identifier của mình.

### 3.3. Phạm vi của BlueID (`resource = {issuer}/directory`)

| Phạm vi | Loại | Cho phép |
|---|---|---|
| `directory.read` | uỷ quyền (người dùng) | Đọc người, nhóm, thành viên, tên miền trong tenant **mà người dùng được thấy** |
| `directory.readwrite` | uỷ quyền | + ghi theo vai trò quản trị của chính người dùng |
| `directory.read.all` | ứng dụng (client credentials) | Đọc toàn tenant đã đồng ý cho ứng dụng |
| `directory.readwrite.all` | ứng dụng | + ghi (chỉ ứng dụng first-party hoặc quản trị viên đồng ý) |
| `directory.delta` | ứng dụng | Truy vấn delta |
| `internal.app_passwords.verify` | ứng dụng, chỉ tài nguyên đã đăng ký | `POST /internal/v1/app-passwords/verify` |
| `internal.users.lookup` | ứng dụng | `GET /internal/v1/users/by-address` |
| `internal.sessions.read` | ứng dụng | `GET /internal/v1/sessions/{sid}` |

Phạm vi OIDC chuẩn: `openid`, `profile`, `email`, `offline_access` (cấp refresh token). Thêm `bid.locale` (claim
`locale`, `zoneinfo`, `hour_cycle` — gộp sẵn trong `profile`, phạm vi riêng cho ứng dụng chỉ cần cài đặt hiển thị).

---

## 4. Luồng được hỗ trợ / bị từ chối

| Được hỗ trợ | Ghi chú |
|---|---|
| Authorization Code + **PKCE `S256` bắt buộc** cho **mọi** máy khách | `plain` bị từ chối (`invalid_request`) |
| Refresh token **xoay vòng** | Mỗi lần dùng trả refresh token mới; dùng lại token cũ → **thu hồi cả chuỗi** + sự kiện `user.sessions_revoked` (lý do `refresh_token_reuse`) |
| Client Credentials | Chỉ máy khách bí mật; thẻ có `idtyp = app` |
| Device Authorization (RFC 8628) | Cho máy khách được bật |
| Token Exchange (RFC 8693) | Theo danh sách cho phép (mục 9) |
| PAR (RFC 9126) | Tuỳ chọn; ứng dụng có `require_par` thì bắt buộc |
| `response_mode`: `query`, `form_post` | |
| Xác thực máy khách: `private_key_jwt` (RFC 7523, **bắt buộc cho sản phẩm Blue**), `client_secret_basic` | `client_secret_post` chỉ cho ứng dụng được đăng ký riêng phương thức này (mặc định tắt, thêm ở 1.1); `client_secret_jwt` không hỗ trợ; `none` cho máy khách công khai (SPA, ứng dụng gốc) |
| Ứng dụng gốc (RFC 8252): redirect loopback `http://127.0.0.1:{bất kỳ cổng}/path`, scheme riêng `com.example.app:/cb` | Cổng loopback là ngoại lệ duy nhất của "khớp chính xác" (RFC 8252 §7.3) |

| **Bị từ chối** (có phép thử âm) | Mã lỗi |
|---|---|
| Implicit (`response_type=token`, `id_token`, `id_token token`) | `unsupported_response_type` |
| Hybrid trả thẻ ở front-channel | `unsupported_response_type` |
| ROPC (`grant_type=password`) | `unsupported_grant_type` |
| Thiếu PKCE, PKCE `plain` | `invalid_request` |
| Redirect URI không khớp **chính xác** từng ký tự | Không chuyển hướng; trang lỗi của BlueID |
| Dùng lại mã uỷ quyền | `invalid_grant` + thu hồi mọi thẻ đã cấp từ mã đó |

Chữ ký `private_key_jwt`: `RS256`, `PS256`, `ES256`; tiêu đề **`typ: client-authentication+jwt`**
(draft-ietf-oauth-rfc7523bis; assertion `typ: JWT` bị từ chối); `aud` của assertion là **issuer** (cũng chấp nhận URL token
endpoint trong v1); `jti` dùng **một lần** (dùng lại → `invalid_client`) trong thời hạn `exp` (tối đa 5 phút).

---

## 5. Thẻ (tokens)

### 5.1. Chung

- JWT ký `RS256` (mặc định) hoặc `ES256`; tiêu đề `kid` trỏ tới JWKS. **Không** có thẻ ký `HS*`, **không** `alg: none`.
- Access token theo **RFC 9068**: tiêu đề `typ: at+jwt`. ID token: `typ: JWT`. Logout token: `typ: logout+jwt`.
  SET: `typ: secevent+jwt`.
- Khoá ký xoay định kỳ (90 ngày): khoá kế tiếp công bố trong JWKS **≥ 48 giờ** trước khi dùng; khoá cũ ở lại JWKS tới
  khi mọi thẻ nó ký đã hết hạn. Sản phẩm **NÊN** đệm JWKS ≤ 24 giờ và **PHẢI** tải lại JWKS khi gặp `kid` lạ (tối đa
  một lần mỗi 5 phút để không bị lợi dụng).

### 5.2. Kiểm access token ở sản phẩm (bắt buộc)

1. `alg` ∈ {`RS256`, `ES256`}; `typ` = `at+jwt`; chữ ký hợp lệ theo khoá `kid` trong JWKS.
2. `iss` = issuer đã cấu hình (so khớp nguyên chuỗi).
3. `aud` chứa identifier của sản phẩm.
4. `exp`, `nbf` với độ lệch đồng hồ tối đa **60 giây**.
5. `tid`: tenant phải đang hoạt động ở phía sản phẩm (sản phẩm biết qua sự kiện `tenant.*`).
6. Người dùng/phiên không nằm trong danh sách thu hồi đã nhận qua sự kiện (mục 7.5).
7. Quyền: `scp` (thẻ người dùng) hoặc `roles` (thẻ ứng dụng) theo yêu cầu của endpoint; giấy phép theo `lic`.

Gói `BlueID.AspNetCore` làm sẵn cả 7 bước (mục 11).

### 5.3. Claims

| Claim | ID token | Access token | Kiểu | Ý nghĩa |
|---|---|---|---|---|
| `iss` | ✓ | ✓ | string | Issuer |
| `aud` | ✓ (`client_id`) | ✓ (identifier tài nguyên) | string | Đích |
| `exp`, `iat`, `nbf` | ✓ | ✓ | number (giây Unix) | |
| `jti` | ✓ | ✓ | string | Mã thẻ, duy nhất |
| `sub` | ✓ | ✓ | string | Mã người dùng **ổn định vĩnh viễn**, không đổi khi đổi tên/địa chỉ, không tái sử dụng; với thẻ ứng dụng là mã service principal |
| `tid` | ✓ | ✓ | string | Tenant nơi phiên đăng nhập diễn ra (với khách mời: tenant **mời**, nơi họ đang làm việc) |
| `home_tid` | khách | khách | string | Tenant gốc của khách mời |
| `acct` | ✓ | ✓ | `member` \| `guest` | |
| `idtyp` | — | ✓ | `user` \| `app` | |
| `sid` | ✓ | ✓ (thẻ người dùng) | string | Mã phiên đăng nhập BlueID (Back-Channel Logout, sự kiện) |
| `auth_time` | ✓ | ✓ | number | Lúc xác thực |
| `amr` | ✓ | ✓ | string[] | RFC 8176: `pwd`, `otp`, `hwk` (khoá phần cứng/passkey), `swk`, `mfa`, `fed` (qua IdP của khách), `user` (UV) |
| `acr` | ✓ | ✓ | string | `urn:blueid:acr:1fa`, `urn:blueid:acr:mfa`, `urn:blueid:acr:phr` (chống lừa đảo: passkey) |
| `nonce` | ✓ (khi có) | — | string | |
| `azp`, `client_id` | `azp` | `client_id` | string | Ứng dụng đã xin thẻ |
| `scp` | — | thẻ người dùng | string (cách nhau dấu cách) | Phạm vi đã cấp, chỉ của tài nguyên `aud` |
| `scope` | — | ✓ | string (cách nhau dấu cách) | RFC 9068: mọi phạm vi đã cấp (cả phạm vi OIDC) — thêm ở 1.1 |
| `roles` | ✓ (vai trò của ứng dụng đăng nhập) | ✓ | string[] | Vai trò ứng dụng **của chính tài nguyên `aud`** đã gán cho người dùng/ứng dụng (trực tiếp hoặc qua nhóm) |
| `lic` | — | ✓ | string[] | Giấy phép của tài nguyên `aud` đã gán cho người dùng, ví dụ `["mail.standard"]` |
| `groups` | theo `groupClaims` | theo `groupClaims` | string[] | Mã nhóm (bắc cầu qua nhóm lồng); chỉ khi tài nguyên bật |
| `hasgroups` | | | `true` | Thay cho `groups` khi quá **200** nhóm: sản phẩm gọi `GET /directory/v1/users/{sub}/transitiveMemberOf` |
| `xms_cc` | — | ✓ (khi máy khách khai báo) | string[] | `["cp1"]`: máy khách hỗ trợ thách thức lại (mục 7.6). Máy khách khai bằng tham số `claims={"access_token":{"xms_cc":{"values":["cp1"]}}}` |
| `name` | `profile` | `profile` | string | Tên hiển thị (`display_name`) |
| `given_name`, `family_name` | `profile`, khi có | — | string | Không bắt buộc, không giả định thứ tự |
| `preferred_username` | `profile` | `profile` | string | Tên đăng nhập (UPN, dạng email) — **có thể đổi**, không dùng làm khoá |
| `email`, `email_verified` | `email` | `email` | string, bool | Địa chỉ chính |
| `locale` | `profile` / `bid.locale` | `profile` / `bid.locale` | string BCP 47 | Ví dụ `vi`, `en-GB` |
| `zoneinfo` | như trên | như trên | string IANA | Ví dụ `Asia/Ho_Chi_Minh` |
| `hour_cycle` | như trên | như trên | `h12` \| `h23` | Cài đặt 12/24 giờ chung cho mọi sản phẩm |
| `ver` | — | ✓ | `"1.0"` | Phiên bản hợp đồng claims của thẻ |

Claim có tiền tố `oi_` (ví dụ `oi_prst`, `oi_au_id`, `oi_tkn_id`) là dữ liệu nội bộ của máy chủ cấp thẻ: sản phẩm **KHÔNG
ĐƯỢC** dựa vào chúng (có thể đổi hoặc biến mất bất cứ lúc nào, mục 1 quy tắc 1). Ngoài `scp`, access token còn có `scope` theo
RFC 9068 (mọi phạm vi được cấp, kể cả `openid`…); sản phẩm dùng `scp` hay `scope` đều được.

Ví dụ access token của BlueMail (đã giải mã):

```json
{ "typ": "at+jwt", "alg": "RS256", "kid": "2026-11-a" }
{
  "iss": "https://id.example.com", "aud": "https://api.mail.example.com",
  "exp": 1795000900, "iat": 1795000000, "nbf": 1795000000, "jti": "b3c2…",
  "sub": "6f1c2a7e-6c0e-4b0e-9a55-0c1c8f3d2b11", "tid": "0b9f3c1e-…", "acct": "member", "idtyp": "user",
  "sid": "e4a1…", "auth_time": 1794999990, "amr": ["pwd", "otp", "mfa"], "acr": "urn:blueid:acr:mfa",
  "client_id": "bluemail-web", "scp": "mail.read mail.send", "roles": ["Mail.Admin"], "lic": ["mail.standard"],
  "name": "Nguyễn Thị Lan", "preferred_username": "lan@acme.vn", "email": "lan@acme.vn",
  "locale": "vi", "zoneinfo": "Asia/Ho_Chi_Minh", "hour_cycle": "h23", "ver": "1.0"
}
```

### 5.4. Thời hạn mặc định (cấu hình theo tenant và theo tài nguyên)

| Thẻ | Mặc định | Giới hạn cấu hình |
|---|---|---|
| Access token | 15 phút | 5–60 phút |
| ID token | 15 phút | |
| Refresh token | hết hạn sau **14 ngày không dùng**, tối đa **90 ngày** từ lần đăng nhập | 1–90 ngày |
| Mã uỷ quyền | 60 giây, dùng một lần | |
| Mã thiết bị | 10 phút | |
| Phiên trình duyệt BlueID | theo chính sách truy cập (mặc định 14 ngày trượt) | |

Đổi mật khẩu, khoá tài khoản, thu hồi phiên, xoá người dùng → **mọi refresh token và phiên** của người đó mất hiệu lực
ngay; access token đang cầm bị sản phẩm từ chối qua sự kiện (mục 7).

---

## 6. Đăng xuất

- **RP-Initiated Logout 1.0**: `GET/POST /connect/logout?id_token_hint=…&post_logout_redirect_uri=…&state=…`;
  `post_logout_redirect_uri` phải khớp chính xác danh sách đã đăng ký.
- **Back-Channel Logout 1.0**: khi phiên BlueID kết thúc (người dùng đăng xuất, quản trị viên thu hồi, khoá tài khoản),
  BlueID `POST` `logout_token` (form `application/x-www-form-urlencoded`) tới `backchannel_logout_uri` của **mọi ứng dụng
  đã dùng phiên đó**. Logout token có `iss`, `aud` (= `client_id`), `iat`, `exp` (2 phút), `jti`, `sub`, `sid`, `tid`,
  `events: {"http://schemas.openid.net/event/backchannel-logout": {}}`, **không** có `nonce`. Ứng dụng trả `200`; lỗi
  hoặc quá 5 giây → thử lại theo lịch ở mục 7.3. `backchannel_logout_session_required = true`.

---

## 7. Sự kiện (Security Event Token) và thu hồi liên tục

### 7.1. Định dạng SET (RFC 8417)

```json
{ "typ": "secevent+jwt", "alg": "RS256", "kid": "2026-11-a" }
{
  "iss": "https://id.example.com",
  "aud": "https://api.mail.example.com",
  "iat": 1795000000,
  "jti": "0c5d9f0a-…",
  "toe": 1794999999,
  "txn": "8a2b…",
  "sub_id": { "format": "opaque", "id": "6f1c2a7e-…" },
  "tid": "0b9f3c1e-…",
  "bid_subject_type": "user",
  "bid_seq": 42,
  "events": {
    "urn:blueid:event:user.disabled:v1": { "reason": "admin_action", "actor": { "type": "user", "id": "…" } }
  }
}
```

- **Một SET mang đúng một sự kiện** (một khoá trong `events`).
- `jti`: duy nhất; bên nhận **PHẢI** chống trùng theo `jti` (giữ ít nhất 7 ngày).
- `bid_seq`: số thứ tự **tăng dần trong từng đối tượng** (`bid_subject_type` + `sub_id.id`), bắt đầu từ 1, không lặp,
  có thể có khoảng trống. Bên nhận **PHẢI** bỏ qua sự kiện có `bid_seq` nhỏ hơn hoặc bằng số đã xử lý của đối tượng đó.
- `toe`: lúc sự kiện xảy ra; `txn`: mã giao dịch (nhiều sự kiện từ cùng một thao tác có cùng `txn`).
- Sự kiện chỉ gửi cho tài nguyên **liên quan**: tài nguyên có `eventTypes` khớp và tenant đã bật dịch vụ (service
  principal) của tài nguyên đó.

### 7.2. Loại sự kiện v1

Định danh: `urn:blueid:event:<loại>:v1`. Payload chỉ chứa **thứ đã đổi** và ngữ cảnh tối thiểu; sản phẩm cần đầy đủ thì
gọi API danh bạ.

| Loại | `bid_subject_type` | Payload |
|---|---|---|
| `user.created` | user | `{ "upn", "mail", "displayName", "accountType", "source" }` |
| `user.updated` | user | `{ "changed": ["displayName", "mail", …] }` (tên trường của đối tượng User mục 8.3) |
| `user.disabled` | user | `{ "reason": "admin_action \| policy \| tenant_suspended \| sync" }` — **thu hồi ngay** |
| `user.enabled` | user | `{}` |
| `user.deleted` | user | `{ "restoreUntil": "2026-12-25T00:00:00Z", "permanent": false }` — xoá mềm thì `permanent=false`; xoá hẳn gửi lần nữa với `permanent=true` |
| `user.restored` | user | `{}` |
| `user.sessions_revoked` | user | `{ "reason": "admin_action \| user_action \| password_reset \| refresh_token_reuse \| risk", "sessionIds": ["…"] \| null }` — `null` = mọi phiên |
| `user.credentials_changed` | user | `{ "kind": "password \| mfa_added \| mfa_removed \| app_password_revoked", "appPasswordId"?: "…" }` |
| `user.license_changed` | user | `{ "added": ["mail.standard"], "removed": [] }` — chỉ SKU của tài nguyên nhận |
| `group.created` | group | `{ "name", "type", "mail" }` |
| `group.updated` | group | `{ "changed": [...] }` |
| `group.deleted` | group | `{}` |
| `group.membership_changed` | group | `{ "added": [{ "type": "user\|group", "id" }], "removed": [...] }` (trực tiếp; nhóm lồng sản phẩm tự tính hoặc gọi API) |
| `tenant.suspended` | tenant | `{ "reason" }` — mọi người trong tenant coi như bị thu hồi |
| `tenant.resumed` | tenant | `{}` |
| `tenant.domain_verified` | tenant | `{ "domain": "acme.vn" }` |
| `tenant.domain_removed` | tenant | `{ "domain": "acme.vn" }` |
| `app.consent_revoked` | user hoặc tenant | `{ "clientId", "scopes": [...] }` |
| `role.assignment_changed` | user \| group \| app | `{ "added": ["Mail.Admin"], "removed": [] }` (vai trò của tài nguyên nhận) |

`actor` (tuỳ chọn, mọi loại): `{ "type": "user \| app \| system", "id" }`.

### 7.3. Giao bằng đẩy (RFC 8935)

- `POST {eventEndpoint}`, `Content-Type: application/secevent+jwt`, thân là SET; `Authorization: Bearer <token>` nếu
  tài nguyên cấu hình `eventAuth.type = bearer`.
- Bên nhận **PHẢI** kiểm chữ ký (JWKS của BlueID), `iss`, `aud` = identifier của mình, rồi trả **`202 Accepted`** (cả khi
  trùng `jti`). Lỗi theo RFC 8935 §2.3: `400` + `{ "err": "invalid_request \| invalid_key \| invalid_issuer \|
  invalid_audience \| authentication_failed \| access_denied", "description": "…" }` — BlueID **không** thử lại lỗi 400
  (ghi lỗi, báo quản trị viên nền tảng).
- `5xx`, `429`, hết thời gian (10 giây), lỗi mạng → thử lại: 1 s, 2 s, 4 s, … tối đa 10 phút một lần, trong 72 giờ;
  `Retry-After` được tôn trọng. Sau 72 giờ: sự kiện chuyển trạng thái `FAILED`, quản trị viên nền tảng được báo, sự kiện
  vẫn lấy được bằng kéo (mục 7.4) thêm 7 ngày.
- **Giao ít nhất một lần, theo thứ tự trong từng đối tượng**: sự kiện sau của cùng một đối tượng không được gửi trước
  khi sự kiện trước đã giao xong (hoặc `FAILED`). Đối tượng khác nhau gửi song song.
- Mục tiêu: từ lúc thao tác (ví dụ khoá tài khoản) tới lúc mọi tài nguyên nhận: **p95 < 5 giây**.

### 7.4. Giao bằng kéo (RFC 8936) — dự phòng

`POST /events/v1/poll` (xác thực máy khách `private_key_jwt`, phạm vi ngầm theo tài nguyên của máy khách),
thân JSON theo RFC 8936: `{ "ack": ["jti…"], "setErrs": { "jti": { "err", "description" } }, "maxEvents": 100,
"returnImmediately": false }` → `{ "sets": { "jti": "<SET>" }, "moreAvailable": true }`. Kéo dài tối đa 30 giây khi
không có sự kiện. Sự kiện đã `ack` ở kéo cũng được coi là đã giao cho đẩy, và ngược lại.

### 7.5. Bên nhận phải làm gì khi thu hồi

| Sự kiện | Sản phẩm PHẢI |
|---|---|
| `user.disabled`, `user.deleted`, `user.sessions_revoked` (sessionIds = null), `user.credentials_changed` (kind = password) | Từ chối mọi access token của `sub` có `iat` **≤ `toe`** của sự kiện; đóng kết nối đang mở (IMAP IDLE, WebSocket…) |
| `user.sessions_revoked` với `sessionIds` | Từ chối thẻ có `sid` trong danh sách |
| `tenant.suspended` | Từ chối mọi thẻ có `tid` này tới khi `tenant.resumed` |
| `user.credentials_changed` (kind = app_password_revoked) | Đóng kết nối dùng mật khẩu ứng dụng đó |

Giữ mục "đã thu hồi" ít nhất bằng thời hạn access token tối đa (60 phút).

### 7.6. Thách thức lại (tương tự Continuous Access Evaluation)

Khi sản phẩm từ chối một thẻ còn hạn vì thu hồi, trả `401` với:

```
WWW-Authenticate: Bearer error="insufficient_claims",
  error_description="token revoked: user_disabled",
  claims="eyJhY2Nlc3NfdG9rZW4iOnsibmJmIjp7ImVzc2VudGlhbCI6dHJ1ZSwgInZhbHVlIjoiMTc5NTAwMDAwMCJ9fX0"
```

`claims` là base64url của `{"access_token":{"nbf":{"essential":true,"value":"<toe>"}}}`. Máy khách có `xms_cc = cp1`
chuyển người dùng tới `/connect/authorize` với tham số `claims` đó (giải base64url), BlueID buộc đăng nhập lại.
Lý do (`error_description` sau dấu `:`): `user_disabled`, `user_deleted`, `sessions_revoked`, `password_changed`,
`tenant_suspended`, `policy`.

---

## 8. API danh bạ `/directory/v1`

Xác thực: `Authorization: Bearer` access token với `aud = {issuer}/directory`. Dữ liệu luôn trong tenant `tid` của thẻ.

### 8.1. Khuôn trả lời (API riêng của BlueID; SCIM và OIDC theo chuẩn của chúng)

```json
{ "success": true, "data": { … }, "message": null, "errors": [] }
{ "success": false, "data": null, "message": "Người dùng không tồn tại.", "errors": [ { "code": "user_not_found", "field": null } ] }
```

- `message` đã dịch theo `Accept-Language` (chỉ để hiển thị); máy **PHẢI** dựa vào `errors[].code` (ổn định, mục 12).
- Mã HTTP: 200/201/204, 400 (kiểm dữ liệu), 401, 403 (`insufficient_scope` / `forbidden`), 404, 409 (xung đột duy nhất,
  `If-Match` lệch), 412, 422, 429 (`Retry-After`), 5xx.
- Ngày giờ: ISO 8601 UTC có `Z`. Tên trường: camelCase.

### 8.2. Danh sách, lọc, chọn trường, phân trang

- `GET /directory/v1/users?filter=…&select=id,displayName,mail&orderBy=displayName&top=100&cursor=…`
- Trả `{ "items": [...], "nextCursor": "…" | null, "count"?: n }`; `count` chỉ khi `count=true`, đếm **cùng tập dòng**
  với danh sách.
- Phân trang bằng con trỏ mờ; thứ tự luôn có **`id` ở cuối** làm khoá phân xử nên không lặp/mất dòng.
- `filter`: biểu thức `attr op value` nối `and`/`or`, ngoặc; `op` ∈ `eq ne sw co gt ge lt le in`; chuỗi trong `"…"`.
  Thuộc tính lọc được ghi trong OpenAPI. Ví dụ: `department eq "Kế toán" and accountEnabled eq true`.
- `top` tối đa 999 (mặc định 100).

### 8.3. Đối tượng

**User**

```json
{
  "id": "6f1c2a7e-…", "tenantId": "0b9f…", "userPrincipalName": "lan@acme.vn", "displayName": "Nguyễn Thị Lan",
  "givenName": "Lan", "familyName": "Nguyễn", "mail": "lan@acme.vn",
  "addresses": [ { "address": "lan@acme.vn", "type": "PRIMARY" }, { "address": "lan.nguyen@acme.vn", "type": "ALIAS" } ],
  "jobTitle": "Kế toán trưởng", "department": "Kế toán", "office": "HN-3", "mobilePhone": null, "businessPhone": null,
  "employeeId": "E000123", "managerId": "…", "orgUnitId": "…",
  "accountType": "MEMBER", "homeTenantId": null, "source": "LOCAL", "externalId": null,
  "status": "ACTIVE", "accountEnabled": true,
  "locale": "vi", "timeZone": "Asia/Ho_Chi_Minh", "hourCycle": "h23", "usageLocation": "VN",
  "licenses": ["mail.standard"], "photo": { "etag": "…" } ,
  "createdAt": "…", "updatedAt": "…", "lastSignInAt": "…", "deletedAt": null, "restoreUntil": null
}
```

`status` ∈ `ACTIVE`, `DISABLED`, `DELETED` (mở rộng được, mục 1). `source` ∈ `LOCAL`, `SCIM`, `LDAP_SYNC`, `JIT`.

**Group**: `{ id, displayName, description, mail, type: SECURITY|DISTRIBUTION|MAIL_SECURITY, membership:
ASSIGNED|DYNAMIC, dynamicRule, visibility: PUBLIC|PRIVATE, source, externalId, createdAt, updatedAt }`

**Domain**: `{ name, verified, verifiedAt, isDefault, federated }` — `name` dạng A-label (Punycode) chữ thường, kèm
`unicodeName`.

**License**: `{ sku, resource, assignedVia: [ "direct" | "<groupId>" ] }`

### 8.4. Endpoint

| Phương thức | Đường dẫn | Phạm vi | Mục đích |
|---|---|---|---|
| GET | `/users`, `/users/{id}` | `directory.read` | Danh sách / một người (`{id}` = `sub`, hoặc `upn:<địa chỉ>`) |
| GET | `/users/{id}/manager`, `/users/{id}/directReports` | `directory.read` | Quản lý trực tiếp / cấp dưới |
| GET | `/users/{id}/memberOf`, `/users/{id}/transitiveMemberOf` | `directory.read` | Nhóm trực tiếp / bắc cầu |
| GET | `/users/{id}/photo?size=48\|96\|240` | `directory.read` | Ảnh (`ETag`, `Cache-Control: private, max-age=3600`); 404 nếu không có |
| GET | `/users/{id}/licenses` | `directory.read` | Giấy phép |
| POST / PATCH / DELETE | `/users`, `/users/{id}` | `directory.readwrite[.all]` | Ghi (kiểm vai trò quản trị); `PATCH` cần `If-Match: <etag>` |
| GET | `/groups`, `/groups/{id}` | `directory.read` | |
| GET | `/groups/{id}/members`, `/groups/{id}/transitiveMembers`, `/groups/{id}/owners` | `directory.read` | |
| POST / DELETE | `/groups/{id}/members[/{memberId}]` | `directory.readwrite[.all]` | |
| GET | `/domains` | `directory.read` | Tên miền **đã xác minh** của tenant |
| GET | `/tenant` | `directory.read` | `{ id, name, defaultLocale, defaultTimeZone, dataRegion, status }` |
| GET | `/users/delta`, `/groups/delta` | `directory.delta` | Truy vấn delta (8.5) |

### 8.5. Truy vấn delta

1. Lần đầu: `GET /directory/v1/users/delta` → trả các trang `{ "items": [...], "nextCursor": "…" }`; trang cuối có
   `"deltaToken": "…"` (không có `nextCursor`).
2. Lần sau: `GET /directory/v1/users/delta?deltaToken=…` → chỉ những đối tượng **đã đổi từ sau token**, mỗi đối tượng
   một lần (trạng thái mới nhất), đối tượng bị xoá có `"@removed": { "reason": "deleted" | "hardDeleted" }`; trang cuối
   có `deltaToken` mới.
3. Token hết hạn sau **30 ngày** không dùng → `410 Gone`, `code = delta_token_expired`: đồng bộ lại từ đầu.
4. Thay đổi thành viên nhóm nằm trong `groups/delta` với `"membersDelta": { "added": [...], "removed": [...] }`.

Delta đi theo `change_seq` tăng dần toàn tenant nên không mất thay đổi xảy ra trong lúc đang đọc.

---

## 9. Token Exchange (RFC 8693) — sản phẩm gọi sản phẩm thay mặt người dùng

`POST /connect/token` với `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`,
`subject_token=<access token người dùng gửi tới sản phẩm A>`,
`subject_token_type=urn:ietf:params:oauth:token-type:access_token`, `resource=<identifier sản phẩm B>`, `scope=…`;
xác thực máy khách của A bằng `private_key_jwt`.

- Chỉ khi có luật cho phép `A → B` với các phạm vi đó (quản trị viên nền tảng cấu hình); không có → `invalid_target`.
- `subject_token` phải có `aud = A` và còn hạn; thẻ mới có cùng `sub`, `tid`, `sid`, `amr`; thêm
  `act: { "sub": "<service principal của A>", "client_id": "<A>" }`; thời hạn không vượt thẻ gốc.
- Người dùng bị thu hồi thì đổi thẻ bị từ chối (`invalid_grant`).

---

## 10. API nội bộ `/internal/v1` (chỉ cho tài nguyên đã đăng ký)

Xác thực: access token client credentials của ứng dụng first-party, `aud = {issuer}/directory`, phạm vi tương ứng.
Giới hạn tốc độ theo máy khách. Khuôn trả lời như 8.1.

| Phương thức | Đường dẫn | Phạm vi | Yêu cầu → Trả lời |
|---|---|---|---|
| POST | `/internal/v1/app-passwords/verify` | `internal.app_passwords.verify` | `{ "username": "lan@acme.vn", "password": "…", "clientIp": "203.0.113.5", "protocol": "imap" }` → `200 { "valid": true, "sub", "tid", "appPasswordId" }` hoặc `200 { "valid": false, "reason": "invalid" \| "locked" \| "disabled_by_policy" }`. Mật khẩu ứng dụng chỉ hợp lệ cho **tài nguyên của máy khách gọi**; tài khoản/tenant bị khoá → `invalid` (không lộ lý do). Cùng kiểm tra trạng thái tài khoản, tenant và chính sách như đăng nhập web. Giới hạn dò theo tài khoản và `clientIp` |
| GET | `/internal/v1/users/by-address?address=lan@acme.vn` | `internal.users.lookup` | `{ "sub", "tid", "status", "primary": "lan@acme.vn", "isAlias": false }` hoặc 404 `address_not_found`. Cho máy chủ thư nhận thư (địa chỉ duy nhất toàn hệ thống; nhóm có thư trả `"groupId"` thay `sub`) |
| GET | `/internal/v1/sessions/{sid}` | `internal.sessions.read` | `{ "active": true, "sub", "tid", "revokedAt": null, "revokeReason": null }` |

---

## 11. Gói tích hợp

| Gói | Dùng khi | Nội dung |
|---|---|---|
| `BlueID.AspNetCore` (NuGet nội bộ) | Sản phẩm .NET | `services.AddBlueId(o => { o.Issuer; o.Resource; o.ClientId; o.PrivateKeyPem; })`: kiểm thẻ theo 5.2, `[RequireScope]`, `[RequireRole]`, `[RequireLicense]`, endpoint nhận SET (`app.MapBlueIdEvents("/blueid/events")`) gọi `IBlueIdEventHandler`, đệm thu hồi + trả lỗi 7.6, `IBlueIdDirectoryClient` (có delta), `IBlueIdTokenExchange`, kiểm mật khẩu ứng dụng |
| `@blueid/client` (npm) | SPA/PWA không có BFF | Authorization Code + PKCE, làm mới thẻ, xử lý `insufficient_claims` |
| Mẫu BFF (`samples/`) | Ứng dụng web có máy chủ (khuyến nghị) | Máy chủ giữ thẻ, trình duyệt chỉ có cookie `__Host-` |

---

## 12. Mã lỗi ổn định (API riêng)

| Mã | HTTP | Nghĩa |
|---|---|---|
| `unauthorized` | 401 | Thiếu/sai thẻ |
| `insufficient_scope` | 403 | Thẻ thiếu phạm vi |
| `forbidden` | 403 | Không đủ vai trò quản trị / ngoài phạm vi đơn vị tổ chức |
| `validation_failed` | 400 | Xem `errors[].field` |
| `user_not_found`, `group_not_found`, `domain_not_found`, `address_not_found` | 404 | |
| `upn_taken`, `address_taken`, `domain_taken`, `group_mail_taken` | 409 | Vi phạm duy nhất toàn hệ thống |
| `domain_not_verified` | 422 | Địa chỉ thuộc tên miền chưa xác minh của tenant |
| `license_seats_exhausted` | 409 | Hết ghế |
| `group_nesting_cycle`, `group_nesting_too_deep` | 422 | |
| `etag_mismatch` | 412 | `If-Match` lệch |
| `delta_token_expired`, `cursor_invalid` | 410 / 400 | |
| `rate_limited` | 429 | Có `Retry-After` |
| `tenant_suspended` | 403 | |
| `confusable_name` | 422 | Tên đăng nhập/hiển thị trùng đồng hình với một tên đã có |
| `mixed_script_domain` | 422 | Cảnh báo tên miền trộn nhiều hệ chữ (cần xác nhận lại với `confirmMixedScript=true`) |

Lỗi OAuth/OIDC dùng đúng mã của RFC 6749/OIDC; SCIM dùng `scimType` của RFC 7644.

---

## 13. Chuyển đổi từ BlueID nhúng trong BlueMail

BlueMail hiện có BlueID nhúng (`BlueMail.Identity`, docs/00 D3 của BlueMail). Bảng đối chiếu cho bước chuyển:

| Hiện tại ở BlueMail | BlueID 1.0 | Việc của BlueMail |
|---|---|---|
| Endpoint `/connect/authorize`, `/connect/token`, `/connect/logout`, `/connect/userinfo` | Giữ nguyên đường dẫn | Đổi issuer sang BlueID |
| `aud = bluemail-api`, phạm vi `bluemail-api` | `aud = https://api.mail.<domain>`; phạm vi `mail.read`, `mail.send`, … + `resource` | Gửi `resource`, kiểm `aud` mới |
| Claim `bm_tenant` | `tid` | Đổi tên claim đọc |
| Claim `bm_sid` | `sid` | Đổi tên claim đọc |
| Claim `bm_stamp` + `ISessionValidator` mỗi yêu cầu | Không có stamp trong thẻ; thu hồi qua **sự kiện** (mục 7) + `GET /internal/v1/sessions/{sid}` khi cần | Bỏ kiểm stamp, nhận SET |
| Khoá ký tĩnh `BM_OIDC_SIGNING_KEY` | JWKS xoay vòng | Đọc JWKS (gói `BlueID.AspNetCore`) |
| Mật khẩu ứng dụng lưu ở BlueMail, kiểm trong `MailLoginService` | Lưu ở BlueID, kiểm qua `/internal/v1/app-passwords/verify` | Gọi API nội bộ |
| `sub` = `users.id` (Guid) của BlueMail | Công cụ nhập giữ **nguyên** Guid làm `sub` | Không đổi khoá |
| Kênh Valkey `bm:invalidate-user` | SET `user.*` | Đổi nguồn tín hiệu |

Băm mật khẩu Argon2id hiện có của BlueMail: xem `docs/00` D1 (kiểm được khi nhập, băm lại khi đăng nhập).

---

## 14. Lịch sử thay đổi hợp đồng

| Phiên bản | Ngày | Thay đổi |
|---|---|---|
| 1.0 | 2026-11-25 | Bản đầu (Phase 0) |
| 1.1 | 2026-11-25 | Làm rõ (Phase 2, trước khi có sản phẩm nào tích hợp): client assertion phải có `typ: client-authentication+jwt`. Thêm: `client_secret_post` tuỳ chọn theo ứng dụng (mặc định tắt); claim `scope` (RFC 9068) bên cạnh `scp`; ghi rõ claim `oi_*` là nội bộ; giá trị `iss` chính xác là issuer **kèm `/` cuối** như trong discovery (ví dụ `https://id.example.com/`) — sản phẩm luôn lấy issuer từ discovery và so nguyên chuỗi |
