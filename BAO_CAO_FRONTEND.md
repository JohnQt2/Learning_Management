# Báo cáo phần Frontend – Hệ thống quản lý học tập theo môn học tích hợp AI

| | |
|---|---|
| **Người thực hiện** | Nguyễn Tuấn Anh – 0203768 |
| **Vai trò trong nhóm** | Frontend: thiết kế UX/UI và xây dựng giao diện React, tích hợp API |
| **Ngày cập nhật** | 14/11/2026 |

## 1. Mục tiêu

Xây dựng giao diện web cho hệ thống quản lý học tập theo môn học, phục vụ ba nhóm người dùng:

- **Sinh viên:** xem môn học, bài tập, điểm và hỏi trợ lý AI.
- **Giảng viên:** xem bài nộp, chấm điểm và dùng trợ lý AI hỗ trợ giảng dạy.
- **Quản trị viên:** quản lý danh sách người dùng.

Giao diện giao tiếp với backend qua REST API và có chế độ dữ liệu mẫu để phát triển độc lập khi backend chưa hoàn thiện.

## 2. Công nghệ sử dụng

| Công nghệ | Mục đích |
|---|---|
| React 18 + TypeScript | Xây dựng giao diện, kiểm soát kiểu dữ liệu |
| Vite | Công cụ build và chạy môi trường phát triển |
| React Router | Điều hướng và phân quyền theo vai trò |
| TanStack Query | Gọi API, cache dữ liệu, quản lý trạng thái loading/lỗi |
| Axios | Gọi HTTP, gắn token, xử lý lỗi tập trung |
| CSS thuần (biến CSS) | Giao diện, responsive, không phụ thuộc thư viện UI |

## 3. Cấu trúc thư mục

```
src/
├── auth/          AuthContext (đăng nhập, lưu phiên), ProtectedRoute (phân quyền)
├── components/    State (loading/lỗi/rỗng), AiAssistant (khung chat AI)
├── layouts/       AppLayout (menu theo vai trò, thanh trên cùng)
├── pages/
│   ├── Login.tsx
│   ├── student/   StudentCourses, CourseDetail
│   ├── lecturer/  Grading
│   └── admin/     Users
├── services/
│   ├── api.ts       Axios instance, interceptor
│   ├── lms.ts       Các hàm gọi API
│   └── mockData.ts  Dữ liệu mẫu
├── types.ts       Kiểu dữ liệu dùng chung
└── styles.css
```

Thiết kế tách riêng lớp gọi API (`services`) khỏi giao diện (`pages`, `components`), nên khi backend đổi đường dẫn hoặc định dạng dữ liệu chỉ cần sửa trong `services/lms.ts`.

## 4. Thiết kế UX

### 4.1. Nguyên tắc

- Mỗi vai trò có menu và màn hình riêng, chỉ hiển thị chức năng liên quan.
- Mọi màn hình hiển thị đủ ba trạng thái: đang tải, lỗi (có hướng dẫn xử lý) và dữ liệu rỗng (có gợi ý hành động).
- Nội dung giao diện bằng tiếng Việt, dùng động từ rõ nghĩa ("Lưu điểm", "Đăng nhập").
- Hỗ trợ responsive (menu chuyển lên đầu trang trên màn hình nhỏ), có viền focus khi dùng bàn phím và tôn trọng thiết lập giảm chuyển động.

### 4.2. Luồng người dùng chính

| Vai trò | Luồng |
|---|---|
| Sinh viên | Đăng nhập → Danh sách môn học → Chi tiết môn → Xem bài tập, trạng thái, điểm |
| Giảng viên | Đăng nhập → Chấm bài → Nhập điểm (0–10) → Lưu điểm |
| Quản trị viên | Đăng nhập → Danh sách người dùng |
| Sinh viên, giảng viên | Bấm "Hỏi trợ lý AI" ở thanh trên cùng → Nhập câu hỏi → Nhận câu trả lời |

### 4.3. Màn hình

_[Chèn ảnh chụp màn hình vào thư mục `docs/images/` rồi gắn vào đây]_

- Trang đăng nhập: `![Đăng nhập](docs/images/login.png)`
- Danh sách môn học: `![Môn học](docs/images/courses.png)`
- Chi tiết môn học: `![Chi tiết môn](docs/images/course-detail.png)`
- Chấm bài (giảng viên): `![Chấm bài](docs/images/grading.png)`
- Quản lý người dùng (quản trị): `![Người dùng](docs/images/users.png)`
- Trợ lý AI: `![Trợ lý AI](docs/images/ai.png)`

## 5. Phân quyền

- Phiên đăng nhập (token và thông tin người dùng) được lưu ở trình duyệt.
- `ProtectedRoute` chặn người chưa đăng nhập (chuyển về `/login`) và chặn truy cập sai vai trò (chuyển về trang chủ của vai trò đó).
- Menu trong `AppLayout` thay đổi theo vai trò; quản trị viên không thấy nút trợ lý AI.

| Vai trò | Đường dẫn |
|---|---|
| Sinh viên | `/student/courses`, `/student/courses/:id` |
| Giảng viên | `/lecturer/grading` |
| Quản trị viên | `/admin/users` |

Lưu ý: phân quyền ở frontend chỉ để định hướng giao diện. Backend vẫn phải kiểm tra quyền cho mọi API.

## 6. Tích hợp API

### 6.1. Cơ chế

- Axios instance dùng chung, địa chỉ cấu hình qua biến môi trường `VITE_API_URL`.
- Request tự gắn header `Authorization: Bearer <token>`.
- Khi nhận lỗi 401 (hết hạn đăng nhập), hệ thống xóa phiên và chuyển về trang đăng nhập.
- Dữ liệu được gọi và cache bằng TanStack Query; thao tác ghi (chấm điểm) dùng `useMutation` và làm mới danh sách sau khi lưu thành công.

### 6.2. Danh sách endpoint frontend đang dùng

| Phương thức | Đường dẫn | Mục đích |
|---|---|---|
| POST | `/auth/login` | Đăng nhập, trả `{ token, user }` |
| GET | `/courses` | Danh sách môn học |
| GET | `/courses/{id}` | Chi tiết môn học |
| GET | `/courses/{id}/assignments` | Bài tập của môn |
| GET | `/submissions` | Bài nộp cần chấm |
| PATCH | `/submissions/{id}/grade` | Lưu điểm, body `{ score }` |
| GET | `/admin/users` | Danh sách người dùng |
| POST | `/ai/chat` | Hỏi AI, body `{ messages }`, trả `{ reply }` |

> Các endpoint trên là thỏa thuận ban đầu với backend. Nếu backend đặt tên khác, cập nhật trong `src/services/lms.ts`.

### 6.3. Chế độ dữ liệu mẫu

Đặt `VITE_USE_MOCK=true` (mặc định) để chạy bằng dữ liệu mẫu, chưa cần backend. Đặt `VITE_USE_MOCK=false` để gọi API thật. Ở chế độ mẫu, email chứa `gv` vào vai giảng viên, chứa `admin` vào vai quản trị viên, còn lại là sinh viên.

## 7. Tích hợp AI

Thành phần `AiAssistant` là khung chat dạng bảng trượt bên phải:

- Gửi toàn bộ lịch sử hội thoại tới `/ai/chat` để AI hiểu ngữ cảnh.
- Hiển thị trạng thái "đang soạn câu trả lời", báo lỗi và cho phép gửi lại.
- Có dòng nhắc nội dung do AI tạo, người dùng cần kiểm tra lại với tài liệu môn học.

## 8. Hướng dẫn chạy

Yêu cầu: Node.js bản LTS.

```bash
npm install
cp .env.example .env     # Windows: copy .env.example .env
npm run dev
```

Mở `http://localhost:5173`. Build bản triển khai: `npm run build`.

## 9. Kết quả đạt được

- Hoàn thành khung giao diện, đăng nhập và phân quyền cho ba vai trò.
- Hoàn thành các màn hình: môn học, chi tiết môn, chấm bài, quản lý người dùng.
- Hoàn thành khung chat AI và lớp gọi API có chế độ dữ liệu mẫu.

## 10. Hạn chế và hướng phát triển

- Chưa có chức năng nộp bài (tải tệp lên) cho sinh viên.
- Chưa có giao diện tạo và chỉnh sửa bài tập, tài liệu cho giảng viên.
- Trang quản trị mới dừng ở xem danh sách; cần thêm thêm/sửa/khóa tài khoản và quản lý môn học.
- Chưa có dashboard thống kê và chưa có kiểm thử tự động.
- Trợ lý AI mới trả lời theo từng lượt, chưa hỗ trợ hiển thị dạng streaming và chưa gắn ngữ cảnh môn học cụ thể.
- Chưa có cơ chế làm mới token (refresh token) khi hết hạn.
