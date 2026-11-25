# 07 — Giao thức và đặc tả (Protocol coverage)

Mỗi dòng: chuẩn, phase làm, trạng thái, bằng chứng. Trạng thái: ⬜ chưa làm · 🟨 đang làm · ✅ đã làm, có bằng chứng.

| Chuẩn | Phase | Trạng thái | Bằng chứng |
|---|---|---|---|
| OAuth 2.0 (RFC 6749), Bearer (RFC 6750) | 2 | ⬜ | |
| PKCE (RFC 7636), `S256` bắt buộc | 2 | ⬜ | |
| Refresh token xoay vòng, phát hiện dùng lại | 2 | ⬜ | |
| Client Credentials | 2 | ⬜ | |
| `private_key_jwt` (RFC 7523) | 2 | ⬜ | |
| Resource Indicators (RFC 8707) | 2 | ⬜ | |
| JWT access token (RFC 9068) | 2 | ⬜ | |
| Introspection (RFC 7662), Revocation (RFC 7009) | 2 | ⬜ | |
| OAuth AS Metadata (RFC 8414) | 2 | ⬜ | |
| OIDC Core 1.0, Discovery 1.0 | 2 | ⬜ | Bộ kiểm thử OpenID: Basic OP, Config OP, Form Post OP |
| OIDC RP-Initiated Logout 1.0 | 2 | ⬜ | Bộ kiểm thử OpenID |
| OIDC Back-Channel Logout 1.0 | 2 | ⬜ | Bộ kiểm thử OpenID |
| OAuth 2.0 Form Post Response Mode | 2 | ⬜ | |
| AMR values (RFC 8176) | 3 | ⬜ | |
| TOTP (RFC 6238), HOTP (RFC 4226) | 3 | ⬜ | |
| WebAuthn Level 2/3, CBOR (RFC 8949), COSE (RFC 9052/9053) | 3 | ⬜ | Trình xác thực ảo của Chrome |
| SET (RFC 8417), Push (RFC 8935), Poll (RFC 8936), Subject Identifiers (RFC 9493) | 5 | ⬜ | |
| Device Authorization (RFC 8628) | 6 | ⬜ | |
| Token Exchange (RFC 8693) | 6 | ⬜ | |
| PAR (RFC 9126) | 6 | ⬜ | |
| OAuth for Native Apps (RFC 8252) | 2 | ⬜ | |
| SAML 2.0 SP (Web Browser SSO, Redirect + POST binding) | 9 | ⬜ | Keycloak đối chứng; mẫu XSW1–8 |
| SCIM 2.0 (RFC 7643, 7644) | 10 | ⬜ | Máy khách SCIM độc lập |
| SAML 2.0 IdP | 11 | ⬜ | |
| IDNA2008 / UTS #46, SMTPUTF8 địa chỉ (RFC 6531), Unicode NFC, UTS #39 (đồng hình) | 1, 4 | ⬜ | |
| Deprecation (RFC 9745), Sunset (RFC 8594) | khi cần | ⬜ | |

**Không hỗ trợ (có phép thử khẳng định bị từ chối):** implicit, hybrid trả thẻ ở front-channel, ROPC.
