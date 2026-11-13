# LMS-AI — Kết quả nâng cấp ngày29/10/2026

Đã triển khai6 phase theo master prompt trong workspace hiện tại, giữ Layered N-Tier Modular Monolith và dữ liệu cũ. Không có .git; không commit/push/merge. Đây là kết quả kiểm tra cục bộ có giới hạn, không phải xác nhận toàn bộ môi trường production.

## Kết quả theo phase

| Phase | Chức năng hoàn thiện | Bằng chứng |
|---|---|---|
|1 Audit/nền tảng|Đọc module/config/tests, kiểm tra schema và backup trước thay đổi|Baseline build và bộ test; audit ở docs/UPGRADE_AUDIT_2026-10-29.md|
|2 Hồ sơ/khóa học/học viên|Email/ngày tạo nullable cho dữ liệu cũ; xem/sửa hồ sơ; khóa học liên quan; Admin search/role/lock filters; roster có email/trạng thái/tiến độ Quiz|Profile66 HTTP assertions, hồi quy HTTP105|
|3 Học liệu|TXT/MD UTF8, PDF text layer, DOCX paragraph/table, PPTX theo thứ tự slide; MIME/format/storage/extraction limits; metadata/preview/re-extract/download; soft-delete bảo toàn lịch sử|Material128 assertions với parser thật, file giả/lớn/bom nén/auth/download|
|4 Gemini|Kế hoạch chia đoạn bao phủ toàn bộ văn bản; số câu phân bổ theo độ dài; budget dùng chung; retry429/503 có Retry-After/backoff/jitter; validate rồi lưu Draft atomic với provenance|Adapter55, Workflow101 ở gate; sau Phase5/6 Workflow120; một phép Gemini live thành công|
|5 Tiến độ/Quiz|Student/Lecturer dashboards; thống kê course/quiz/student/topic; gợi ý RuleBased có lý do; Question Topic/source/status/difficulty filters; Quiz.Revision chống stale publish/edit|Progress82, Workflow120 gồm hai race xác định; Smoke96/HTTP105 hồi quy|
|6 UI/QA/tài liệu|Navigation/empty/loading/error/responsive CSS; UTC timestamp; tên download từ server; CORS expose headers; script kiểm tra và tài liệu cập nhật|FE23 tests/build39 modules; Hosting54; final Release build0 warning/error|

## Database đang dùng

Đã áp dụng6 SQLite migrations sau khi kiểm thử trên bản sao và tạo backup mới. So sánh toàn bộ cột đã có trước nâng cấp:13 bảng nghiệp vụ/38 hàng giữ nguyên chính xác;1 file Uploads không đổi; integrity=ok, không vi phạm khóa ngoại. Dữ liệu lịch sử chưa biết Email/CreatedAt/upload time/uploader/provenance giữ null hoặc Legacy phù hợp, không tạo thời gian giả.

Backup cuối trước áp dụng: `artifacts/backups/upgrade_before_apply_20261029T055404Z/lms-ai.before.sqlite`, cùng Uploads và manifest. Kết quả: `artifacts/verification/local-upgrade-result.json`. Backup chứa dữ liệu tài khoản thật, chỉ giữ riêng; không đưa vào gói source gửi người khác.

SQLite và SQL Server đều có migrations/snapshot riêng, không pending model changes. Đã tạo lại SQL Server idempotent script `docs/sqlserver-migrations.sql`. SQL Server chưa chạy migration thực tế vì Docker daemon không hoạt động.

## Kiểm thử thực tế

Lệnh `./scripts/verify_all.ps1 -DatabasePath BE/Services/Core/LMS.Core.API/lms-ai.sqlite` chạy thành công: Release solution13 projects,9 suite .NET,22 Python và23 FE tests/build. Log: `artifacts/verification/final-20261029-125102`. Chi tiết từng suite/lệnh ở [VERIFICATION](docs/VERIFICATION.md); không dùng các assertion này làm bằng chứng browser UI.

Gemini live: Backend dùng cấu hình hiệu lực `gemini-3.5-flash-lite`, HTTP200,3 Draft hợp lệ,4,11 giây,1 provider request. Upload TXT mẫu419 ký tự → plan → provider → validation → SQLite thật, database cô lập. Đã đọc lại3 Draft và log Success; không sửa key/model hay gửi tài liệu người dùng. Phép thử không xác minh live mọi định dạng, tài liệu dài, retry thực tế hoặc chất lượng trên toàn bộ học liệu. Bằng chứng: `artifacts/verification/gemini-live-20261029T054651Z/result.json`.

API local sau migration: health/readiness/Swagger200; frontend HTML/module200; CORS đúng origin và expose Content-Disposition/Retry-After. Các tiến trình kiểm thử đã dừng; `artifacts/verification/final-network-smoke.json`. Lệnh mở nền bị automatic review từ chối (`blocked by policy`, không có lý do chi tiết); kiểm tra HTTP được hoàn tất bằng phiên có quản lý rồi dừng.

NuGet13 projects và npm audit không báo advisory hiện biết; Compose config hợp lệ. API publish Release thành công. Đây là kiểm tra advisory/cấu hình/artifact cục bộ, không phải chứng nhận bảo mật hay deploy.

## Giới hạn còn lại

- Browser: kết nối báo `No browser is available`, discovery `[]`;68 ca tương tác/visual/mobile trong docs/KIEM_THU_COBAN.md còn UNVERIFIED.
- SQL Server runtime/backup/restore: Docker daemon pipe không tồn tại. Model/snapshot/script đã kiểm tra; chưa dùng thay bằng runtime PASS.
- OCR chưa triển khai. PDF scan/slide chỉ ảnh có trạng thái cần OCR. PDF dùng codec nội dung chưa hỗ trợ có thể bị từ chối; xuất lại PDF hoặc TXT.
- Gemini có thể tiếp tục503/429/timeout; không tự đổi model, bật billing, approve/publish hoặc retry vô hạn. Kết quả live chỉ có phạm vi một fixture nêu trên.
- Chưa deploy VPS/domain, chưa kiểm remote CI/Git history. Course không có thiết kế status nên không tự thêm trạng thái đào tạo.

## Chạy trên Windows

Từ root, terminal1: `dotnet run --project BE/Services/Core/LMS.Core.API -c Release --launch-profile LMS.Core.API`. Terminal2: `cd FE`, `npm run dev`. API: http://localhost:5088; frontend: http://127.0.0.1:5173. Cấu hình backend ở .env riêng, frontend ở FE/.env.local; xem README và docs/ENV_SETUP.md. Không reset database.

Bản audit trước nâng cấp được lưu tại docs/archive/PROJECT_AUDIT_before_upgrade_2026-10-29.md; đó là lịch sử, không dùng các trạng thái cũ thay cho báo cáo này.
