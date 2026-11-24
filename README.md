# LMS-AI

LMS dạy–học–kiểm tra dùng ASP.NET Core 10, React 19 và EF Core, theo kiến trúc Layered N-Tier Modular Monolith. Backend hỗ trợ SQLite và SQL Server; Google Gemini hỗ trợ giảng viên soạn câu hỏi. Dữ liệu khóa học, học liệu, câu hỏi và bài nộp được giữ khi nâng cấp.

Bản nâng cấp ngày 29/10/2026 bổ sung hồ sơ/email, danh sách học viên và tiến độ, học liệu TXT/PDF/DOCX/PPTX/Markdown, chia tài liệu dài để sinh câu hỏi, dashboard sinh viên và thống kê khóa học. [Biên bản kiểm chứng](docs/VERIFICATION.md) ghi kết quả cuối cùng và trạng thái nâng cấp database local.

SQLite local đã được backup rồi nâng cấp từ 2 lên 6 migrations: giữ nguyên 13 bảng nghiệp vụ, 38 hàng cũ và hash của tệp Uploads. Bằng chứng: `artifacts/verification/local-upgrade-result.json`; backup: `artifacts/backups/upgrade_before_apply_20261029T055404Z`. Không cần chạy baseline lại cho DB local đã có history này.

Đã kiểm chứng Gemini thật trên tài liệu tổng hợp 419 ký tự với cấu hình hiệu lực `gemini-3.5-flash-lite`: HTTP 200, 3 câu Draft được lưu trong SQLite cô lập, 1 lượt gọi, 4,11 giây. Đây là bằng chứng cho lần sinh ngắn đó; luồng duyệt/Quiz và lỗi/retry có kiểm thử offline riêng. SQL Server runtime, UI tương tác/responsive trong trình duyệt và triển khai production vẫn cần môi trường nghiệm thu.

## Chạy trên Windows

Cần .NET 10 SDK, Node.js 22.12+ và npm. Python 3 dùng cho các công cụ kiểm tra/nâng cấp SQLite; ứng dụng không cần Python để phục vụ API. Docker chỉ cần khi chọn SQL Server bằng Compose.

Từ thư mục gốc dự án, giữ `.env` đang có. Nếu chưa có, tạo bản cấu hình từ mẫu:

```powershell
if (-not (Test-Path -LiteralPath '.env')) {
    Copy-Item -LiteralPath '.env.example' -Destination '.env'
}
dotnet restore BE/LMS.sln
dotnet build BE/LMS.sln -c Release --disable-build-servers -m:1
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --launch-profile LMS.Core.API
```

Dừng tiến trình API đang dùng cùng bản build trước khi build lại. API chạy tại http://localhost:5088; Swagger Development: http://localhost:5088/swagger; readiness: http://localhost:5088/api/health/ready.

Mở terminal thứ hai từ thư mục gốc:

```powershell
cd FE
npm ci
npm run dev
```

Mở http://localhost:5173. `FE/.env.local` chỉ cần:

```dotenv
VITE_API_BASE=http://localhost:5088
```

Backend tự đọc `.env` ở gốc repository. Frontend đọc `FE/.env.local`. Restart Backend sau khi đổi cấu hình; restart Vite sau khi đổi biến `VITE_*`. API key và JWT secret chỉ đặt ở Backend. [Hướng dẫn từng cấu hình](docs/ENV_SETUP.md).

SQLite và Uploads mặc định nằm trong `BE/Services/Core/LMS.Core.API`. Đường dẫn tương đối được quy về thư mục API. Không xóa database để sửa lỗi khởi động.

## Database cũ

Database đã có `__EFMigrationsHistory`: backup DB/Uploads, thử nâng cấp trên bản sao rồi áp dụng các migration còn thiếu theo [DEPLOYMENT](DEPLOYMENT.md). Bộ mã hiện có 6 migration cho mỗi provider.

Nếu API báo `Existing database has no EF migration history`, dừng API và xác minh schema legacy trước:

```powershell
python scripts/baseline_sqlite.py --database BE/Services/Core/LMS.Core.API/lms-ai.sqlite
```

Chỉ với đúng schema legacy được công cụ chấp nhận, thêm `--apply` để công cụ tạo backup và ghi baseline đã tồn tại. File có schema khác cần được review riêng. Không chạy baseline cho mọi database, không reset dữ liệu. [Chi tiết schema và nâng cấp](docs/DATABASE.md).

## Tài khoản đầu tiên

Database đang có giữ nguyên tài khoản và mật khẩu. Bật seed không đổi mật khẩu tài khoản hiện hữu.

Với database mới, cấu hình Admin đầu tiên trong terminal Backend:

```powershell
$env:Bootstrap__AdminUsername = 'admin'
$env:Bootstrap__AdminFullName = 'Quản trị viên'
$adminPassword = Read-Host 'Mật khẩu Admin mới' -AsSecureString
$env:Bootstrap__AdminPassword = [System.Net.NetworkCredential]::new('', $adminPassword).Password
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --launch-profile LMS.Core.API
```

Mật khẩu bootstrap/seed cần 10–128 ký tự, có chữ hoa, chữ thường, số và ký hiệu. Bỏ cấu hình bootstrap sau khi provision. Admin tạo Lecturer; Student tự đăng ký.

`Demo__Seed=false` là tắt tạo tài khoản demo. Chỉ Development mới cho phép `Demo__Seed=true` cùng `Demo__Password` riêng; khi Users rỗng sẽ tạo `admin`, `lecturer`, `student`. Không tạo khóa học/bài nộp mẫu và không ghi đè dữ liệu có sẵn.

## Luồng nghiệp vụ

1. Admin tạo Lecturer, tìm/lọc tài khoản và khóa/mở khóa. Người dùng xem hồ sơ, cập nhật họ tên/email; không tự đổi role hoặc trạng thái khóa.
2. Lecturer tạo/sửa Course, chia sẻ JoinCode, quản lý Topic và danh sách học viên. Student tham gia một lần bằng mã.
3. Lecturer upload TXT, PDF có lớp chữ, DOCX, PPTX hoặc Markdown; xem trạng thái/nội dung trích xuất, tải file và xóa theo quyền. PDF scan/slide chỉ có ảnh được báo cần OCR hoặc không có text.
4. Lecturer soạn câu thủ công hoặc xem kế hoạch chia đoạn rồi sinh bằng Gemini. Câu AI lưu Draft, có nguồn học liệu, độ khó và khoảng ký tự của đoạn.
5. Lecturer sửa Draft, duyệt Approved, chọn câu cùng Course để lưu Quiz Draft rồi publish.
6. Student làm Quiz Published, trả lời đủ câu và nộp một lần. Backend chấm điểm 0–10, lưu Attempt/StudentAnswer và bảo vệ request nộp trùng.
7. Student xem dashboard, lịch sử và chủ đề cần ôn; Lecturer xem thống kê từng khóa, điểm từng học viên và chủ đề nhiều câu sai.

Tiến độ là tỷ lệ Quiz Published đã nộp, không phải tỷ lệ đọc tài liệu. Điểm trung bình thiếu dữ liệu trả `null`. Dashboard/thống kê hiện tại chỉ tính người đang ghi danh và bài nộp `Submitted`. Lịch sử bài nộp đã có vẫn được bảo toàn.

Gợi ý ôn tập dùng quy tắc tỷ lệ đúng dưới 60%, tối đa 2 học liệu còn hoạt động mỗi Topic. API/UI ghi rõ `RuleBased` và lý do; không gọi đây là kết quả Gemini.

Approved Question và Published Quiz không sửa nội dung qua API. Xóa học liệu là xóa mềm: ngừng truy cập mới nhưng giữ file, câu hỏi và kết quả liên quan. JWT cũ bị thu hồi sau logout, đổi mật khẩu hoặc khóa/mở khóa tài khoản.

## Gemini và giới hạn xử lý

Để bật AI thật, đặt `Ai__Mode=Gemini`, `Ai__GeminiApiKey` riêng và model tài khoản được phép sử dụng. `Ai__Mode=Disabled` vẫn cho phép toàn bộ luồng LMS thủ công. Xem [AI_SECURITY](docs/AI_SECURITY.md).

Giới hạn mặc định: 3–10 câu, 16.000 đơn vị UTF-16/đoạn, tối đa 3 đoạn, 4 lượt gửi tổng cộng kể cả retry. Backend chỉ retry lỗi Google 429/503, tối đa 1 lần mỗi đoạn trong budget chung; có backoff/jitter và tôn trọng Retry-After. Tài liệu vượt khả năng xử lý bị từ chối trước khi gọi, không âm thầm cắt mất phần còn lại. Không tự chuyển model, bật billing, duyệt hoặc publish câu AI.

File `appsettings.json` hiện giữ model `gemini-3.8-flash`; cấu hình riêng `.env` dùng trong lần kiểm chứng trên là `gemini-3.5-flash-lite`. Giá trị hiệu lực phụ thuộc thứ tự nguồn cấu hình. Tên model trong file và `status.enabled` không chứng minh model gọi được: cần kiểm tra kết quả request thật.

## Kiểm thử

Từ thư mục gốc, build trước rồi chạy tuần tự. Các project kiểm thử là console app, dùng `dotnet run`:

Có thể chạy toàn bộ gate offline và kiểm tra bản sao DB bằng `powershell -ExecutionPolicy Bypass -File scripts/verify_all.ps1 -DatabasePath BE/Services/Core/LMS.Core.API/lms-ai.sqlite`. Script lưu log, dừng khi một gate lỗi và không gọi Google thật. Các lệnh riêng để kiểm tra từng phần:

```powershell
dotnet build BE/LMS.sln -c Release --disable-build-servers -m:1
dotnet run --project BE/Tests/LMS.SmokeTests -c Release --no-build
dotnet run --project BE/Tests/LMS.SecurityTests -c Release --no-build
dotnet run --project BE/Tests/LMS.IntegrationTests -c Release --no-build
dotnet run --project BE/Tests/LMS.HostingTests -c Release --no-build
dotnet run --project BE/Tests/LMS.ProfileTests -c Release --no-build
dotnet run --project BE/Tests/LMS.MaterialTests -c Release --no-build
dotnet run --project BE/Tests/LMS.AiAdapterTests -c Release --no-build
dotnet run --project BE/Tests/LMS.AiWorkflowTests -c Release --no-build
dotnet run --project BE/Tests/LMS.ProgressTests -c Release --no-build
python -m unittest discover -s scripts -p "test_*.py" -v
python scripts/verify_legacy_upgrade.py --configuration Release
cd FE
npm test
npm run build
```

Các test dùng database/thư mục cô lập. Mock/offline không tiêu quota Gemini. `python scripts/verify_gemini_live.py --run-live` là phép kiểm tra riêng, có gọi Google thật, tối đa 2 lượt gửi với tài liệu tổng hợp và database cô lập; chỉ chủ động chạy khi cần kiểm chứng cấu hình của mình.

## Tài liệu

- [Triển khai, migration, backup và SQL Server](DEPLOYMENT.md)
- [Các file env và thứ tự cấu hình](docs/ENV_SETUP.md)
- [API/DTO](docs/API_COBAN.md), [schema và lịch sử](docs/DATABASE.md), [kiến trúc](docs/KIEN_TRUC.md)
- [Bằng chứng kiểm tra cuối](docs/VERIFICATION.md), [trạng thái/giới hạn](docs/TRANG_THAI_VA_GIOI_HAN.md)
- [Audit](PROJECT_AUDIT.md), [checklist](PROJECT_TASKS.md), [file thay đổi](CHANGED_FILES.md)

Phạm vi chưa có: OCR, gửi email/reset mật khẩu, thanh toán, proctoring/timer, chuyển dữ liệu tự động giữa provider và hạ tầng phân tán. Dự án không bổ sung khoa/ngành/tín chỉ/học phí/điểm danh.
