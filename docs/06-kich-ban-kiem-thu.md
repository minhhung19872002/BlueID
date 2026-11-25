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

## Phase 2 — Lõi OAuth/OIDC

Chạy: `dotnet test BlueID.slnx`; bộ kiểm thử tuân thủ: `sh scripts/conformance/run.sh` (dev compose + `scripts/dev-run.sh web`),
tóm tắt `python scripts/conformance/summary.py <plan-id>`.

| Mã | Kịch bản | Mong đợi | Bằng chứng | |
|---|---|---|---|---|
| P2-01 | Discovery | Chỉ `code`; PKCE chỉ `S256`; grant code/refresh/client credentials; có `backchannel_logout_supported`; RFC 8414 cùng nội dung; JWKS có khoá ACTIVE + NEXT | `Discovery_lists_only_what_the_contract_allows` | ✅ |
| P2-02 | Luồng code + PKCE | Access token `at+jwt`, RS256, `kid` ACTIVE, đúng một `aud`, claims đúng hợp đồng (không thừa), id token cùng `sid`, userinfo | `Code_flow_with_pkce_gives_contract_tokens_for_one_resource` | ✅ |
| P2-03 | Client credentials | `idtyp=app`, không refresh token | `Client_credentials_gives_an_application_token` | ✅ |
| P2-04 | Implicit, hybrid | `unsupported_response_type`, không thẻ trong redirect (5 dạng) | `Implicit_and_hybrid_are_refused` | ✅ |
| P2-05 | ROPC | `unsupported_grant_type` | `Password_grant_is_refused` | ✅ |
| P2-06 | Thiếu PKCE / PKCE `plain` | `invalid_request`, kể cả máy khách bí mật | `Missing_pkce_…`, `Plain_pkce_is_refused` | ✅ |
| P2-07 | Redirect URI lệch một ký tự (5 dạng) | Không bao giờ chuyển tới URI lạ; trang lỗi của BlueID | `Redirect_uri_one_character_off_never_redirects` | ✅ |
| P2-08 | Dùng lại mã | `invalid_grant`, thẻ đã cấp từ mã bị thu hồi | `Code_reuse_is_refused_and_revokes_what_it_gave` | ✅ |
| P2-09 | Dùng lại refresh token | Cả chuỗi bị thu hồi | `Refresh_token_reuse_revokes_the_whole_chain` | ✅ |
| P2-10 | Đổi refresh token song song | Đúng một bên thắng (BUG-010) | `Parallel_refresh_with_one_token_has_exactly_one_winner` | ✅ |
| P2-11 | Luật resource | Thiếu resource → `invalid_target`; scope tài nguyên khác → `invalid_scope`; hai resource → `invalid_target`; resource ngoài quyền client bị từ chối | `Resource_rules_of_the_contract` | ✅ |
| P2-12 | Xác thực máy khách | `client_secret_post` bị từ chối khi chưa đăng ký (D16); sai bí mật | `Client_secret_post_and_wrong_secrets_are_refused` | ✅ |
| P2-13 | `private_key_jwt` | Khoá đúng được; khoá lạ bị từ chối; assertion dùng lại bị từ chối (BUG-011) | `Private_key_jwt_works_and_a_foreign_key_does_not` | ✅ |
| P2-14 | Sản phẩm kiểm thẻ (ứng dụng mẫu) | Thẻ đúng 200; sai `aud`, hết hạn, sai `typ`, khoá lạ, `alg: none`, RS256→HS256 → 401 | `Product_api_accepts_only_its_own_good_tokens` | ✅ |
| P2-15 | Request object | `request_not_supported` về client, theo `response_mode` của yêu cầu (BUG-014) | `Request_object_is_refused_back_to_the_client` | ✅ |
| P2-16 | Userinfo | Không thẻ / thẻ rác / thẻ đã thu hồi → 401, không bao giờ chuyển hướng (BUG-015) | `Userinfo_answers_401_never_a_redirect` | ✅ |
| P2-17 | Đồng ý | Ứng dụng bên thứ ba: `prompt=none` → `consent_required`; trang đồng ý hiện tên ứng dụng và quyền; đồng ý được nhớ | `Third_party_app_asks_for_consent_once` | ✅ |
| P2-18 | `prompt` | `prompt=none` khi chưa đăng nhập → `login_required`; `prompt=login` buộc đăng nhập lại, chuyển `login_hint`; dấu `bid_reauth` giả hoặc dùng lại không lách được (BUG-009) | `Prompt_none_without_session_and_prompt_login_with_session` | ✅ |
| P2-19 | Đăng xuất kênh sau | Đăng xuất ở BlueID → ứng dụng mẫu nhận logout token hợp lệ và ngắt phiên của nó | `Sign_out_reaches_the_product_over_the_back_channel` | ✅ |
| P2-20 | Đăng xuất do ứng dụng khởi tạo | Chỉ chuyển hướng với `id_token_hint` hợp lệ (BUG-016) | `Logout_redirects_only_with_a_valid_id_token_hint` | ✅ |
| P2-21 | Giao logout token | 4xx dừng ngay; đổi URI huỷ hàng chờ (BUG-017) | `Refused_or_outdated_logout_deliveries_stop` | ✅ |
| P2-22 | Khoá tài khoản | Refresh bị từ chối; sự kiện `user.disabled` trong outbox | `Disabling_a_user_stops_refresh_and_tells_the_product` | ✅ |
| P2-23 | Trần 90 ngày | Refresh từ chối khi phiên đăng nhập quá 90 ngày | `Refresh_stops_after_ninety_days_from_sign_in` | ✅ |
| P2-24 | Xoay khoá | Thẻ mới ký bằng khoá mới ngay; khoá cũ vẫn trong JWKS; không khởi động lại | `Key_rotation_reaches_tokens_and_jwks_without_a_restart` | ✅ |
| P2-25 | D14 | Không tạo được máy khách miễn PKCE ngoài chế độ tuân thủ; máy khách mang cờ bị tắt khi khởi động | `Conformance_exemption_cannot_exist_outside_conformance_mode` | ✅ |
| P2-26 | Khoá chủ, khoá Data Protection | Mã hoá theo mục đích, phát hiện sửa; khoá cookie không còn ở dạng rõ | `MasterKeyTests` | ✅ |
| P2-27 | **Bộ kiểm thử tuân thủ OpenID** (5 hồ sơ) | Không bài nào FAILED; cảnh báo/REVIEW có lý do ghi trong docs/07 | `scripts/conformance/run.sh` | xem docs/07 |
