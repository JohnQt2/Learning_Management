# LMS-AI — Checklist nâng cấp ngày29/10/2026

Phạm vi: LMS dạy–học–kiểm tra, giữ N-Tier và dữ liệu cũ; không mở rộng sang quản lý đào tạo, không commit/push/merge. `[x]` có bằng chứng local; `[!]` chưa xác minh hoặc thiếu môi trường. Chi tiết tại PROJECT_AUDIT.md và docs/VERIFICATION.md.

## Phase1 — Audit/nền tảng

- [x] Đọc master prompt, modules/config/entities/services/controllers/tests/docs; xuất bảng Đã có/Thiếu/Lỗi/Cần nâng cấp trước sửa.
- [x] Baseline Release build0 warning/error; Smoke96/Security73/HTTP105/Hosting53; FE13/build đạt.
- [x] Kiểm tra SQLite13 bảng nghiệp vụ/38 hàng, integrity/FKs đúng,2 migrations ban đầu; backup DB+Uploads trước sửa tại artifacts/backups/upgrade_20261029_015812.
- [!] SQL Server runtime: Docker daemon pipe không tồn tại.
- [!] Browser UI: lần cuối kết nối báo No browser is available, discovery[].
- [!] Git history/remote CI: không có.git.

## Phase2 — Hồ sơ/khóa học/học viên

- [x] Email/CreatedAt nullable cho legacy; migrations riêng SQLite/SQL Server; bản sao nâng cấp giữ dữ liệu cũ.
- [x] Xem/sửa fullName/email và khóa học liên quan; không tự sửa role/lock.
- [x] Admin tìm kiếm/lọc vai trò/trạng thái; tạo Lecturer có email; phân quyền owner/member.
- [x] Roster có email, ngày tham gia, trạng thái tài khoản, số Quiz/tiến độ/điểm và trạng thái học tập thật.
- [x] Gate: Profile66 HTTP, HTTP105 hồi quy, FE14/build đạt.

## Phase3 — Học liệu5 định dạng

- [x] TXT/MD UTF8; PDF text layer; DOCX đoạn/tiêu đề/bảng; PPTX theo thứ tự slide/shape/bảng.
- [x] Kiểm phần mở rộng/MIME/format; cấu hình giới hạn upload/text/archive/PDF decoded bytes.
- [x] Metadata, preview có đánh dấu cắt ngắn, trích xuất lại legacy và giữ lịch sử chưa biết.
- [x] Download có quyền; soft-delete giữ file/Question/Quiz/history và chặn truy cập mới.
- [x] UI5 định dạng; scan PDF/slide chỉ ảnh báo cần OCR; chưa triển khai OCR.
- [x] Gate: Material128/Security73/HTTP105/Smoke96/Profile66, FE16/build; copy4 migrations giữ38 hàng.

## Phase4 — Gemini

- [x] Plan không gọi provider; chia đoạn có thứ tự, bao phủ toàn bộ UTF-16, không cắt surrogate; phân bổ số câu theo độ dài.
- [x] Tài liệu quá giới hạn/đoạn toàn khoảng trắng bị từ chối trước khi gửi; không âm thầm bỏ nội dung.
- [x] Budget dùng chung; retry chỉ429/503, có Retry-After/backoff/jitter, giới hạn/cancellation/deadline; không tự đổi model/billing.
- [x] Văn bản5 định dạng → validate JSON → lưu Draft atomic sau mọi đoạn; difficulty/material/topic/chunk ranges/log metrics; không trùng do retry.
- [x] Xóa nguồn trong lúc chờ provider trả409; không lưu Draft dở khi provider/JSON/budget lỗi.
- [x] Gate: Adapter55/Workflow101, các hồi quy, FE17/build; copy5 migrations giữ38 hàng.
- [x] Gemini live mới: HTTP200, cấu hình hiệu lực3.5-flash-lite,1 TXT mẫu419 ký tự →3 Draft/4,11 giây/1 request; đọc DB/log đối chiếu. Evidence: artifacts/verification/gemini-live-20261029T054651Z.
- [!] Live mọi định dạng/tài liệu dài/retry/quota dài hạn chưa xác minh; kết quả3.8-flash503 cũ vẫn là lịch sử riêng.

## Phase5 — Tiến độ/kết quả/Quiz

- [x] Dashboard Student: khóa học, Quiz, điểm trung bình, lịch sử, chủ đề yếu và gợi ý từ Submitted/ghi danh hiện tại; chưa có điểm trả null.
- [x] Thống kê Course cho Lecturer owner: sinh viên, Quiz, số người nộp, điểm, chủ đề yếu; không lộ answer keys trong statistics.
- [x] Gợi ý RuleBased có lý do/accuracy, loại học liệu đã xóa; lịch sử bài nộp vẫn giữ sau khi rời Course.
- [x] Lọc Question theo Topic/status/source/difficulty; độ khó thủ công cho phép null, rỗng để xóa, bỏ field để giữ; Approved không sửa.
- [x] Quiz.Revision chống stale publish/edit; hai kiểm thử haiDbContexts xác nhận rollback, không ghi đè bộ câu hỏi/title mới.
- [x] Gate: Progress82/Workflow120/Smoke96/HTTP105, FE20/build0.

## Phase6 — UI/QA/tài liệu

- [x] UI theo vai trò, validation/loading/error/empty, responsive CSS; UTC timestamp/session; download giữ tên gốc; CORS expose headers.
- [x] Final Release build13 projects0 warning/error;9 suite .NET:96/73/105/54/66/128/55/120/82;22 Python;23 FE/build39 modules. Logs: artifacts/verification/final-20261029-125102.
- [x] Hai provider không pending model changes; SQL Server idempotent script có6 migrations; publish local thành công.
- [x] NuGet/npm audit không báo advisory hiện biết; Compose config đạt. Không coi advisory audit là chứng nhận bảo mật/runtime.
- [x] Legacy fixture và bản sao DB đang dùng nâng cấp6 migrations giữ nguyên dữ liệu.
- [x] Backup mới trước áp dụng local: artifacts/backups/upgrade_before_apply_20261029T055404Z.
- [x] DB local đã áp dụng6 migrations:13 bảng/38 hàng cũ giữ nguyên,1 file Uploads hash không đổi, integrity ok/FK0; local-upgrade-result.json.
- [x] HTTP thật: health/readiness/Swagger/FE HTML/module200, CORS đúng. Đã dừng dịch vụ sau kiểm tra; lệnh chạy tại README.
- [x] README/deployment/API/DB/env/security/audit/changed-files/evidence và checklist68 ca UI cập nhật UTF8.
- [!] Browser visual/interaction/mobile68 ca còn UNVERIFIED; không có trình duyệt kết nối.
- [!] SQL Server runtime/migrations/backup/restore chưa chạy; Docker daemon vắng.
- [!] Remote CI/production deployment chưa thực hiện.

Checkpoint cuối:6 phase triển khai và qua các gate local nêu trên. Browser UI và SQL Server runtime còn thiếu môi trường; không gọi toàn bộ production acceptance là PASS. Không có tiến trình API/FE kiểm thử còn chạy; mở hai terminal theo README để dùng.
