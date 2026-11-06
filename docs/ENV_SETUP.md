# Cấu hình Backend và Frontend

Đối chiếu mã ngày 29/10/2026. Cấu hình không tự thay đổi tài khoản hoặc chuyển dữ liệu khi đổi provider.

## File nào dùng ở đâu?

| File/nguồn | Bên đọc | Nội dung |
|---|---|---|
| `BE/Services/Core/LMS.Core.API/appsettings.json` | Backend | Mặc định không bí mật, Storage limits, provider và AI |
| `appsettings.Development.json` | Backend Development | Mặc định local, auto migrate |
| `.env` ở gốc repository | Backend | Cấu hình riêng của máy; không đưa vào source ZIP/Git |
| `FE/.env.local` | Vite Frontend | `VITE_API_BASE`; không đặt secret |
| Environment variables | Tiến trình Backend/Compose | Giá trị override và secrets |
| .NET User Secrets | Backend Development | Secrets local ngoài repository |

Backend đã có bộ nạp `.env` riêng. Khi chạy trong repository, nó tìm gốc có `BE/LMS.sln` và `FE/package.json`; bản publish đọc `.env` tại content root cạnh `appsettings.json`.

Thứ tự ưu tiên, từ thấp đến cao: appsettings → appsettings theo môi trường → User Secrets (Development) → `.env` → biến môi trường của tiến trình → command line. Vì vậy biến đã đặt trong terminal có thể che giá trị bạn vừa sửa ở `.env`.

Tên `Ai__Mode` trong environment/`.env` tương ứng khóa `Ai:Mode` của .NET. Đặt `DotEnv__Enabled=false` trong môi trường để tắt đọc file; dùng khi tests hoặc deployment quản lý toàn bộ cấu hình riêng.

`ASPNETCORE_ENVIRONMENT` và host settings được quyết định sớm: đặt ngoài `.env` hoặc trong launch profile. Launch profile `LMS.Core.API` chọn Development và cổng 5088; `--no-launch-profile` cho phép dùng cấu hình host bạn cung cấp.

## Tạo cấu hình local

Giữ file đang có. Khi chưa có:

```powershell
if (-not (Test-Path -LiteralPath '.env')) {
    Copy-Item -LiteralPath '.env.example' -Destination '.env'
}
```

Mẫu để chạy LMS bằng SQLite, AI tắt:

```dotenv
Database__Provider=Sqlite
Database__AutoMigrate=true
ConnectionStrings__Sqlite=Data Source=lms-ai.sqlite
Storage__RootPath=Uploads
Demo__Seed=false
Ai__Mode=Disabled
Cors__AllowedOrigins__0=http://localhost:5173
Cors__AllowedOrigins__1=http://127.0.0.1:5173
```

Đường dẫn SQLite và Storage tương đối tính từ thư mục API, kể cả khi gõ lệnh từ gốc hoặc `BE`. Có thể đặt đường dẫn tuyệt đối. Production nên dùng thư mục dữ liệu bền vững và AutoMigrate=false.

Frontend, tạo/sửa `FE/.env.local`:

```dotenv
VITE_API_BASE=http://localhost:5088
```

Không đưa `Ai__GeminiApiKey`, `Jwt__Key` hoặc connection password vào `VITE_*`; Vite đưa các giá trị đó vào bundle trình duyệt. Restart API/Vite sau khi đổi file tương ứng. Đổi `VITE_API_BASE` cho production cần build FE lại.

## Tài khoản và JWT

| Biến | Ý nghĩa |
|---|---|
| `Demo__Seed=false` | Không tạo tài khoản demo |
| `Demo__Seed=true` | Chỉ Development, chỉ khi Users rỗng; cần `Demo__Password` để tạo admin/lecturer/student |
| `Bootstrap__AdminUsername`, `Bootstrap__AdminFullName`, `Bootstrap__AdminPassword` | Provision Admin đầu tiên; không nâng quyền tài khoản có sẵn |
| `Jwt__Key` | Secret ký JWT, tối thiểu 32 byte UTF-8; dùng ngẫu nhiên riêng cho từng môi trường |
| `Jwt__Issuer` / `Jwt__Audience` | Mặc định LMS-AI / LMS-AI-Frontend |

Mật khẩu seed/bootstrap cần 10–128 ký tự, đủ hoa/thường/số/ký hiệu. Đăng ký Student, tạo Lecturer và đổi mật khẩu đang áp dụng giới hạn nghiệp vụ 8–128 ký tự. Bật seed không reset hoặc đổi mật khẩu tài khoản cũ.

Development thiếu JWT key sẽ sinh key tạm và yêu cầu đăng nhập lại sau restart. Để key ổn định, đặt secret riêng qua môi trường/User Secrets hoặc file `.env` được giữ kín. Ví dụ sinh key vào môi trường PowerShell, không in ra màn hình:

```powershell
$jwtBytes = New-Object byte[] 48
$jwtGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
$jwtGenerator.GetBytes($jwtBytes)
$env:Jwt__Key = [Convert]::ToBase64String($jwtBytes)
$jwtGenerator.Dispose()
```

Biến này chỉ sống trong terminal hiện tại. Khi đóng terminal, cần nguồn secret bền vững của môi trường. Không dùng chung key giữa máy demo và production. Dù key ổn định, JWT vẫn hết hạn và bị thu hồi bởi SecurityVersion sau logout/đổi mật khẩu/khóa/mở khóa.

## Bật Gemini thật

Đặt mode, API key và model trong Backend rồi restart:

```powershell
$env:Ai__Mode = 'Gemini'
$env:Ai__GeminiModel = 'gemini-3.5-flash-lite'
$geminiKey = Read-Host 'Google Gemini API key' -AsSecureString
$env:Ai__GeminiApiKey = [System.Net.NetworkCredential]::new('', $geminiKey).Password
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --launch-profile LMS.Core.API
```

`gemini-3.5-flash-lite` đã sinh 3 Draft thành công trong lần kiểm chứng local ngày 29/10/2026. Đây là ví dụ model đã kiểm chứng với tài khoản hiện tại, không bảo đảm quyền/quota của tài khoản khác hoặc ở thời điểm khác. `appsettings.json` vẫn giữ model `gemini-3.8-flash`; nguồn ưu tiên cao hơn quyết định giá trị cuối. Ứng dụng không tự chọn model thay bạn.

`Ai__Mode=Disabled` chỉ tắt sinh câu hỏi AI. `Mock` chỉ được dùng ở Development và được đánh dấu mô phỏng; production từ chối. `GET /api/ai/questions/status` cần tài khoản Lecturer; `enabled` chỉ phản ánh cấu hình, không thực hiện request kiểm tra Google.

| Biến AI | Mặc định trong mã | Khoảng hỗ trợ/tác dụng |
|---|---|---|
| `Ai__TimeoutSeconds` | 45 | 5–120 giây, giới hạn một lần xử lý đoạn gồm mạng và chờ retry; local đã dùng 120 |
| `Ai__MaxSourceCharacters` | 16000 | 1000–50000 đơn vị UTF-16 mỗi đoạn |
| `Ai__MaxGenerationChunks` | 3 | 1–5 đoạn; còn bị giới hạn bởi số câu yêu cầu |
| `Ai__MaxProviderRequests` | 4 | 1–12 lần gửi, dùng chung mọi đoạn và retry |
| `Ai__MaxRetries` | 1 | 0–2 lần thử lại mỗi đoạn, chỉ Google 429/503 |
| `Ai__RetryBaseDelaySeconds` | 2 | 0,1–10 giây; exponential backoff và jitter |
| `Ai__MaxRetryDelaySeconds` | 30 | 1–60 giây; Retry-After lớn hơn mức này kết thúc request, không rút ngắn thời gian Google yêu cầu |
| `Ai__GenerationTimeoutSeconds` | 240 | 5–300 giây cho provider/retry/validation trên toàn bộ đoạn; ghi DB dùng cancellation của request |

API công khai nhận 3–10 câu, độ khó Easy/Medium/Hard. Kế hoạch chia đoạn không gọi Google. Tăng giới hạn không bảo đảm model chấp nhận request và có thể tăng quota/chi phí. [Xử lý lỗi và validation](AI_SECURITY.md).

## Giới hạn học liệu

| Biến | Mặc định | Giới hạn cấu hình |
|---|---:|---:|
| `Storage__MaxUploadBytes` | 5000000 | 1–100000000 byte |
| `Storage__MaxExtractedCharacters` | 2000000 | 1–10000000 đơn vị UTF-16 |
| `Storage__MaxArchiveExpandedBytes` | 50000000 | 1–200000000 byte |
| `Storage__MaxArchiveEntries` | 2000 | 1–20000 mục |
| `Storage__MaxPdfPages` | 500 | 1–5000 trang |
| `Storage__MaxPreviewCharacters` | 30000 | 1–100000 đơn vị UTF-16 |

Các Storage limit ngoài khoảng làm API dừng với lỗi cấu hình; AI limits được chặn về khoảng hỗ trợ. Trần request Kestrel/multipart bằng MaxUploadBytes + 65536 byte. Proxy/IIS cần cho phép phần bao gói này. Preview có thể cắt với `truncated=true`; nội dung trích xuất dùng AI không bị cắt lặng lẽ.

## CORS, SQL Server và production

`Cors__AllowedOrigins__N` là origin chính xác: scheme + host + port, không path hoặc slash cuối. Ví dụ preview FE cần origin `http://127.0.0.1:4173`. CORS không thay thế xác thực/phân quyền.

SQL Server dùng `Database__Provider=SqlServer`, `ConnectionStrings__LmsDb` riêng và migration tương ứng. Đổi provider không chuyển dữ liệu SQLite. Compose đọc `MSSQL_SA_PASSWORD` và chỉ chạy SQL Server tại cổng 14333.

Production đặt `ASPNETCORE_ENVIRONMENT=Production` ngoài file, `Database__AutoMigrate=false`, `Demo__Seed=false`, JWT key/CORS/connection/storage rõ ràng. Cấu hình TLS/proxy và lệnh migrate theo [DEPLOYMENT](../DEPLOYMENT.md).

## Khi cấu hình không có tác dụng

- Kiểm tra terminal còn biến override: `Get-Item Env:Ai__Mode` hoặc `Get-Item Env:Ai__GeminiModel`. Chỉ kiểm tra biến không bí mật; không in API key/JWT key.
- Nếu muốn dùng giá trị từ file, bỏ riêng override tương ứng, ví dụ `Remove-Item Env:Ai__Mode`, rồi restart API.
- Không kết nối máy chủ: kiểm tra `/api/health/ready`, cổng/API process, `VITE_API_BASE` và CORS trước.
- HTTP 503 từ Gemini khác với API local không chạy; đọc message có `Google HTTP 503` và log đã làm sạch.
- Thiếu migration history: làm theo [DATABASE](DATABASE.md), không xóa SQLite.

Bộ đọc `.env` hỗ trợ `NAME=value`, `export NAME=value`, nháy đơn/đôi và comment. Không chạy shell, không nội suy biến, không giải mã escape. Syntax error chỉ chỉ ra số dòng; không in secret.
