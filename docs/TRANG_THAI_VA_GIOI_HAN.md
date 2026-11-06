# Trạng thái và giới hạn — 29/10/2026

Bản mã đã triển khai sáu phase nâng cấp LMS, giữ kiến trúc N-Tier và 13 bảng nghiệp vụ. Checklist/bằng chứng cuối ở [PROJECT_TASKS](../PROJECT_TASKS.md), [PROJECT_AUDIT](../PROJECT_AUDIT.md) và [VERIFICATION](VERIFICATION.md). Không coi build/tests local là nghiệm thu production.

## Đã triển khai và kiểm tra local

- Hồ sơ/email/ngày tạo, sửa trường được phép, khóa/mở khóa và thu hồi JWT; Admin tìm/lọc/tạo Lecturer; Course/JoinCode/Topic/roster với phân quyền Backend.
- Upload/download/preview TXT, Markdown, PDF text-layer, DOCX, PPTX; metadata/extraction status; giới hạn dung lượng/giải nén/PDF/text; nhận file giả và PDF/slide không có chữ.
- Xóa mềm học liệu, giữ tệp và reference lịch sử; không dùng tài liệu đã xóa cho truy cập mới/gợi ý/newAI.
- Kế hoạch chia nguồn có thứ tự/range, không truncate âm thầm; retry 429/503 có Retry-After/backoff/jitter và budget tổng; validate/lưu Draft atomic, không tự approve/publish.
- Manual/AI question bank, độ khó, Draft/Approved, Quiz Draft/Publish, chấm điểm Backend và chống nộp trùng/concurrency.
- Dashboard Student, course statistics Lecturer, lịch sử và gợi ý RuleBased/reason từ dữ liệu thật; empty/null metrics khi chưa có bài nộp.
- Migrations riêng SQLite/SQL Server; copy upgrade đối chiếu dữ liệu cũ và persistence sau restart.

Final gate local đã chạy: Release build 0 warning/error; 9 bộ .NET (Smoke96, Security73, HTTP105, Hosting54, Profile66, Material128, Adapter55, Workflow120, Progress82), Python22 và Frontend23 tests/build39 modules. Log gate: `artifacts/verification/final-20261029-125102`. Số này mô tả lần chạy cụ thể, không có nghĩa bao phủ mọi tình huống.

## Gemini thật: có bằng chứng thành công, phạm vi hữu hạn

Phép kiểm tra cô lập ngày 29/10 dùng `gemini-3.5-flash-lite` từ cấu hình Backend hiệu lực: HTTP200, 3 Draft hợp lệ lưu SQLite, 1 lượt gửi, 4,11 giây, bài học tổng hợp 419 ký tự. Bằng chứng `artifacts/verification/gemini-live-20261029T054651Z/result.json`.

Lần này chưa chứng minh mọi file dài/5 định dạng/retry/Approve/Quiz đều chạy với Google thật. Các nhánh đó có kiểm thử offline riêng dùng parser/SQLite/HTTP thật và provider fixture. Google có thể tiếp tục trả quota, timeout hoặc 503 tùy model/tài khoản/thời điểm. Lần lỗi trước với model 3.8 không được coi đã hết lỗi vì model 3.5 hiện sinh thành công.

## Môi trường chưa được nghiệm thu

| Mục | Trạng thái thực tế |
|---|---|
| UI browser/visual/responsive | UNVERIFIED: browser runtime từng lỗi khởi tạo; lần thử lại báo không có browser, discovery rỗng. FE tests/build không thay thế thao tác màn hình thật |
| SQL Server runtime | UNVERIFIED: Docker CLI có nhưng daemon pipe không hoạt động; đã có provider migrations/script, chưa chạy DB server thật |
| Remote Git/CI | UNVERIFIED: workspace không có .git; không tự commit/push/merge hoặc suy diễn CI remote |
| Production/VPS/domain | Chưa triển khai/nghiệm thu; hướng dẫn publish và cấu hình không phải bằng chứng deploy |

SQLite local đã được backup và nâng từ 2 lên 6 migrations: 13 bảng/38 hàng cũ giữ nguyên, 1 tệp Uploads giữ hash, integrity đúng/FK không lỗi. Proof `artifacts/verification/local-upgrade-result.json`; backup `artifacts/backups/upgrade_before_apply_20261029T055404Z`. API local health/readiness/Swagger, FE HTML/module và CORS đã kiểm tra qua HTTP; các process kiểm tra đã dừng. [VERIFICATION](VERIFICATION.md) ghi chi tiết. Không dựa vào trạng thái local để bỏ qua backup/kiểm tra bản sao khi nâng cấp một DB khác.

## Giới hạn chức năng còn lại

- Không OCR. PDF scan/ảnh hoặc slide chỉ có ảnh không sinh được text tự động; PDF mã hóa/hỏng hoặc codec nội dung không được decoder an toàn hỗ trợ có thể bị từ chối. Không bảo đảm bố cục/phương trình/cột phức tạp giữ hoàn hảo sau trích xuất.
- Lọc question bank hiện thực hiện trên danh sách đã tải ở FE; đa số danh sách chưa phân trang server, ngoại trừ Admin users. Chưa có load test quy mô lớn.
- Tiến độ là Quiz Published đã nộp, không đo thời gian học/đọc học liệu. Một Student có một Attempt/Quiz, chưa có thi lại/timer/proctoring.
- Gợi ý ôn tập là quy tắc tỷ lệ đúng dưới 60%, không dùng Gemini/ML và không được gắn nhãn AI.
- Chưa có idempotency key xuyên các POST sinh AI độc lập. Retry transport trong một request không lưu trùng; người dùng chủ động sinh lại có thể tạo thêm câu.
- Nguồn AI truy theo Material/chunk/range và tệp gốc được giữ; chưa lưu snapshot toàn bộ văn bản riêng cho từng generation.
- Chưa có gửi email/xác thực email/khôi phục mật khẩu, refresh token, thanh toán, đa AI provider, tự chuyển dữ liệu giữa SQLite/SQL Server hoặc storage phân tán.
- Xóa học liệu giữ tệp để bảo vệ lịch sử; chưa có chức năng purge vật lý/quản lý dung lượng sau xóa.

Tài liệu các lần trước được giữ trong `docs/archive` hoặc biên bản có ngày cũ để truy cứu. Cấu hình local của người khác cần secret/model/quota riêng; không chia sẻ `.env`, DB, Uploads người dùng hoặc artifacts trong source ZIP.
