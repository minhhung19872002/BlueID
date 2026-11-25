# 00 — Quyết định kỹ thuật (Technical decisions)

Mỗi quyết định ghi: **bối cảnh → quyết định → bằng chứng → hệ quả**. Theo đặc tả (mục 0, nguyên tắc 6), kỹ sư chính
tự quyết định ở các điểm cần chọn, theo hướng an toàn hơn, ghi lại ở đây và làm tiếp.

Ngày lập: 2026-11-25 (Phase 0).

---

## D1. Băm mật khẩu: PBKDF2-HMAC-SHA512 (phương án a)

**Bối cảnh.** Đặc tả mục 1: .NET chưa có Argon2; chọn (a) PBKDF2 có sẵn trong BCL hoặc (b) thư viện Argon2id đã kiểm
toán; mặc định đề xuất (a).

**Quyết định.** (a). `Rfc2898DeriveBytes.Pbkdf2` (BCL), HMAC-SHA512, **210 000 vòng** (OWASP Password Storage Cheat
Sheet, mức hiện hành cho PBKDF2-HMAC-SHA512), muối ngẫu nhiên 16 byte, đầu ra 64 byte. Định dạng chuỗi có tiền tố thuật
toán (kiểu PHC):

```
$pbkdf2-sha512$i=210000$<muối base64 không đệm>$<băm base64 không đệm>
```

- Kiểm bằng `CryptographicOperations.FixedTimeEquals`.
- Đọc lại tham số từ chuỗi khi kiểm → tăng số vòng về sau không làm hỏng mật khẩu cũ; số vòng thấp hơn mức hiện hành →
  `SuccessRehashNeeded` → băm lại ở lần đăng nhập thành công (đặc tả 6.1).
- Chặn chuỗi bất thường từ CSDL bị sửa: số vòng ngoài khoảng [100 000; 5 000 000], muối/băm sai độ dài → coi là sai mật
  khẩu (không đốt CPU, không ném lỗi ra ngoài).
- Cắm vào ASP.NET Core Identity qua `IPasswordHasher<User>` (một lớp).

**Nhập người dùng từ BlueMail** (hợp đồng mục 13): BlueMail đang lưu `$argon2id$v=19$m=19456,t=2,p=1$…` qua
Konscious.Security.Cryptography.Argon2. Quyết định: công cụ chuyển đổi (Phase 5) nhận chuỗi này nguyên dạng; bộ kiểm có
**một nhánh chỉ-kiểm** cho tiền tố `$argon2id$` dùng đúng thư viện BlueMail đang dùng (MIT, đã ghi ở docs/09 của
BlueMail), luôn trả `SuccessRehashNeeded` để đổi sang PBKDF2 ngay lần đăng nhập đầu. Không băm mới bằng Argon2. Khi không
còn chuỗi `$argon2id$` nào trong CSDL thì gỡ nhánh và gói (phép thử kiểm số dòng còn lại trước khi gỡ).

**Hệ quả.** Không thêm thư viện mật mã ngoài cho đường chính. PBKDF2 kém chống GPU hơn Argon2id → bù bằng danh sách mật
khẩu phổ biến/đã lộ, độ dài tối thiểu 12, giới hạn dò (đặc tả 6.1).

## D2. Ghim phiên bản

| Thành phần | Phiên bản | Ghi chú |
|---|---|---|
| .NET SDK / runtime | 10.0.404 / 10.0 | |
| ASP.NET Core Identity, EF Core | 10.0.15 | Cùng BlueMail; dòng 11.0 là của .NET 11 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.4 | |
| OpenIddict | 7.9.0 | Bản ổn định mới nhất (NuGet, 2026-11-25) |
| MediatR | 12.5.0 | Theo đặc tả |
| FluentAssertions | 7.2.0 | Theo đặc tả (bản cuối giấy phép Apache-2.0) |
| PostgreSQL | 16.16 (alpine) | |
| Valkey | 8.1.12 | |
| Caddy | 2.10.2 | |

Mọi gói NuGet/npm phải có trong `docs/09` (phép thử kiểm từ Phase 1).

## Z1. Mạng dev (`deploy/docker-compose.dev.yml`)

**Bối cảnh.** Máy dev đã có nhiều dự án Docker (172.17–31/16, 192.168.x/20, BlueMail 10.77.0.0/24).

**Quyết định.** Mạng `10.78.0.0/24`, project `blueid-dev`. Tiến trình BlueID chạy trên máy (`scripts/dev-run.sh`),
Caddy đứng trước cấp TLS.

| IP | Dịch vụ | Cổng trên máy (chỉ 127.0.0.1) |
|---|---|---|
| 10.78.0.10 | PostgreSQL 16.16 (`citext`, `pg_trgm`) | 54432 |
| 10.78.0.12 | Valkey 8.1.12 | 54379 |
| 10.78.0.13 | Mailpit 1.30.0 (máy nhận thư dev) | UI 54025, SMTP 54026 |
| 10.78.0.20 | Caddy 2.10.2 — `id.blue.test`, `sample.blue.test` | 54443 |
| 10.78.0.30 | Keycloak 26.4.7, realm `doitac` (`idp.doitac.test`) — **chỉ kiểm thử** | 54080 |
| 10.78.0.31 | OpenLDAP (osixia 1.5.0), `dc=doitac,dc=test` — **chỉ kiểm thử** | 54389 |
| 10.78.0.40–42 | Bộ kiểm thử tuân thủ OpenID (mongo, server, nginx) — **chỉ kiểm thử** | 8443 |

**Issuer dev = `https://id.blue.test:54443`.** Caddy nghe đúng cổng 54443 cả trong container lẫn trên máy, và có
alias mạng `id.blue.test`, nên **cùng một URL** dùng được từ trình duyệt trên máy (thêm dòng hosts
`127.0.0.1 id.blue.test sample.blue.test idp.doitac.test`) và từ bộ kiểm thử tuân thủ trong mạng Docker. Cổng 443 của
máy dev đã bị dự án khác chiếm, nên không dùng 443.

**Bằng chứng (2026-11-25).** `sh scripts/dev-phase0-check.sh` → `PHASE 0 CHECK: PASS` (11/11 mục, kể cả 10 000
người dùng LDAP và đủ 5 kế hoạch kiểm thử của OpenID).

## Z2. Bộ kiểm thử tuân thủ OpenID

**Quyết định.** Dùng image dựng sẵn của OpenID Foundation `registry.gitlab.com/openid/conformance-suite:release-v5.3.4`
và `…/nginx:release-v5.3.4` (ghim theo thẻ phát hành, không build Java). Bản 5.3.5 ra ngày 2026-11-24 — chọn 5.3.4 vì đã
có một tháng; nâng có chủ đích. MongoDB **6.0.13** đúng như compose gốc: bộ kiểm thử tự đặt FCV 6.0, chạy trên mongo 7
thì dừng lúc khởi động (docs/08 BUG-002). Kịch bản chạy kế hoạch (`scripts/run-test-plan.py` của bộ kiểm thử) lấy từ bản
sao `.cache/conformance-suite` cùng thẻ (không commit).

Kế hoạch dùng ở Phase 2 (đặc tả mục 7): `oidcc-basic-certification-test-plan`, `oidcc-config-certification-test-plan`,
`oidcc-formpost-basic-certification-test-plan`, `oidcc-rp-initiated-logout-certification-test-plan`,
`oidcc-backchannel-rp-initiated-logout-certification-test-plan`.

## Z3. IdP đối chứng: Keycloak 26.4.7

Realm `doitac` (nhập từ `deploy/keycloak/realm-doitac.json`): 3 người dùng tên nhiều nước, máy khách OIDC `blueid`
(bí mật, PKCE S256, redirect `https://id.blue.test:54443/federation/oidc/callback`), máy khách SAML với entityID
`https://id.blue.test:54443/federation/saml/metadata`, ACS `…/federation/saml/acs`, ký assertion + response RSA-SHA256.
Chỉ dùng trong kiểm thử (đặc tả mục 9). Đường dẫn `federation/*` là cam kết với Phase 9.

## Z4. Thư mục khách: OpenLDAP (osixia/openldap 1.5.0)

Image cũ (2021) nhưng là bản phổ biến nhất có bootstrap LDIF; chỉ dùng kiểm thử. Dữ liệu sinh bằng
`scripts/dev-gen-ldap.py` (seed cố định → chạy lại cho cùng dữ liệu, cần cho nghiệm thu Phase 10 "chạy lại không trùng"):
`BID_LDAP_USERS` (mặc định 10 000) người `inetOrgPerson` tên nhiều nước, 8 nhóm `groupOfNames` theo phòng ban. Giá trị
ngoài ASCII mã hoá base64 trong LDIF (RFC 2849). Tệp sinh ra không commit.

## Z5. Máy nhận thư dev: Mailpit 1.30.0

Nhận SMTP mọi địa chỉ (không gửi ra ngoài), API `GET /api/v1/search` để phép thử đọc thư đặt lại mật khẩu/lời mời.

## D8. Biên bản duyệt hợp đồng tích hợp 1.0

**Bối cảnh.** Đặc tả Phase 0 (đã sửa theo yêu cầu chủ sản phẩm ngày 2026-11-25: "bạn tự duyệt luôn, sửa spec lại"):
kỹ sư chính tự duyệt `docs/05`, ghi biên bản ở đây.

**Duyệt ngày 2026-11-25, người duyệt: kỹ sư chính (Claude).** Danh mục đã đối chiếu:

| Mục | Kết quả |
|---|---|
| Đủ claims của đặc tả 2.3 (`sub`, `tid`, `sid`, hồ sơ, `acct`, `home_tid`, `amr/acr/auth_time`, `roles`, `scp`, `lic`, `groups/hasgroups` ngưỡng 200, `xms_cc`, `client_id/azp`, `idtyp`) | Đủ; thêm `hour_cycle` (đặc tả 6.7 yêu cầu mọi sản phẩm cùng cài đặt 12/24 giờ) và `ver` |
| Đủ 19 loại sự kiện của đặc tả 2.4 | Đủ, định danh `urn:blueid:event:<loại>:v1` |
| SET đúng RFC 8417; đẩy RFC 8935 (202, lỗi 400 dạng `err`); kéo RFC 8936 | Đúng |
| Thứ tự trong từng đối tượng, chống trùng `jti` | `bid_seq` theo đối tượng + `jti` |
| Thách thức lại (`insufficient_claims`) có định dạng cụ thể | Có (7.6) |
| Back-Channel Logout 1.0: logout token đúng chuẩn | Có `events` đúng URI, không `nonce` |
| RFC 8707: một thẻ một `aud` | Có; phạm vi không tiền tố + `resource` bắt buộc |
| Điều cấm của đặc tả mục 3 (implicit, hybrid front-channel, ROPC, PKCE plain) có mã lỗi | Có (mục 4) |
| API danh bạ: delta, lọc, chọn trường, con trỏ, sắp có khoá duy nhất cuối, đếm cùng tập dòng (bài học 9) | Có (mục 8) |
| API nội bộ: kiểm mật khẩu ứng dụng, tra theo địa chỉ, trạng thái phiên | Có (mục 10); cùng kiểm tra như đăng nhập web (bài học 8) |
| Chính sách phiên bản: chỉ thêm, bỏ qua thứ không biết, chu kỳ báo trước | Có (mục 1; `Deprecation`/`Sunset`, 12 tháng) |
| **Tương thích BlueMail hiện tại** (đọc `D:\Source\BlueMail`: docs/00 D3, D25; docs/05 "BlueID (OIDC)"; `OidcController`) | Giữ nguyên đường dẫn `/connect/*`; bảng chuyển claim `bm_tenant→tid`, `bm_sid→sid`; bỏ `bm_stamp` thay bằng sự kiện; giữ Guid người dùng làm `sub`; mật khẩu ứng dụng chuyển sang API nội bộ; Argon2id cũ kiểm được (D1). Thay đổi bắt buộc phía BlueMail: gửi `resource`, kiểm `aud` mới, nhận SET |

Những điểm cố ý **không** có trong 1.0 (thêm được về sau mà không phá vỡ): claim nhóm theo tên (chỉ có mã), sự kiện
`org_unit.*`, webhook không ký.

**Hệ quả.** Từ Phase 1, mọi thay đổi claims/sự kiện/API phải sửa `docs/05`, tăng MINOR, thêm dòng mục 14 và phép thử
hợp đồng.

## D9. Bí mật dev nằm trong git

`deploy/.env.dev`, mật khẩu Keycloak/LDAP/người dùng mẫu, `BID_MASTER_KEY` dev: cố ý commit (chỉ dùng cho tên miền
`.test`, mạng dev). Repo GitHub `minhhung19872002/BlueID` để **public** → bí mật production **không bao giờ** nằm trong
repo; production đọc từ biến môi trường của máy chủ (`docs/04`).

## D10. ASP.NET Core Identity: dùng bộ băm, không dùng `UserManager`

**Bối cảnh.** Đặc tả: người dùng/mật khẩu trên ASP.NET Core Identity; nhưng mọi ghi danh bạ phải qua **một**
`IDirectoryWriter` (mục 11) và mật khẩu nằm ở `auth.passwords` (mục 4, có lịch sử). `UserManager` + store EF của Identity
ghi thẳng vào bảng người dùng (và bảng `AspNet*`), đi vòng qua cả hai luật.

**Quyết định.** Dùng các abstraction của Identity — `IPasswordHasher<User>` (cài bằng `Pbkdf2PasswordHasher`, D1) và
`PasswordVerificationResult` (kể cả `SuccessRehashNeeded`) — trong `PasswordService`; người dùng là entity riêng
`directory.users`. Không có bảng `AspNet*`. Cookie phiên là cookie authentication của ASP.NET Core (D11).

**Hệ quả.** Đổi mật khẩu đi qua `IDirectoryWriter.ChangePasswordAsync` (thu hồi mọi phiên, đổi security stamp, sự kiện
`user.credentials_changed` trong cùng giao dịch). Phase 3 thêm lịch sử N mật khẩu và danh sách mật khẩu lộ vào
`PasswordService.Validate`.

## D11. Phiên trình duyệt của BlueID và cổng tài khoản/quản trị

- Cookie `__Host-bid_session` (dev HTTP: `bid_session`): `Secure; HttpOnly; SameSite=Lax; Path=/`, nội dung bảo vệ bằng
  Data Protection, vòng khoá chung trong `auth.data_protection_keys` (Web và Api cùng đọc; nhiều nút dùng chung).
- **Mỗi yêu cầu** kiểm lại dòng `auth.sessions` (chưa thu hồi, chưa hết hạn), trạng thái người dùng, trạng thái tenant và
  security stamp: khoá tài khoản/thu hồi phiên có tác dụng ngay với trang của chính BlueID (phép thử
  `Disabled_user_cannot_sign_in_and_loses_open_sessions_at_once`). Một truy vấn có chỉ mục; chưa cần bộ đệm.
- Portal (React) là ứng dụng first-party **cùng origin** với BlueID (Caddy chia đường dẫn: `/me/v1`, `/admin/v1`,
  `/directory`, `/internal/v1`, `/scim` → Api; còn lại → Web), nên dùng thẳng cookie phiên — đúng mô hình BFF, trình
  duyệt không cầm thẻ. Chống CSRF cho API: mọi lệnh ghi phải có tiêu đề `X-BlueID-CSRF` (biểu mẫu chéo trang không đặt
  được; script khác origin cần pre-flight CORS mà BlueID không cho) cộng `SameSite=Lax`. Trang Razor dùng anti-forgery
  token chuẩn.
- CSP portal: Ant Design chèn `<style>` lúc chạy → `index.html` của portal được phục vụ kèm **nonce ngẫu nhiên mỗi phản
  hồi** và `style-src 'self' 'nonce-…'` chỉ cho phản hồi đó; không bao giờ `'unsafe-inline'` (BUG-005).

## D12. Chống dò mật khẩu ở Phase 1

- Khoá mềm theo cặp **(tài khoản, IP)**: 5 lần sai đầu miễn phí, sau đó trễ 2^(n−5) giây, tối đa 15 phút; bộ đếm tăng
  nguyên tử ở CSDL (`INSERT … ON CONFLICT … RETURNING`, phép thử 6 lần sai song song). Kẻ dò từ IP khác **không** khoá được
  chủ tài khoản (phép thử `Parallel_wrong_passwords_are_all_counted`).
- Giới hạn theo IP: 30 lượt POST trang đăng nhập/phút (`RateLimiter`).
- Thời gian trả lời: mọi nhánh tốn đúng một lần PBKDF2 và có sàn 400 ms (`SignInService.MinimumDuration`), nên truy vấn
  phụ của nhánh "có tài khoản" không lộ ra; trang trả lời giống hệt nhau (phép thử so sánh HTML).
- Phase 3 bổ sung: phát hiện dò hàng loạt nhiều tài khoản từ một IP → thử thách thêm; giới hạn theo tài khoản trên mọi IP.

## D13. Enum, chỉ mục và dữ liệu mẫu

- Enum lưu dạng SNAKE_UPPER (`ACTIVE`, `LDAP_SYNC`) — cùng chữ với đặc tả mục 4 và API.
- Chỉ mục duy nhất đặt tên tường minh (`ux_…`) và luôn có `WHERE deleted_at IS NULL`; tên tự sinh của EF có thể vượt 63
  ký tự của PostgreSQL. `DirectoryWriter` đổi vi phạm duy nhất thành mã lỗi ổn định theo tên chỉ mục.
- Dữ liệu mẫu (`BID_SEED_SAMPLE_DATA=true`): tenant nền tảng (`admin@<BID_PLATFORM_DOMAIN>`, buộc đổi mật khẩu lần đầu),
  `bluemail.test` (en, 40 người) và `acme.test` (vi, 45 người), tên nhiều nước, phòng ban + quản lý, một người bị khoá;
  quản trị viên `admin@bluemail.test`, `admin@acme.test`.

## Bằng chứng Phase 1 (2026-11-25)

- `dotnet test BlueID.slnx`: 32 unit + 19 tích hợp (PostgreSQL 16 thật qua Testcontainers) xanh; `npx vitest run` 3 xanh.
- `python scripts/dev-smoke-login.py` (tiến trình dev) và `python scripts/dev-smoke-login.py https://localhost:58443`
  (image `blueid/server` + `deploy/docker-compose.yml` chạy thật, project `blueid-trial`): **PASS 11/11**, có HSTS, cookie
  `__Host-`. Ảnh chụp Playwright 1366×768 và 375×812: không cuộn ngang.

## D14. PKCE bắt buộc và bộ kiểm thử tuân thủ OpenID (quyết định trước Phase 2)

**Bối cảnh.** Đặc tả mục 0 (nguyên tắc 4) và mục 3: PKCE bắt buộc cho **mọi** máy khách, kể cả máy khách bí mật. Đặc tả
mục 7 và 10: phải **qua** bộ kiểm thử tuân thủ OpenID (Basic OP, Config OP, Form Post OP, RP-Initiated Logout,
Back-Channel Logout). Đọc mã nguồn bộ kiểm thử `release-v5.3.4` (`.cache/conformance-suite`): các bài OIDCC chuẩn
**không gửi** `code_challenge`; chỉ bài `oidcc-ensure-request-with-valid-pkce-succeeds` (`OIDCCEnsureRequestWithValidPkceSucceeds.java`)
gửi PKCE. Bắt PKCE với máy khách của bộ kiểm thử thì gần như mọi bài của hồ sơ Basic/Form Post trượt ở bước authorize.

**Quyết định.** PKCE `S256` vẫn bắt buộc cho mọi máy khách thật. Thêm một ngoại lệ hẹp, có canh:

- Ứng dụng có cờ `conformance_test_client = true` thì không bị bắt PKCE (nhưng gửi PKCE thì vẫn được kiểm đúng, và
  `plain` vẫn bị từ chối).
- Cờ này **chỉ tạo được** khi tiến trình chạy với `BID_CONFORMANCE_MODE=true` (chỉ có trong `deploy/.env.dev` và CI),
  chỉ cho ứng dụng thuộc tenant riêng `conformance` do kịch bản kiểm thử tạo; production không đặt biến này nên không có
  đường nào tạo ra cờ. Khi khởi động mà không có `BID_CONFORMANCE_MODE=true`, mọi ứng dụng mang cờ bị **tắt** (ghi log
  cảnh báo, ghi kiểm toán) — một bản sao CSDL dev lọt sang production cũng không mở được lỗ.
- Mọi phần còn lại của cấu hình khi chạy bộ kiểm thử giống hệt production (cùng image, cùng mã, cùng thời hạn thẻ).

**Bằng chứng phải có ở Phase 2.** (1) Bộ kiểm thử qua cả 5 hồ sơ; (2) phép thử âm: máy khách thường thiếu PKCE →
`invalid_request`, `plain` → `invalid_request` (kể cả với máy khách của bộ kiểm thử); (3) phép thử: khởi động không có
`BID_CONFORMANCE_MODE` thì ứng dụng mang cờ bị tắt; (4) phép thử: API quản trị không đặt được cờ.

**Hệ quả.** Chứng nhận/đối chứng là của đúng mã và cấu hình chạy thật, chỉ khác yêu cầu PKCE của riêng máy khách kiểm
thử — điều mà chính bộ kiểm thử OpenID coi là tuỳ chọn của OIDC. Ghi rõ trong `docs/07` cạnh kết quả chạy.

## D15. Lõi OAuth/OIDC trên OpenIddict 7.9 (Phase 2)

- **Chế độ passthrough**: OpenIddict kiểm giao thức (client, redirect URI khớp chính xác, PKCE, quyền theo grant/scope/
  resource của client, mã một lần); `OidcController` quyết định người dùng là ai, thẻ ghi gì (`TokenClaims` = hợp đồng
  docs/05 5.3) và có được cấp không (phiên còn sống, người dùng/tenant hoạt động, đồng ý, ứng dụng nhiều tenant).
- **Chỉ bật đúng thứ hợp đồng có**: code + refresh + client credentials; PKCE chỉ `S256` và bắt buộc theo từng client
  (`ft:pkce`, D14); bỏ `client_secret_jwt`, `fragment`; implicit/hybrid/password không bao giờ được bật.
- **Resource Indicators**: tài nguyên nằm trong `apps.resources` (thêm lúc chạy) nên tắt kiểm resource tĩnh của OpenIddict
  (`DisableResourceValidation`); `OidcController.ValidateTargetAsync` kiểm thay: tối đa một resource, resource phải có
  thật, phạm vi phải thuộc đúng resource; quyền `rsrc:` của client vẫn do OpenIddict kiểm.
- **Khoá ký** (`keys.signing_keys`): RSA 3072, khoá riêng mã hoá AES-256-GCM bằng khoá con HKDF của `BID_MASTER_KEY`.
  Vòng đời NEXT → ACTIVE (sau 90 ngày và khi NEXT đã công bố ≥ 48 giờ) → RETIRING (91 ngày, vẫn trong JWKS và vẫn được
  chấp nhận) → RETIRED (xoá khoá riêng). Mỗi nút nạp lại 30 giây/lần; tập khoá đổi thì change token của
  `IOptionsMonitor<OpenIddictServerOptions>` kích hoạt, thẻ mới ký bằng khoá mới ngay, không khởi động lại (bài học 5;
  phép thử `Key_rotation_reaches_tokens_and_jwks_without_a_restart`). Xoay chạy một nút mỗi lần (advisory lock).
  ES256: hợp đồng cho phép, nhưng 1.x chỉ ký RS256 (chọn thuật toán theo client: Phase 6).
- **Một lần là một lần** (bài học 1): `auth.used_tokens` (khoá chính `kind, value`) cho refresh token (`oi_tkn_id`) và
  `client_id:jti` của client assertion. OpenIddict không nguyên tử ở cả hai (BUG-010, BUG-011). Bên thua khi đổi refresh
  token bị coi là dùng lại: thu hồi cả authorization.
- **Trần 90 ngày** của refresh token tính từ lúc đăng nhập (OpenIddict chỉ có trượt 14 ngày).
- Thẻ người dùng gắn với **phiên BlueID**: mỗi lần đổi mã/làm mới hỏi lại `SessionService.ValidateAsync` (cũng gia hạn phiên),
  nên đăng xuất/khoá/đổi mật khẩu làm refresh token chết ngay.
- **Đăng xuất kênh sau**: `auth.session_clients` ghi ứng dụng nào đã nhận thẻ qua phiên nào; kết thúc phiên (mọi lối:
  đăng xuất, quản trị thu hồi, khoá, đổi mật khẩu) xếp `auth.logout_deliveries` trong cùng giao dịch; bộ gửi lấy hàng bằng
  `FOR UPDATE SKIP LOCKED`, thử lại 1 s → 10 phút trong 72 giờ; 4xx (trừ 408/429) dừng ngay; đổi/gỡ URI huỷ hàng chờ (BUG-017).
- **Đăng xuất do ứng dụng khởi tạo**: chỉ chuyển hướng tới `post_logout_redirect_uri` khi `id_token_hint` hợp lệ (BUG-016).
- **Trang lỗi OAuth**: lỗi mà OpenIddict không gửi về client được (redirect URI sai, thiếu `response_type`…) → trang
  `/oauth-error` có dịch, chỉ hiện mã lỗi; chỉ cho endpoint authorize và end-session (BUG-015). Request object (OIDC Core 6)
  không hỗ trợ: trả `request_not_supported` về client theo `response_mode` của yêu cầu (BUG-014).
- Khoá Data Protection (cookie phiên) mã hoá khi lưu bằng khoá chủ (`MasterKeyXmlEncryptor`).
- Giới hạn trang đăng nhập 300 POST/phút/IP (`BID_SIGNIN_IP_LIMIT_PER_MINUTE`, BUG-012); chống dò chính là khoá mềm theo
  tài khoản (D12).

**Còn thiếu so với hợp đồng (làm ở Phase 5, phase sự kiện):** phát hiện dùng lại refresh token đã thu hồi cả chuỗi, nhưng
**chưa** phát sự kiện `user.sessions_revoked` (lý do `refresh_token_reuse`, docs/05 4); dùng lại tuần tự do OpenIddict bắt
trước khi tới `OidcController`, nên cần một handler riêng. Chọn ES256 theo client và cổng loopback tuỳ ý của RFC 8252:
Phase 6.

## D16. `client_secret_post` theo từng ứng dụng, mặc định tắt

**Bối cảnh.** Hợp đồng 1.0 không có `client_secret_post`; hồ sơ chứng nhận Basic OP bắt buộc thử cả `client_secret_basic`
lẫn `client_secret_post` (`OIDCCBasicTestPlan.java`: "the certification profile requires that both basic and post are
tested"). **Quyết định.** Bật phương thức ở máy chủ, nhưng `ClientSecretPostGuard` chỉ chấp nhận từ ứng dụng có
`allow_client_secret_post = true` (mặc định `false`; mọi ứng dụng hiện có giữ nguyên hành vi, phép thử
`Client_secret_post_and_wrong_secrets_are_refused`). Bộ kiểm thử dùng máy khách riêng `conformance-post`.
**Hệ quả.** Hợp đồng 1.1 ghi đây là tuỳ chọn theo ứng dụng (chỉ thêm, không phá vỡ).

## D14 (bổ sung Phase 2)

Máy khách kiểm thử: `conformance-1/2` (mọi hồ sơ), `conformance-3/4` (có `backchannel_logout_uri`, chỉ cho hồ sơ
Back-Channel Logout), `conformance-post` (D16). Ở chế độ tuân thủ, HttpClient gửi logout token chấp nhận chứng chỉ tự ký
của bộ kiểm thử (chỉ khi `BID_CONFORMANCE_MODE=true`).

## Bằng chứng Phase 2 (2026-11-25)

Bộ kiểm thử tuân thủ OpenID `release-v5.3.4` chạy với BlueID dev (`sh scripts/conformance/run.sh`), kết quả chi tiết
và lý do từng cảnh báo: `docs/07-giao-thuc.md` mục "Bộ kiểm thử tuân thủ OpenID". `dotnet test`: xem docs/06 Phase 2.
