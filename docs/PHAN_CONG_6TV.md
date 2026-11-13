# Phân công bảo trì và nghiệm thu cho 6 thành viên

Source hiện đã có các module LMS cơ bản và kiểm thử tự động, giữ kiến trúc Layered N-Tier Modular Monolith (.NET 10/EF Core, React 19/Vite). Bảng dưới là **đề xuất trách nhiệm bảo trì tiếp theo**, không phải lịch sử đóng góp và không gán các thay đổi đã có cho sinh viên chưa thực hiện chúng.

Mỗi thành viên đọc module, chạy kiểm thử và làm thay đổi có giá trị thực nếu cần. Không chia source có sẵn thành commit giả để thể hiện tiến độ. Trạng thái xác minh chung xem [`PROJECT_AUDIT.md`](../PROJECT_AUDIT.md), [`PROJECT_TASKS.md`](../PROJECT_TASKS.md); phần UI còn phải nghiệm thu xem [`KIEM_THU_COBAN.md`](KIEM_THU_COBAN.md).

| Thành viên | Phạm vi mã nguồn và chức năng đã có | Trách nhiệm tiếp theo |
|---|---|---|
| **TV1 — Backend Core / Authentication** | `BE/Services/Core/LMS.Core.API/Program.cs`, Middleware, `AuthController`, `AdminController`, `AuthenticationModule`, `AdminModule`: JWT, hash mật khẩu, hồ sơ, đổi mật khẩu/đăng xuất thu hồi token, lock/role validation, Admin tạo Lecturer, JSON lỗi, rate limit, CORS và cấu hình proxy. | Chạy nghiệm thu tài khoản ba vai trò, rà soát quyền và cấu hình HTTPS/proxy trong môi trường triển khai; phối hợp TV6 duy trì regression tests và startup checks. Không sửa role/JWT contract mà thiếu review tích hợp. |
| **TV2 — Database / Course / Enrollment** | `Core.Domain/Entities`, `Core.Infrastructure/Persistence`, provider-specific migrations, `CourseModule`, `CoursesController`, các script nâng cấp legacy SQLite: Course CRUD cơ bản, JoinCode, ghi danh bằng mã, số lượng/danh sách học viên, Topic edit, unique/index/quan hệ và bảo vệ nâng cấp dữ liệu. | Xác minh migrations trên SQL Server thật, diễn tập backup/restore và nâng cấp bản sao dữ liệu phù hợp; duy trì toàn vẹn Enrollment và dữ liệu sau restart. Không reset/xóa DB để xử lý lỗi schema. |
| **TV3 — Learning Content / Question / Quiz Authoring** | `MaterialModule`, `QuestionModule`, phần authoring của `QuizModule`, Controllers tương ứng và `LocalFileStorage`: upload/xem/tải TXT/PDF theo quyền, validation tên/nội dung/kích thước, manual Draft edit/approve, Quiz mô tả/chọn câu hỏi/lưu Draft/publish, bảo vệ câu hỏi đã được dùng. | Nghiệm thu file thật và tác vụ soạn/duyệt/publish; duy trì kiểm tra cấu trúc và xung đột cạnh tranh. Trích xuất PDF/OCR là mở rộng ngoài MVP, chỉ thêm khi được chốt phạm vi. Phối hợp TV2 trước mọi thay đổi schema/storage. |
| **TV4 — Gemini / Recommendations** | `AIModule`, `GeminiAiClient`, `QuestionJsonValidator`, phần gợi ý trong `ResultModule`: Gemini REST, cấu hình/status, structured JSON, timeout/cancellation/quota, giới hạn TXT, validate/lưu subset hợp lệ dưới Draft; gợi ý theo tỷ lệ Topic dưới 60% và tài liệu đúng Course. | Chạy **TEST 15 với key thật** do đơn vị cung cấp, validate và chứng minh Draft lưu được rồi duyệt/đưa Quiz; rà soát quota/lỗi provider và nội dung không tin cậy. Phối hợp TV6 kiểm tra tổng hợp Topic. Không gọi rule-based recommendation là machine learning, không dùng fixture/Mock để công nhận Gemini live. |
| **TV5 — Frontend** | `FE/src/main.jsx`, `api.js`, `shared.jsx`, `utils.js`, `pages/`, CSS và `FE/tests`: UI ba vai trò, profile/password, Admin phân trang/lock, Course/Topic/Material, question bank, Gemini form, Quiz authoring/take, kết quả/lịch sử/gợi ý, loading/error/empty/validation và session expiry. | Chạy checklist trình duyệt ba vai trò, responsive, bàn phím, upload/download và refresh trên máy có browser; sửa lỗi theo bằng chứng. Đồng bộ API với TV1/TV3/TV4/TV6, giữ key phía Backend. `npm test`/build đã có bằng chứng nhưng chưa thay thế UI acceptance. |
| **TV6 — Quiz Submission / Results / QA / CI** | Phần submit/scoring của `QuizModule`, kết quả trong `ResultModule`, `BE/Tests/{LMS.SmokeTests,LMS.SecurityTests,LMS.IntegrationTests,LMS.HostingTests}`, `.github/workflows/ci.yml`, kiểm tra migrations/legacy: chấm Backend, lưu answers/Attempt, một bài nộp/Quiz, result authorization, concurrency và HTTP tests. | Phối hợp nghiệm thu UI/API/DB đầy đủ, chạy SQL Server và deployment checks còn thiếu; kích hoạt/xác minh CI remote sau khi source được nhóm review đưa lên Git. Giữ phân biệt automated local, provider fixture, live Gemini và browser evidence. |

## Ranh giới phối hợp

- TV2 điều phối Entity/DbContext/migrations; TV1 điều phối error/auth contract; mọi module dùng chung phải được thông báo trước khi thay đổi tương thích.
- TV3 sở hữu luồng tạo/chọn/publish Quiz; TV6 sở hữu submit/chấm/lịch sử. Hai phần cùng `QuizService` cần chia hạng mục và review để tránh sửa đè.
- TV4 đề xuất logic gợi ý; TV6 bảo đảm dữ liệu điểm và quyền truy cập. TV5 triển khai giao diện theo DTO thực tế, không suy đoán hoặc tự tạo dữ liệu thay thế.
- Mỗi PR ghi lỗi cụ thể, thay đổi, lệnh kiểm thử thật, kết quả và giới hạn chưa xác minh; không chuyển task sang hoàn thành chỉ vì đã viết mã.

## Các cổng nghiệm thu còn cần người thực hiện

| Công việc | Điều kiện và người phối hợp | Trạng thái bàn giao |
|---|---|---|
| UI ba vai trò, responsive và accessibility cơ bản | TV5 + chủ module; trình duyệt có kết nối và bộ dữ liệu kiểm thử riêng. | [!] UNVERIFIED — chưa có browser khả dụng trong phiên hoàn thiện. |
| Gemini live, TEST 15 từ TXT → Draft → duyệt → Quiz | TV4 + TV3 + TV5; API key thật cấu hình tại Backend, tài liệu được phép gửi. | [!] UNVERIFIED — chưa có key thật cho kiểm chứng; fixture không thay thế. |
| SQL Server runtime/migrations/backup-restore | TV2 + TV6; server thử nghiệm và bản sao dữ liệu được phép. | [!] UNVERIFIED — cần chạy trên SQL Server thực, phân biệt với script/migration SQLite đã kiểm tra. |
| HTTPS/reverse proxy và triển khai production | TV1 + TV2 + TV6; cấu hình, backup và quyền triển khai cụ thể. | [ ] Chưa triển khai; không tự đưa lên VPS/domain. |
| GitHub CI/PR/merge | Cả nhóm; repository/nhánh thực tế được đối chiếu trước khi tích hợp. | [!] Chưa xác minh remote; workspace bàn giao không có metadata `.git`, không suy ra đã đồng bộ `develop`. |

Các mở rộng như thanh toán/marketplace, giám sát thi/timer phức tạp, OCR PDF, reset mật khẩu qua email và mô hình ML nằm ngoài MVP hiện tại; phải chốt yêu cầu trước khi triển khai.

## Quy trình Git khi nhóm tiếp tục

1. Kiểm tra repository, nhánh, commit và thay đổi thực tế. Source bàn giao và `develop` có thể khác; không ghi đè hoặc giả định đã đồng bộ.
2. Tạo nhánh tích hợp có thể review nếu cần đưa source vào repository; bảo toàn công việc thành viên và dữ liệu runtime. Không commit `.sqlite`, `Uploads`, secret, `bin`, `obj`, `node_modules`, `dist`.
3. Mỗi thành viên tạo Issue/branch theo thay đổi mình thực sự làm, tự kiểm tra và mở PR; reviewer khác tác giả kiểm tra hợp đồng/API và bằng chứng.
4. Chỉ push/merge/deploy khi người có thẩm quyền của nhóm giao thực hiện. Không force push, tạo lịch sử commit giả hoặc tự merge để hợp thức hóa phân công.

Tài liệu này không tạo branch, commit, PR hoặc thay đổi nào trên GitHub.
