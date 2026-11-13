# Tiếp nhận database local để Backend khởi động — 23/10/2026

Đã xử lý trực tiếp lỗi `Existing database has no EF migration history` trên database local đang có. Build không phải nguyên nhân; database được tạo từ EnsureCreated ở bản cũ nên thiếu history.

Các thao tác đã chạy:

1. Xác minh read-only schema legacy chính xác, integrity và khóa ngoại.
2. Chạy `scripts/baseline_sqlite.py --apply`: tạo backup nhất quán trước khi ghi lịch sử baseline.
3. Chạy API `--migrate-only`, seed/bootstrap đều tắt: chỉ thêm Users.SecurityVersion (default0) và Quizzes.Description (default rỗng).
4. Đối chiếu với backup: toàn bộ giá trị cũ của13 bảng/4 hàng (3 Users,1 Course) giữ nguyên; có2 migrations trong history; integrity/FKs đạt.
5. Chạy đúng launch profile LMS.Core.API: ứng dụng listening tại http://localhost:5088; health/readiness/Swagger đều HTTP200. Đã dừng phiên kiểm tra để người dùng chạy lại trong Terminal.

Backup giữ nguyên tại:

`BE/Services/Core/LMS.Core.API/lms-ai.sqlite.20261023T133712743185Z.bak`

Không xóa/reset database, không thay mật khẩu hoặc tài khoản. File DB hiện tại đã được cập nhật schema, nên hash vật lý thay đổi là bình thường; hash gốc ghi trong audit trước đó mô tả thời điểm trước lần nâng cấp này. Các giá trị nghiệp vụ cũ được đối chiếu trực tiếp và giữ nguyên.

Database local này không cần chạy baseline thêm lần nữa. Từ thư mục BE:

```powershell
dotnet run --project Services/Core/LMS.Core.API --launch-profile LMS.Core.API
```

Hoặc từ thư mục gốc:

```powershell
dotnet run --project BE/Services/Core/LMS.Core.API --launch-profile LMS.Core.API
```

Thông báo Development tự sinh JWT signing key là thông tin bình thường; sau mỗi lần restart cần đăng nhập lại khi không cấu hình key cố định.
