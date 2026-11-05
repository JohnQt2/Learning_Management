# Hệ thống quản lý học tập – Frontend

Giao diện React cho 3 vai trò: sinh viên, giảng viên, quản trị viên; có trợ lý AI cho sinh viên và giảng viên.

## Công nghệ
Vite, React 18, TypeScript, React Router, TanStack Query, Axios.

## Chạy dự án
```bash
npm install
cp .env.example .env
npm run dev
```
Mở http://localhost:5173

## Dữ liệu mẫu và API thật
- `VITE_USE_MOCK=true` (mặc định): dùng dữ liệu mẫu, chưa cần backend.
  Đăng nhập: email chứa `gv` là giảng viên, chứa `admin` là quản trị viên, còn lại là sinh viên (mật khẩu nhập tùy ý).
- `VITE_USE_MOCK=false`: gọi API thật tại `VITE_API_URL`.

## Cấu trúc
```
src/
  auth/        đăng nhập, phân quyền theo vai trò
  components/  thành phần dùng chung (trạng thái, trợ lý AI)
  layouts/     khung giao diện có menu theo vai trò
  pages/       student/ lecturer/ admin/
  services/    api.ts (axios), lms.ts (các hàm gọi API), mockData.ts
```

## API backend cần có
| Phương thức | Đường dẫn | Mục đích |
|---|---|---|
| POST | /auth/login | Đăng nhập, trả `{ token, user }` |
| GET | /courses, /courses/{id} | Danh sách, chi tiết môn |
| GET | /courses/{id}/assignments | Bài tập của môn |
| GET | /submissions | Bài nộp cần chấm |
| PATCH | /submissions/{id}/grade | Lưu điểm `{ score }` |
| GET | /admin/users | Danh sách người dùng |
| POST | /ai/chat | Hỏi AI `{ messages }`, trả `{ reply }` |
