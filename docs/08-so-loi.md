# 08 — Sổ lỗi (Bug log)

Mỗi lỗi: ngày, phase, triệu chứng, cách tái hiện, nguyên nhân, cách sửa, phép thử đỏ trước/xanh sau.

| Mã | Ngày | Phase | Triệu chứng | Tái hiện | Nguyên nhân | Sửa | Phép thử |
|---|---|---|---|---|---|---|---|
| BUG-001 | 2026-11-25 | 0 | nginx của bộ kiểm thử OpenID khởi động lại liên tục: `host not found in upstream "server"`; server dừng: Spring báo đăng ký OAuth2 rỗng | `docker compose … up -d` lần đầu | Cấu hình nginx của bộ kiểm thử trỏ tới tên `server`; server cần `OIDC_GOOGLE_*`/`OIDC_GITLAB_*` không rỗng (compose gốc đặt giá trị giả) | Alias mạng `server`; đặt 4 biến giả như compose gốc | `scripts/dev-phase0-check.sh` mục "openid conformance suite api" (đỏ → xanh) |
| BUG-002 | 2026-11-25 | 0 | Server của bộ kiểm thử dừng: `MongoCommandException … downgraded the FCV … confirm: true` | Chạy với `mongo:7.0.25` | Bộ kiểm thử đặt FCV 6.0; mongo 7 đòi xác nhận khi hạ | Dùng `mongo:6.0.13` như compose gốc, xoá volume | Như trên (đỏ → xanh) |
