# Chạy local và chuẩn bị triển khai

Hướng dẫn cho bản mã ngày 29/10/2026. Kết quả kiểm tra, trạng thái áp dụng migration vào DB local và vị trí backup thực tế được ghi tại [VERIFICATION](docs/VERIFICATION.md). Hướng dẫn này không tuyên bố đã deploy production.

## Local Windows

Từ gốc repository:

```powershell
dotnet restore BE/LMS.sln
dotnet build BE/LMS.sln -c Release --disable-build-servers -m:1
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --launch-profile LMS.Core.API
```

Terminal thứ hai:

```powershell
cd FE
npm ci
npm run dev
```

API 5088, FE 5173. Swagger chỉ Development. Đọc [ENV_SETUP](docs/ENV_SETUP.md) để tạo `.env`/`FE/.env.local` và provision tài khoản đầu tiên. Bản local không bắt buộc Docker. Dừng API trước khi build lại cùng configuration để tránh khóa DLL/EXE.

Backend tự nạp `.env` gốc repository. Environment variables và CLI được ưu tiên cao hơn file. `--no-launch-profile` cần dùng khi tự chọn môi trường/URL, nhất là Production.

## Backup trước nâng cấp

Xác định đúng connection và Storage root đang dùng. Đường dẫn tương đối mặc định thuộc `BE/Services/Core/LMS.Core.API`. Dừng API và mọi tiến trình ghi trước khi backup DB cùng Uploads.

Ví dụ với đường dẫn SQLite mặc định, từ gốc dự án:

```powershell
$apiDirectory = (Resolve-Path -LiteralPath 'BE/Services/Core/LMS.Core.API').Path
$databasePath = Join-Path $apiDirectory 'lms-ai.sqlite'
$backupDirectory = Join-Path (Get-Location).Path ('artifacts/backups/manual_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $backupDirectory -ErrorAction Stop | Out-Null
$backupDatabase = Join-Path $backupDirectory 'lms-ai.sqlite'
python -c "import sqlite3,sys; s=sqlite3.connect(sys.argv[1]); d=sqlite3.connect(sys.argv[2]); s.backup(d); d.close(); s.close()" $databasePath $backupDatabase
if ($LASTEXITCODE -ne 0) { throw 'SQLite backup failed' }
if (Test-Path -LiteralPath (Join-Path $apiDirectory 'Uploads')) {
    Copy-Item -LiteralPath (Join-Path $apiDirectory 'Uploads') -Destination $backupDirectory -Recurse
}
```

Nếu cấu hình path khác, thay hai đường dẫn đầu bằng DB/Uploads thực tế. SQLite backup API tạo bản sao nhất quán, gồm dữ liệu đã commit trong WAL. Không chỉ copy file `.sqlite` khi vẫn có writer. Backup chứa dữ liệu người dùng; không đưa vào source ZIP hoặc commit.

## Kiểm tra bản sao rồi áp dụng migrations

Có 6 migration riêng cho mỗi provider. Thay đổi mới thêm profile metadata, material metadata, nguồn/chunk AI và Quiz revision; không thêm bảng nghiệp vụ ngoài 13 bảng hiện có.

Kiểm tra bản sao DB hiện tại, file nguồn không bị thay đổi:

```powershell
python scripts/verify_legacy_upgrade.py --database BE/Services/Core/LMS.Core.API/lms-ai.sqlite --configuration Release
```

Công cụ sao lưu sang thư mục tạm, áp dụng migration bằng API thật trên bản sao và so sánh từng giá trị ở cột cũ, số hàng, FK/integrity và migration history. Không truyền `--database` sẽ tạo fixture legacy để kiểm thử.

Sau khi backup và kiểm tra bản sao đạt, áp dụng vào connection mục tiêu rồi thoát:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Demo__Seed = 'false'
$env:Bootstrap__AdminUsername = ''
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --no-launch-profile -- --migrate-only
```

Chạy API bình thường và kiểm tra `/api/health/ready` trả 200. Development mặc định auto migrate, nhưng với dữ liệu cần giữ nên thực hiện bước backup/kiểm tra trước khi khởi động bản mới. Production mặc định không tự migrate; dùng lệnh `--migrate-only` với secret/CORS/connection của môi trường đó.

Không dùng `EnsureDeleted` hoặc xóa file DB để sửa lỗi. Migration Down có thể mất dữ liệu cột mới; rollback môi trường có dữ liệu bằng DB/Uploads backup và binary cùng phiên bản sau review.

## SQLite có bảng nhưng không có migration history

Nếu gặp `Existing database has no EF migration history`, API dừng trước khi thay schema. Dừng API rồi xác minh:

```powershell
python scripts/baseline_sqlite.py --database BE/Services/Core/LMS.Core.API/lms-ai.sqlite
```

Chỉ khi schema chính xác của bản legacy được chấp nhận, chạy:

```powershell
python scripts/baseline_sqlite.py --database BE/Services/Core/LMS.Core.API/lms-ai.sqlite --apply
```

Mặc định chỉ đọc. `--apply` khóa ghi, tạo backup nhất quán, kiểm tra DDL/index/FK/integrity và ghi baseline đã tồn tại; không chạy CREATE TABLE lại. Schema tùy chỉnh, view/trigger hoặc history lạ bị từ chối. DB đã có history dùng migrations còn thiếu, không cần nhận baseline lại.

SQL Server legacy phải được DBA đối chiếu schema và ghi baseline sau review riêng; không dùng công cụ SQLite cho SQL Server.

## EF Core theo provider

Service inject `LmsDbContext`; hai context `SqliteLmsDbContext` và `SqlServerLmsDbContext` có snapshot riêng. Khi đổi model, scaffold cả hai bộ, kiểm tra SQL và thử nâng cấp trên bản sao. Dừng API cùng bản build trước khi scaffold.

```powershell
dotnet ef migrations add TenThayDoi --context SqliteLmsDbContext --project BE/Services/Core/LMS.Core.Infrastructure --startup-project BE/Services/Core/LMS.Core.API --configuration Release --output-dir Persistence/Migrations/Sqlite
dotnet ef migrations add TenThayDoi --context SqlServerLmsDbContext --project BE/Services/Core/LMS.Core.Infrastructure --startup-project BE/Services/Core/LMS.Core.API --configuration Release --output-dir Persistence/Migrations/SqlServer
```

Dùng `dotnet-ef` phiên bản tương thích EF Core 10 đang tham chiếu. Design factory chỉ dành cho scaffold/script, không trỏ tới connection runtime. Vì vậy không dùng `dotnet ef database update` để ngầm áp dụng vào DB đang cấu hình qua factory.

Sinh SQL Server idempotent script:

```powershell
dotnet ef migrations script --idempotent --context SqlServerLmsDbContext --project BE/Services/Core/LMS.Core.Infrastructure --startup-project BE/Services/Core/LMS.Core.API --configuration Release --output docs/sqlserver-migrations.sql
```

DBA review `docs/sqlserver-migrations.sql` rồi áp dụng đúng connection mục tiêu. SQLite dùng migration runtime/history, không có script idempotent tương đương.

## SQL Server qua Docker hoặc server riêng

Compose chỉ chạy SQL Server 2022 tại `localhost,14333` và volume `lms_sql_data`:

```powershell
$sqlPassword = Read-Host 'SQL Server SA password' -AsSecureString
$env:MSSQL_SA_PASSWORD = [System.Net.NetworkCredential]::new('', $sqlPassword).Password
docker compose config --quiet
docker compose up -d
$env:Database__Provider = 'SqlServer'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
# Đặt ConnectionStrings__LmsDb riêng tới localhost,14333; không in password.
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --no-launch-profile -- --migrate-only
dotnet run --project BE/Services/Core/LMS.Core.API -c Release --no-build --launch-profile LMS.Core.API
```

Chuỗi kết nối minh họa: `Server=localhost,14333;Database=LmsAi;User Id=sa;Password=<secret>;Encrypt=True;TrustServerCertificate=True`. Tùy chọn tin certificate trên chỉ cho local có kiểm soát; production dùng certificate được xác thực và tài khoản ứng dụng ít quyền hơn SA.

Đổi provider chọn một DB khác, không di chuyển dữ liệu SQLite. Không chạy `docker compose down -v` với dữ liệu cần giữ. SQL Server thực chưa được nghiệm thu trong workspace: Docker CLI có nhưng daemon pipe không hoạt động. Sinh migration/script và build không thay thế kiểm thử server thật.

## Build và chạy Production

```powershell
dotnet publish BE/Services/Core/LMS.Core.API -c Release -o artifacts/publish
cd FE
npm ci
# Cấu hình VITE_API_BASE=https://api.example.edu trong nguồn env của FE trước khi build.
npm run build
```

Phục vụ `FE/dist` bằng static hosting có HTTPS. Chạy bản API trên host có ASP.NET Core 10 runtime, với content root là thư mục publish. Cấu hình tối thiểu:

| Cấu hình | Yêu cầu |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Production, đặt ở environment/host |
| `Jwt__Key` | Key ngẫu nhiên riêng, tối thiểu 32 byte UTF-8; không dùng key demo |
| `Cors__AllowedOrigins__0` | Origin HTTPS của FE, không wildcard/path/slash cuối |
| `Database__Provider` và connection | Đúng DB mục tiêu, account ứng dụng phù hợp |
| `Database__AutoMigrate` | false; migration được áp dụng rõ ràng trước start |
| `Demo__Seed` | false |
| `Storage__RootPath` | Thư mục bền vững; quyền đọc/ghi chỉ cần cho API |
| `Ai__Mode` | Disabled hoặc Gemini với key/model/quota đã kiểm chứng |
| `Security__RequireHttps` | true mặc định ngoài Development |

Nếu dùng reverse proxy, cấu hình IP tin cậy tại `ReverseProxy__KnownProxies__N`; chuyển đúng `X-Forwarded-For` và `X-Forwarded-Proto`. Cấu hình cổng HTTPS đích khi API tự redirect. Không mở tin cậy mọi proxy. Swagger tắt ngoài Development.

Health liveness là `/api/health`; readiness `/api/health/ready` chỉ đạt khi DB kết nối được và không còn pending migrations. Readiness không gọi Gemini hoặc kiểm tra mọi file học liệu.

Giới hạn mặc định: auth 30 request/phút/IP, sinh AI 6 request/phút/user. HTTP 429 local có `Retry-After: 60`. Upload tối đa 5.000.000 byte; trần request multipart bằng `Storage:MaxUploadBytes + 65536`. Cấu hình proxy/IIS nhất quán với giới hạn này.

`npm run preview` phục vụ local tại 127.0.0.1:4173 để xem bundle; thêm origin này vào CORS nếu kiểm tra với API. Preview không phải cấu hình hosting production.

## Kiểm tra sau triển khai và bàn giao

Chạy [bộ kiểm thử](README.md#kiểm-thử), readiness, đăng nhập từng role, kiểm tra download/auth và toàn bộ luồng Course → Draft → Approve → Publish → Submit → Result trên môi trường mục tiêu. Dùng [biên bản kiểm chứng](docs/VERIFICATION.md) để phân biệt offline, live Gemini và các môi trường chưa đạt.

Khi gửi source, loại `.env`/`FE/.env.local`, key/certificate riêng, DB/WAL/SHM, Uploads người dùng, backups, logs/artifacts, `bin/obj`, `node_modules` và `dist`. Giữ `.env.example`, source, lockfiles, migrations, scripts và tài liệu. Chuyển dữ liệu thực bằng quy trình riêng nếu người nhận cần; không gộp secret vào gói source.
