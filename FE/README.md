# Frontend LMS-AI

Cập nhật ngày 29/10/2026. React 19 và Vite, sử dụng API ASP.NET Core thật. Frontend không chèn tài khoản, mật khẩu hoặc số liệu học tập mẫu.

## Chạy trên Windows

Cần Node.js tương thích Vite 7 và Backend đang chạy. Từ thư mục `FE`:

```powershell
npm ci
npm run dev
```

Mở `http://127.0.0.1:5173` hoặc `http://localhost:5173`. API mặc định là `http://localhost:5088`. Vite dùng `--strictPort`: nếu cổng 5173 đang bận, dừng đúng tiến trình chiếm cổng hoặc đổi cổng cùng cấu hình CORS của Backend.

Đổi Backend bằng `.env.local`:

```powershell
Copy-Item .env.example .env.local
```

```dotenv
VITE_API_BASE=http://localhost:5088
```

URL trỏ tới gốc Backend, không thêm `/api`. Khởi động lại Vite sau khi sửa. Biến `VITE_*` được đưa vào bundle khi build; chỉ đặt cấu hình công khai ở đây. Gemini API key, JWT signing key và mật khẩu thuộc cấu hình Backend. Backend phải cho phép đúng origin qua `Cors__AllowedOrigins__0` và các chỉ số tiếp theo khi cần. Header `Content-Disposition` và `Retry-After` cần được Backend expose qua CORS để trình duyệt đọc được.

## Chức năng theo vai trò

- **Sinh viên:** đăng ký/đăng nhập → Tổng quan với khóa học đã tham gia, tiến độ Quiz, điểm trung bình, bài làm gần đây, chủ đề cần ôn và học liệu gợi ý. Mở Khóa học để tham gia bằng JoinCode, xem/tải học liệu và làm Quiz Published. Chọn đủ đáp án trước khi nộp; điểm được Backend tính. Lịch sử và kết quả đã nộp được lưu trong database.
- **Giảng viên:** Tổng quan liệt kê khóa học mình quản lý và lượt tham gia. Tạo/sửa khóa học, chia sẻ JoinCode, quản lý chủ đề/học liệu, sinh câu hỏi Gemini hoặc soạn thủ công, sửa Draft, duyệt Approved, tạo Quiz Draft rồi phát hành. Tab Sinh viên & kết quả hiển thị email, ngày tham gia, trạng thái tài khoản, tiến độ Quiz và bài nộp. Tab Thống kê hiển thị số sinh viên, Quiz đã công bố, số bài nộp, điểm trung bình, kết quả từng Quiz/từng sinh viên và chủ đề có tỷ lệ đúng dưới 60%.
- **Admin:** tạo Lecturer, tìm tài khoản theo tên/username/email, lọc vai trò/trạng thái trước khi phân trang, xem chi tiết và khóa/mở khóa. Giao diện không cho tự tạo Admin.
- **Tất cả vai trò:** xem hồ sơ với email, ngày tạo, trạng thái và khóa học liên quan; chỉ cập nhật họ tên/email, không tự đổi vai trò hoặc trạng thái khóa. Dữ liệu cũ chưa có ngày tạo hiển thị chưa có dữ liệu. Có đổi mật khẩu và đăng xuất theo chính sách thu hồi JWT của Backend.

Tiến độ chỉ phản ánh Quiz đã nộp so với Quiz đã công bố, chưa đo việc đọc tài liệu. Điểm trung bình chưa có bài nộp hiển thị rõ trạng thái trống; điểm thật bằng 0 vẫn hiển thị 0. Tổng lượt tham gia trên dashboard giảng viên có thể tính cùng một sinh viên ở nhiều khóa học.

Frontend lưu phiên trong `sessionStorage`, kiểm tra `/api/auth/me`, kiểm tra hạn token và về đăng nhập khi nhận HTTP 401. Backend dùng JWT key Development tạm thời sẽ yêu cầu đăng nhập lại sau khi khởi động lại. Điều hướng dùng đường dẫn gốc; biểu mẫu chưa lưu và lựa chọn Quiz chưa nộp không được giữ khi rời màn hình/refresh.

## Học liệu và Gemini

Hỗ trợ `.txt`, `.md`, `.pdf`, `.docx`, `.pptx`. Giới hạn upload lấy từ `/api/materials/upload-policy`, mặc định 5.000.000 byte. Danh sách hiển thị định dạng, dung lượng, chủ đề, thời điểm/người upload và trạng thái trích xuất. Dữ liệu lịch sử chưa có metadata được ghi rõ. Có lọc chủ đề, xem văn bản trích xuất, xem PDF gốc và tải file; tên tải về ưu tiên filename gốc do Backend trả trong `Content-Disposition`.

TXT/Markdown dùng UTF-8. PDF có text layer được trích xuất; PDF scan cần OCR. DOCX trích xuất đoạn/bảng; PPTX giữ thứ tự slide. Tệp không có text vẫn tải được nhưng không dùng để sinh câu hỏi. OCR chưa triển khai. Preview có thể giới hạn độ dài và sẽ thông báo khi chỉ hiển thị một phần; không coi preview là toàn bộ nội dung gửi AI.

Giảng viên có thể yêu cầu trích xuất lại học liệu cũ và xóa học liệu sau xác nhận. Xóa ẩn tài liệu khỏi danh sách, giữ câu hỏi/Quiz/kết quả liên quan theo quy tắc Backend.

Khi Backend cấu hình Gemini, chọn học liệu có text, số câu 3–10 và độ khó. Giao diện gọi endpoint lập kế hoạch trước: hiển thị toàn bộ số ký tự, các đoạn theo thứ tự, phạm vi ký tự, phân bổ câu hỏi, ngân sách request, số lần retry và thời hạn xử lý. Lập kế hoạch chưa gửi nội dung tới Gemini. Chỉ bấm Sinh câu hỏi mới gửi nội dung tới Google.

Backend chia tài liệu trong giới hạn cấu hình; tài liệu quá giới hạn bị từ chối trước khi gọi provider. Retry chỉ cho lỗi tạm thời 429/503, có giới hạn và nằm trong ngân sách tổng, tôn trọng `Retry-After`. Frontend không tự retry generation. Hiển thị lỗi HTTP, quota, JSON/validation, timeout hoặc xung đột 409 do Backend trả về; không báo thành công khi chưa lưu Draft.

Câu hỏi AI luôn là Draft. Ngân hàng hiển thị nguồn học liệu, đoạn/phạm vi ký tự, độ khó và lọc theo chủ đề/trạng thái/nguồn/độ khó. Câu hỏi cũ chưa có độ khó giữ nguyên Chưa xác định. Giảng viên phải kiểm tra/sửa/duyệt trước khi chọn vào Quiz.

Khi AI Disabled, thiếu key hoặc chỉ dùng Mock, giao diện không trình bày kết quả thành Gemini thật; luồng thủ công vẫn hoạt động. Gợi ý ôn tập ghi rõ **theo quy tắc nghiệp vụ**, có lý do từ kết quả trả lời dưới 60%; không gọi đây là gợi ý Gemini.

## Kiểm thử đã chạy và giới hạn

```powershell
npm test
npm run build
```

Ngày 29/10/2026: `npm test` **23/23 đạt**, gồm chạy với `TZ=Asia/Bangkok`; `npm run build` **đạt, 39 modules**. Các test Node kiểm tra HTTP client, bearer token, validation, multipart, cancellation/401, phiên đăng nhập, payload Quiz, lỗi/Retry-After, năm định dạng upload, bộ lọc câu hỏi, điểm 0 so với chưa có bài nộp, timestamp UTC và tên tải về gốc. Đây là kiểm thử client tự động và build.

**Browser UI, visual QA, responsive thực tế và E2E UI: UNVERIFIED.** Browser runtime không có browser khả dụng sau khi kiểm tra kết nối; CSS responsive đã được rà mã nguồn, chưa thay thế nghiệm thu trên desktop/mobile. Checklist thủ công: [`../docs/KIEM_THU_COBAN.md`](../docs/KIEM_THU_COBAN.md).

Gemini thật đã có một kiểm tra Backend với TXT tổng hợp: HTTP 200, model hiệu lực `gemini-3.5-flash-lite`, lưu 3 Draft, khoảng 4,11 giây và một lượt gửi provider. Bằng chứng: [`result.json`](../artifacts/verification/gemini-live-20261029T054651Z/result.json). Kết quả này không chứng minh luồng trình duyệt, tài liệu thực của đơn vị hoặc live cả năm định dạng đã nghiệm thu. Mock/offline và live được ghi riêng trong [`../PROJECT_AUDIT.md`](../PROJECT_AUDIT.md). SQL Server runtime cũng cần bằng chứng riêng; Docker daemon hiện chưa kết nối được.

## Build để triển khai

Build với API HTTPS của môi trường đích:

```powershell
$env:VITE_API_BASE = 'https://api.example.org'
npm run build
```

Đưa `dist/` lên static hosting theo hướng dẫn triển khai. `npm run preview` chỉ kiểm tra bundle tại `http://127.0.0.1:4173`, chỉ bind loopback. Muốn gọi API qua preview, thêm đúng origin 4173 vào CORS Backend rồi khởi động lại API. Không có thao tác deploy tự động.

Timestamp ISO từ Backend không có hậu tố múi giờ được hiểu là UTC, sau đó hiển thị theo giờ máy người dùng; timestamp có offset được giữ đúng thời điểm. Ngày không có/không hợp lệ hiển thị `—`.

## Cấu trúc

- `src/main.jsx`: phiên và điều hướng theo vai trò.
- `src/api.js`: HTTP, lỗi, upload, preview và tải học liệu có JWT/tên gốc.
- `src/shared.jsx`: component, loading/error state và thao tác chống gửi lặp.
- `src/utils.js`: validation, bộ lọc, thời gian UTC và payload nộp Quiz.
- `src/pages/`: Account, Admin, Dashboard, Courses, Materials, Questions, Quizzes, Results.
- `tests/client.test.js`: test Node; không gọi Gemini thật hoặc database đang dùng.

Hướng dẫn Backend, tài khoản ban đầu, migrations, backup và cấu hình production: [`../README.md`](../README.md), [`../DEPLOYMENT.md`](../DEPLOYMENT.md).
