# PROJECT_AUDIT — LMS-AI

**Cập nhật sau bàn giao:** đã backup/baseline/migrate database local gốc, xác minh giữ nguyên dữ liệu cũ và health/readiness/Swagger HTTP200. [Biên bản xử lý](docs/STARTUP_DATABASE_FIX.md). Những mô tả hash gốc/chỉ migrate bản sao bên dưới là bằng chứng của pha trước lần xử lý này.

Ngày23/10/2026. Source hiện tại được kiểm tra trực tiếp trong workspace; không có .git nên không xác nhận branch, lịch sử hoặc đồng bộ GitHub. Các ghi chú ZIP/develop trước đây là lịch sử, không phải bằng chứng current remote.

## Kết luận bàn giao

Giữ kiến trúc Layered N-Tier Modular Monolith,4 project nghiệp vụ ASP.NET Core10/EF10/Domain/Infrastructure và React19/Vite. Đã hoàn thiện các luồng cơ bản cho3 vai trò, persistence, AI adapter thật, tests và cấu hình triển khai.

**Chưa được gọi là100% production-ready:** còn Gemini live, UI browser, SQL Server runtime và remote CI chưa được kiểm chứng. Không deploy/push/merge trong phiên này. Xem [checklist](PROJECT_TASKS.md), [evidence](docs/VERIFICATION.md) và [giới hạn](docs/TRANG_THAI_VA_GIOI_HAN.md).

## Hiện trạng lúc tiếp quản

- Có phần lớn mã nền và SQLite13 bảng nhưng chưa có migrations; tài liệu cũ chỉ báo static checks.
- Máy hiện tại có .NET SDK10.0.302/runtime10.0.10, Node25.2.1/npm11.2.0, Python3.12.9; restore/build khả dụng.
- Source gốc build được nhưng có cảnh báo NuGet SQLite/OpenAPI/Pkcs; không dùng tài liệu thiếu SDK cũ làm blocker nữa.
- Database gốc có3 tài khoản và1 Course, các bảng nghiệp vụ khác rỗng. Đọc schema/count, không đọc/in credential.
- Thiếu password/profile/logout, topic edit, quiz draft description/selection/preview, feedback đầy đủ, recommendation tổng hợp, HTTP tests, upgrade path và UI đồng bộ.
- Browser runtime kết nối thất bại "No browser is available", discovery []; Docker CLI có nhưng daemon không hoạt động; không thấy Gemini key được cấu hình.

## Vấn đề đã sửa

| Mức | Vấn đề và nguyên nhân | Cách xử lý / bằng chứng |
|---|---|---|
| P0 | Dependencies báo advisory mức cao/trung bình | EF/JWT10.0.10, Swagger10.3.1, SQLite bundle3.0.5, Pkcs10.0.13; build0 warnings, NuGet/npm audit không báo vulnerable package |
| P1 | Thiếu hồ sơ/password/logout và quản lý session cũ | Profile DB, password hashing/current password, SecurityVersion, JWT revalidation/role/lock; HTTP+security+hosting tests |
| P1 | Admin và frontend chưa đủ workflow | API detail/pagination/create/lock, UI thật theo role; API/client/build đạt, browser acceptance vẫn chặn |
| P1 | Course/Topic/Quiz còn thao tác thiếu | JoinCode-only/detail/count/topic PUT; Quiz Description/Draft selection/update/authoring/questionCount; service+HTTP |
| P1 | Validation null/options/payload và publish lệch phạm vi | Chính xác4 option/1 correct, same-course Approved, invalid input400; smoke/security/HTTP |
| P1 | Stale child-only edit có thể vượt approve/publish | Status concurrency token và forced parent update, transaction rollback; regression giữ parent text không đổi |
| P1 | Cạnh tranh enrollment/attempt | Unique index và precise409; barrier tests ép hai luồng qua precheck, HTTP concurrent tests |
| P1 | Lịch sử và recommendations chưa đầy đủ | Freeze Approved/Published, feedback chi tiết, cumulative enrolled-topic<60%, nullable material; smoke/HTTP |
| P1 | Storage/file metadata có trường hợp cleanup sai hoặc thiếu kiểm quyền | Bounded actual bytes, path/reparse checks, private stream, rollback/ambiguous commit handling; security tests |
| P1 | AI adapter thiếu structured schema/robust parsing/error bounds | API hiện hành, schema+strict validator, partial-valid Draft, sanitized errors/timeout/cancellation;65 security fixtures, chưa Gemini live |
| P2 | Seed hardcode credential, môi trường production chưa đủ guard | Opt-in password từ secret, first-admin bootstrap không promote/reset, CORS/JWT/Mock/demo/proxy/rate guards;47 hosting checks |
| P2 | EnsureCreated không có upgrade path cho DB cũ | Hai provider migrations; fail-safe legacy detection; schema-verified backup/baseline utility,22 tests và real EF copy upgrade |
| P2 | UI state/lỗi/session/nhấn lặp thiếu nhất quán | Shared hooks/API errors/cancellation, role pages/forms;13 client tests+Vite build |
| P3 | Tài liệu sai hiện trạng và thiếu bằng chứng runtime | Rewrite README/deployment/audit/tasks/API/DB/evidence/giới hạn; giữ UI/live chưa nghiệm thu rõ |

## Bảo toàn dữ liệu

File SQLite gốc SHA256 vẫn `5FC6446647F2BC009076746BDE1E0E5A7936A67EB5FB4FFD3F283E3E4C6F8C26`. Không migrate/xóa/reset trực tiếp file này, không đổi mật khẩu đang có, không xóa Uploads cũ.

Thử nâng cấp bản sao: backup nhất quán → adopt đúng baseline → EF migration thật thêm SecurityVersion/Quiz.Description.13 bảng/4 hàng legacy giữ nguyên từng giá trị; FK/integrity/defaults đúng; original hash không đổi. API từ chối database có bảng mà không có migration history. Người vận hành thực hiện công cụ baseline có backup trước lần chạy bản mới, hướng dẫn [DEPLOYMENT](DEPLOYMENT.md).

## Bằng chứng cuối

- Restore/build Release thành công,0 warnings/0 errors; publish API thành công.
- Smoke96, Security65, HTTP105, Hosting47: **313 kiểm tra .NET đạt**.
- Python22 unit tests + real EF migration trên bản sao đạt.
- FE13 client tests, npm build38 modules thành công; npm audit0 vulnerabilities.
- Kestrel/Vite khởi động thật qua HTTP loopback: readiness/Swagger/HTML/module200, CORS đúng origin; đã dừng sau thử.
- EF snapshot cả2 provider không pending model changes; SQL Server idempotent script sinh được; Compose config valid.
- NuGet audit cả8 projects không báo vulnerable package theo nguồn hiện tại. Đây không thay thế kiểm toán bảo mật hoặc browser acceptance.

Log cục bộ được lưu trong artifacts/verification (gitignored); [VERIFICATION](docs/VERIFICATION.md) mô tả lệnh, phạm vi và giới hạn để tái lập.

## Rủi ro và việc còn cần xác minh

Gemini thật chưa có key/quota hợp lệ; không thể xác nhận yêu cầu TEST15. Browser không có instance;52 bước UI vẫn UNVERIFIED. SQL Server server/Docker daemon chưa chạy; chỉ script/config đã kiểm tra. Remote CI chưa được chạy do thiếu metadata repository. Không ước lượng phần trăm hoàn thành.

Các danh sách nghiệp vụ hiện trả list trừ Admin có pagination; cần bổ sung API+UI pagination khi dữ liệu lớn, chưa có tải thực để biện minh tối ưu thêm. Upload file lưu local cần backup cùng DB. Môi trường deploy phải cấu hình HTTPS/proxy, persistent paths, secrets và kiểm thử restore. Không tự triển khai production.

Ngoài MVP: PDF/OCR extraction, email password reset, timer/proctoring, thanh toán/marketplace, AI provider khác và hệ thống phân tán.



C?p nh?t env ng?y 24/10/2026: Backend ?? h? tr? t? ??c .env ? g?c repository, env/CLI ?u ti?n ghi ??; t?o .env local v?i JWT key ng?u nhi?n v? FE/.env.local tr? localhost:5088, kh?ng thay database/t?i kho?n. Xem docs/ENV_SETUP.md. Release build 0 warning/error, Hosting53 v? HTTP105 ??t; health/readiness th? c?ng15089 HTTP200; AI Disabled v? ch?a c? key th?t. API Debug ?ang ch?y c?a ng??i d?ng gi? nguy?n, c?n Ctrl+C v? ch?y l?i ?? n?p code/env m?i.


Checkpoint29/10/2026 Gemini: ??c danh s?ch model t? Google HTTP200, model c?u h?nh c? trong danh s?ch. M?t probe sinh r?t nh? kh?ng d?ng t?i li?u user h?t th?i gian ch?25s; ch?a x?c ??nh ch?nh x?c upstream HTTP c?a l?i user tr??c ??. Adapter ?? ph?n lo?i l?i Google402/429/400/401/403/404/500/503/408/504, tr? m? HTTP an to?n v? log numeric code, kh?ng raw response/key/document, kh?ng retry ho?c ??i model; b? candidateCount kh?ng c?n thi?t. Release build0 warning/error; Security73 v? HTTP105 ??t. API Debug user ?ang ch?y kh?a file; c?n restart ?? n?p binary m?i. Kh?ng thay API key/model ho?c d? li?u nghi?p v?.

B? sung cu?i29/10/2026: ?? x?c minh ??ng executable d? ?n, d?ng ti?n tr?nh API Debug c? gi? DLL v? kh?i ??ng l?i th?nh c?ng v?i adapter m?i; localhost:5088 readiness HTTP200. Kh?ng c?n c?n ng??i d?ng t? restart cho l?n s?a n?y. Ch?a sinh c?u h?i th?t th?nh c?ng.


Checkpoint29/10/2026 timeout: local .env Ai__TimeoutSeconds t?ng45?120 (trong gi?i h?n hi?n c?5..120); kh?ng ??i API key/model, kh?ng retry, kh?ng g?i generation m?i. Timeout message hi?n th? s? gi?y c?u h?nh v? h??ng d?n3 c?u/TXT ng?n. Release build0 warning/error; Security73 v? HTTP105 ??t (fixtures). API ???c restart, readiness HTTP200. T?ng th?i gian ch? kh?ng kh?c ph?c Google503 v? ch?a ch?ng minh Gemini sinh th?nh c?ng.


Checkpoint ch?n ?o?n29/10/2026: ?? x?c ??nh th?ng ?i?p upstream th?c t? b?ng1 request t?ng h?p t?i gi?n t?i c?ng model/key (kh?ng t?i li?u/schema, kh?ng retry): HTTP503 UNAVAILABLE sau1,84s; Google b?o model ?ang high demand. Kh?ng ph?i k?t lu?n ch? t? label c?a app. Kh?ng ??i model/key/billing/code/database trong l?n ch?n ?o?n; xem docs/GEMINI_DIAGNOSIS_2026-10-29.md v? artifact ?? lo?i secret. Ch?a sinh c?u h?i th?nh c?ng.
