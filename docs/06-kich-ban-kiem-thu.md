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

## Phase 1 — Nền móng

Chạy: `dotnet test BlueID.slnx` (cần Docker; portal đã build: `cd web && npm run build`), `cd web && npx vitest run`,
`python scripts/dev-smoke-login.py [base-url]`.

| Mã | Kịch bản | Mong đợi | Bằng chứng | |
|---|---|---|---|---|
| P1-01 | Đăng nhập mật khẩu | Trang 2 bước → cookie phiên → `/me/v1` trả người dùng | `Password_sign_in_gives_a_session_the_api_accepts`; smoke | ✅ |
| P1-02 | Không lộ tài khoản | Sai mật khẩu và không có tài khoản: cùng thông báo, cùng HTML, không cookie | `Wrong_password_and_unknown_user_get_the_same_answer` | ✅ |
| P1-03 | Khoá tài khoản | Không đăng nhập được; phiên đang mở bị từ chối ngay; outbox `user.created`, `user.disabled` thứ tự 1, 2 | `Disabled_user_cannot_sign_in_and_loses_open_sessions_at_once` | ✅ |
| P1-04 | **Chéo tenant** | Đọc/sửa/khoá/thu hồi phiên người tenant khác → 404; danh sách không có; tạo người trong tên miền tenant khác → 422; khách hàng không vào API nền tảng | `Another_tenant_is_invisible_on_every_admin_endpoint` | ✅ |
| P1-05 | Quyền và CSRF | Người không có vai trò quản trị → 403; ghi thiếu `X-BlueID-CSRF` → 403; múi giờ sai → 400 có `field` | `User_without_admin_role_is_forbidden_and_writes_need_the_csrf_header` | ✅ |
| P1-06 | Đồng thời | 3 yêu cầu song song thật cùng tên đăng nhập (khác hoa thường) → đúng 1 thành công | `Three_truly_parallel_creations_of_one_sign_in_name_give_exactly_one_user` | ✅ |
| P1-07 | Dò mật khẩu | 6 lần sai song song đều được đếm; IP dò bị khoá mềm; chủ tài khoản ở IP khác vẫn vào | `Parallel_wrong_passwords_are_all_counted` | ✅ |
| P1-08 | Buộc đổi mật khẩu | Không có cookie trước khi đổi | `Required_password_change_gives_no_session_until_done` | ✅ |
| P1-09 | Nhật ký kiểm toán | Chuỗi băm hợp lệ; sửa một dòng bị phát hiện đúng dòng | `Audit_chain_verifies…`, `Tampering_with_an_audit_row_is_detected` | ✅ |
| P1-10 | Cửa vào | U+0000 trong query → 400; trong JSON bị bỏ | `Nul_characters_are_refused_at_the_door` | ✅ |
| P1-11 | Canh quyền | Mọi action API có `[RequirePermission]`/`[SelfGuarded]` | `Every_api_endpoint_states_its_guard` | ✅ |
| P1-12 | Migration | Mọi lớp có `[Migration]`; model không lệch migration | `Every_migration_class…`, `The_model_has_no_pending_changes…` | ✅ |
| P1-13 | Bộ lọc tenant đóng | Chưa đặt tenant → 0 dòng | `Tenant_filter_is_closed_when_no_tenant_is_set` | ✅ |
| P1-14 | Tiêu đề bảo mật, tiếng Việt | CSP, X-Frame-Options; trang `vi` không mã hoá thực thể | `Pages_send_strict_security_headers_and_answer_in_vietnamese` (BUG-003) | ✅ |
| P1-15 | Chuyển hướng mở | `ReturnUrl` ngoài bị bỏ | `Open_redirects_from_the_sign_in_page_are_refused` | ✅ |
| P1-16 | CSP portal | Nonce mới mỗi phản hồi, khớp CSP, không `unsafe-inline` | `Portal_index_carries_a_fresh_style_nonce_matching_its_csp` (BUG-005) | ✅ |
| P1-17 | Băm mật khẩu, IDNA, NFC | Tiền tố thuật toán, băm lại khi số vòng thấp, chuỗi hỏng không ném lỗi; Punycode khớp bộ mã hoá của Python | `PasswordHasherTests`, `IdentifierTests` | ✅ |
| P1-18 | Dịch | en/vi đủ khoá (máy chủ và portal, mọi cấp); mọi mã lỗi trong mã có câu dịch | `LocalisationTests`, `format.test.ts` | ✅ |
| P1-19 | Giấy phép | Mọi gói NuGet/npm có trong docs/09 | `LicenceListTests` | ✅ |
| P1-20 | Giờ của người dùng | Ngày giờ theo múi giờ và 12/24 giờ của người dùng | `format.test.ts` | ✅ |
| P1-21 | Giao diện | 1366×768 và 375×812: không cuộn ngang; tương phản header (BUG-007) | Ảnh chụp Playwright 2026-11-25 | ✅ |
| P1-22 | Triển khai thật | Image + compose production qua Caddy HTTPS: smoke PASS, HSTS, cookie `__Host-` | `dev-smoke-login.py https://localhost:58443` | ✅ |
