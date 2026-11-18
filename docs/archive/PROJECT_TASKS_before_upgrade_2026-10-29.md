# PROJECT_TASKS — Phạm vi và bằng chứng

**Checkpoint sau bàn giao:** đã verify → backup/baseline → additive migration trên DB local đang có, đối chiếu dữ liệu với backup và thử Backend health/readiness/Swagger HTTP200. [Biên bản](docs/STARTUP_DATABASE_FIX.md). Database này đã hoàn tất bước1 ở mục Bước tiếp tục; bằng chứng hash không đổi/copy-only bên dưới thuộc pha trước nâng cấp thực tế.

Ngày23/10/2026. `[x]` đã làm và có phép kiểm tương ứng; `[!]` bị chặn nghiệm thu, kèm lý do; `[ ]` chưa làm. Không dùng build/client tests để thay cho browser hoặc Gemini live. **Tổng:66 mục đạt,0 mục chưa triển khai độc lập,8 mục bị chặn nghiệm thu** (Git1, SQL Server1, Gemini live1, browser5).52 bước UI chi tiết là checklist con, không cộng lặp. Chi tiết lệnh tại [VERIFICATION](docs/VERIFICATION.md).

## Audit và nền tảng

- [x] Đọc source/tài liệu theo các module, đối chiếu Controllers/DTOs/FE/Entities/config trước khi sửa; kiểm tra chéo giữa các phần.
- [x] Giữ4 project Layered N-Tier Modular Monolith, .NET10/React19/EF10, không thay kiến trúc.
- [!] Branch/lịch sử/remote hiện tại: thư mục không có .git; không đối chiếu develop hoặc xác minh remote CI được.
- [x] Restore toàn solution và build Release:0 warnings,0 errors.
- [x] Kiểm tra DI/routes/config/middleware bằng HTTP host và khởi động Kestrel loopback.
- [x] Chuẩn hóa JSON lỗi400/401/403/404/409/429/500, không trả stack/secret.
- [x] CORS allowlist và production thiếu/invalid config bị từ chối, kiểm thử hosting.
- [x] Rate limit login/register theo IP và AI theo user, HTTP429/Retry-After đã thử.
- [x] Security headers, Swagger chỉ Development, readiness xét pending migrations.
- [x] Nâng dependencies có advisory; audit NuGet/npm hiện không báo package dễ bị tấn công.
- [x] API publish Release thành công, không deploy lên server/domain.

## Database và dữ liệu

- [x] SQLite file ổn định, mới tạo bằng migrations, dữ liệu còn sau restart host.
- [x] Hai provider context/snapshot cùng model; generated migrations SQLite/SQL Server khớp model.
- [x] Unique CourseCode/JoinCode/Enrollment/Attempt, FK Restrict; test sai phạm vi và race.
- [x] Công cụ legacy mặc định read-only; schema/DDL/index/collation/FK/integrity được so sánh chặt.
- [x] Legacy --apply có khóa ghi/backup/baseline;22 unit tests, từ chối schema/history lạ.
- [x] Chạy EF upgrade thật trên bản sao file đang có:13 bảng/4 hàng legacy giữ nguyên từng giá trị.
- [x] Kiểm tra file SQLite gốc hash không đổi; không xóa/reset DB hoặc học liệu cũ.
- [x] SQL Server idempotent script và Compose config hợp lệ, không hardcode SA password.
- [!] SQL Server runtime/migrations/backup restore thật: Docker daemon chưa chạy, chưa có server kiểm thử.

## Authentication và Admin

- [x] Student register/login; duplicate username/sai password/input SQL injection không bypass.
- [x] JWT chữ ký/thời hạn/thiếu/sai/hết hạn; role từ Backend và session đối chiếu DB.
- [x] GET profile của tài khoản thật, không password hash trong DTO.
- [x] Đổi mật khẩu có current password; logout server; JWT cũ bị thu hồi.
- [x] Admin danh sách phân trang/detail/tạo Lecturer, không tạo Admin hoặc đổi role từ payload.
- [x] Khóa/mở khóa từ chối JWT cũ cả sau unlock, ngăn self-lock.
- [x] SecurityVersion optimistic concurrency, không phục hồi session cũ khi ghi đồng thời.
- [x] Bootstrap Admin đầu tiên và seed Development opt-in: generated test passwords, không hardcode credential, không nâng quyền/tự reset tài khoản sẵn có.
- [x] Production từ chối thiếu/short JWT, demo seed, Mock/InMemory hoặc config không hợp lệ.

## Course, Topic và Material

- [x] Lecturer tạo/sửa/detail/list Course sở hữu; code duy nhất, enrollment count/student roster.
- [x] Student join bằng JoinCode, chỉ thấy Course đã tham gia; enrollment race một200/một409.
- [x] Lecturer tạo/sửa Topic đúng Course; Student đọc Topic sau enrollment.
- [x] Upload TXT UTF-8/BOM và PDF, giới hạn5.000.000 byte thực, từ chối file sai/rỗng.
- [x] Download stream xác thực đúng owner/member, TXT/PDF trả lại nguyên byte.
- [x] Storage tên ngẫu nhiên, chặn traversal/reparse/outside delete.
- [x] DB/file rollback và kết quả commit chưa rõ không tạo DB tham chiếu file đã bị xóa.
- [x] Restart giữ Course/attempt/score/upload trong SQLite/thư mục cô lập.

## Question, Quiz, Results, Recommendation

- [x] Manual Question cùng cấu trúc AI, create Draft/edit/approve.
- [x] Validation đúng4 phương án khác nhau/1 đáp án đúng, null và malformed input trả lỗi.
- [x] Bank chỉ Lecturer owner; Student không đọc đáp án trước nộp.
- [x] Approved Question và Published Quiz khóa sửa; stale child-only edit không vượt approve/publish.
- [x] Quiz Draft có title/description/selection; lưu/sửa/authoring preview/questionCount.
- [x] Publish từ chối rỗng/Draft/câu Course khác; chỉ Approved cùng Course.
- [x] Student chỉ lấy Quiz Published khi enrolled; response không IsCorrect/explanation.
- [x] Backend chấm0–10; payload score/IsCorrect giả không đổi kết quả.
- [x] Submit một lần;2 request đồng thời chỉ1 Attempt, lưu answers/recommendations atomic.
- [x] Kết quả đúng/sai/accuracy/feedback/history; Student không xem người khác, Lecturer chỉ Course sở hữu.
- [x] Gợi ý cumulative Topic<60%, đúng60% không yếu, giới hạn Course hiện ghi danh.
- [x] Học liệu gợi ý có link tải, Topic chưa có material vẫn hiện accuracy với material null.

## Gemini

- [x] Endpoint/model/structured request đối chiếu tài liệu Google hiện hành, key chỉ Backend.
- [x] Disabled/missing key hiển thị rõ, không giả lập thành công và không ảnh hưởng luồng thủ công.
- [x] Giới hạn TXT/số câu/độ khó/response, timeout/cancellation, quota/error, không retry vô hạn.
- [x] JSON schema + validator chặt; lọc riêng câu sai, lưu câu hợp lệ Draft/Source=AI cùng log.
- [x] Chỉ owner chủ động gửi TXT; tài liệu đặt ở phần dữ liệu không tin cậy, không có external tools.
- [!] TEST15 Gemini live TXT→Draft→approve→Quiz: chưa có API key/quota được cấu hình để thử; transport fixtures không thay thế.

## Frontend

- [x] Tách pages/components/hooks vừa đủ;3 vai trò dùng API/DTO thực, FE production build.
- [x] Form validation/loading/success/error/empty state và chống submit lặp có code tích hợp;13 client tests xác minh HTTP/session/payload.
- [x] Session expiry/401/logout/password, abort request cũ và key theo account/Course; API/client tests.
- [!] Public/account forms qua browser: không có browser được kết nối.
- [!] Lecturer UI đầy đủ Course/Topic/Material/Question/AI/Quiz/results qua browser: cùng blocker browser.
- [!] Student UI join/material/quiz/result/history/recommendations qua browser: cùng blocker browser.
- [!] Admin UI tạo/list/detail/lock qua browser: cùng blocker browser.
- [!] Visual/responsive/mobile/keyboard/upload/refresh/navigation thực tế: cùng blocker browser;52 bước chi tiết trong docs/KIEM_THU_COBAN.md.

## Kiểm thử và tài liệu

- [x] Smoke/business96 kiểm tra đạt trên SQLite cô lập.
- [x] Security/AI transport65 kiểm tra đạt; không gọi provider.
- [x] HTTP integration105 assertions đạt, có auth/IDOR/concurrency/restart/persistence.
- [x] Hosting47 assertions đạt production guards/bootstrap/rate limits.
- [x] Database22 unit tests + copy-only real EF upgrade đạt.
- [x] FE13 client tests và Vite production build đạt.
- [x] Kestrel readiness/Swagger và Vite HTML/module trả200 qua HTTP loopback; không gọi đây là browser UI test.
- [x] CI cấu hình các gate Backend/Frontend/Python, không tạo lịch sử commit giả.
- [x] README/DEPLOYMENT/.env.example/API41 endpoint/Database/Architecture cập nhật.
- [x] Audit/task/evidence/changed-files và phân công6 thành viên cập nhật; giới hạn giữ rõ ràng.
- [x] Checklist52 bước UI và TEST01..15 cho người có browser/key; không đánh dấu UI/live đã đạt.

Ngoài phạm vi nghiệm thu phiên này: public VPS/domain deployment, marketplace/payment, timer/proctoring, reset mật khẩu email, OCR/PDF extraction, đa provider và hạ tầng phân tán. Đây không phải task còn thiếu của luồng LMS thủ công đã chốt.

## Bước tiếp tục

1. Khi muốn chạy với file DB legacy gốc, dừng API rồi thực hiện hai lệnh baseline trong README (verify → --apply tạo backup); phiên này chỉ chạy trên bản sao.
2. Cấu hình Gemini key bằng Backend secret và chạy TEST15, giữ kết quả Draft cho human review.
3. Kết nối browser và thực hiện52 bước nghiệm thu UI; xử lý lỗi thực tế nếu có.
4. Bật server SQL Server riêng, thử migrations và restore; đối chiếu repo thật trước khi tích hợp/CI.



C?p nh?t env ng?y 24/10/2026: Backend ?? h? tr? t? ??c .env ? g?c repository, env/CLI ?u ti?n ghi ??; t?o .env local v?i JWT key ng?u nhi?n v? FE/.env.local tr? localhost:5088, kh?ng thay database/t?i kho?n. Xem docs/ENV_SETUP.md. Release build 0 warning/error, Hosting53 v? HTTP105 ??t; health/readiness th? c?ng15089 HTTP200; AI Disabled v? ch?a c? key th?t. API Debug ?ang ch?y c?a ng??i d?ng gi? nguy?n, c?n Ctrl+C v? ch?y l?i ?? n?p code/env m?i.


Checkpoint29/10/2026 Gemini: ??c danh s?ch model t? Google HTTP200, model c?u h?nh c? trong danh s?ch. M?t probe sinh r?t nh? kh?ng d?ng t?i li?u user h?t th?i gian ch?25s; ch?a x?c ??nh ch?nh x?c upstream HTTP c?a l?i user tr??c ??. Adapter ?? ph?n lo?i l?i Google402/429/400/401/403/404/500/503/408/504, tr? m? HTTP an to?n v? log numeric code, kh?ng raw response/key/document, kh?ng retry ho?c ??i model; b? candidateCount kh?ng c?n thi?t. Release build0 warning/error; Security73 v? HTTP105 ??t. API Debug user ?ang ch?y kh?a file; c?n restart ?? n?p binary m?i. Kh?ng thay API key/model ho?c d? li?u nghi?p v?.

B? sung cu?i29/10/2026: ?? x?c minh ??ng executable d? ?n, d?ng ti?n tr?nh API Debug c? gi? DLL v? kh?i ??ng l?i th?nh c?ng v?i adapter m?i; localhost:5088 readiness HTTP200. Kh?ng c?n c?n ng??i d?ng t? restart cho l?n s?a n?y. Ch?a sinh c?u h?i th?t th?nh c?ng.


Checkpoint29/10/2026 timeout: local .env Ai__TimeoutSeconds t?ng45?120 (trong gi?i h?n hi?n c?5..120); kh?ng ??i API key/model, kh?ng retry, kh?ng g?i generation m?i. Timeout message hi?n th? s? gi?y c?u h?nh v? h??ng d?n3 c?u/TXT ng?n. Release build0 warning/error; Security73 v? HTTP105 ??t (fixtures). API ???c restart, readiness HTTP200. T?ng th?i gian ch? kh?ng kh?c ph?c Google503 v? ch?a ch?ng minh Gemini sinh th?nh c?ng.


Checkpoint ch?n ?o?n29/10/2026: ?? x?c ??nh th?ng ?i?p upstream th?c t? b?ng1 request t?ng h?p t?i gi?n t?i c?ng model/key (kh?ng t?i li?u/schema, kh?ng retry): HTTP503 UNAVAILABLE sau1,84s; Google b?o model ?ang high demand. Kh?ng ph?i k?t lu?n ch? t? label c?a app. Kh?ng ??i model/key/billing/code/database trong l?n ch?n ?o?n; xem docs/GEMINI_DIAGNOSIS_2026-10-29.md v? artifact ?? lo?i secret. Ch?a sinh c?u h?i th?nh c?ng.
