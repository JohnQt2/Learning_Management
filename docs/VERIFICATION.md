# Bằng chứng kiểm thử —29/10/2026

Kiểm tra trực tiếp trên Windows trong workspace hiện tại. Các suite dùng SQLite/Uploads cô lập, credential ngẫu nhiên; không reset database thật. Browser UI và SQL Server runtime được ghi riêng, không suy từ build hoặc HTTP fixture.

## Gate cuối

Lệnh từ root:

```powershell
./scripts/verify_all.ps1 -DatabasePath BE/Services/Core/LMS.Core.API/lms-ai.sqlite
```

Lệnh đã chạy exit0; log từng bước ở `artifacts/verification/final-20261029-125102`.

| Lệnh/bộ kiểm tra | Kết quả thực tế | Phạm vi |
|---|---|---|
|dotnet restore BE/LMS.sln|exit0|13 projects|
|Release build --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false|0 warnings/errors|Toàn solution|
|LMS.SmokeTests|96 checks đạt|Nghiệp vụ, atomic graph, scoring, concurrency|
|LMS.SecurityTests|73 checks đạt|Auth/role/owner, storage, AI validation/error transport fixtures|
|LMS.IntegrationTests|105 assertions đạt|HTTP role/IDOR, Course/Enrollment/Quiz/results, race submit, restart|
|LMS.HostingTests|54 assertions đạt|Production guards, dotenv, auth/AI limits, CORS exposed headers|
|LMS.ProfileTests|66 assertions đạt|Profile/email/readonlyrole, Admin filters, roster progress, persistence|
|LMS.MaterialTests|128 assertions đạt|TXT/PDF/DOCX/PPTX/MD thật, MIME/format/size/archive/PDF limits, image-only, download/delete/auth|
|LMS.AiAdapterTests|55 assertions đạt|HTTP fixtures400/401/403/404/429/500/503/timeout/JSON, retry/backoff/Retry-After/budget/cancel|
|LMS.AiWorkflowTests|120 assertions đạt|5format extract→Draft→Approve→Quiz→score/restart; full chunk coverage/surrogates/budget/atomicfailure/deletionrace; manualdifficulty; staleQuizpublish/edit|
|LMS.ProgressTests|82 assertions đạt|HTTP dashboard/statistics trên điểm0/10 thực, null averages, scope/auth/weak rule/materialdelete/restart|
|python -m unittest discover -s scripts -p test_*.py -v|22 tests OK|Legacy exactschema/backup/drift/orphan safeguards|
|verify_legacy_upgrade.py --configuration Release|exit0,6 migrations|Legacy fixture13 bảng/2 hàng cũ giữ nguyên|
|verify_legacy_upgrade.py --database ... --configuration Release|exit0,6 migrations|Bản sao DB đang dùng13 bảng/38 hàng cũ giữ nguyên, file gốc không đổi|
|npm test, npm run build trong FE|23/23 đạt,39 modules|Client/session/filter/date/download helpers; Vite build, không browser|
|FE tests với TZ=Asia/Bangkok|23/23 đạt|Timestamp EF không zone được hiểu UTC; explicit offsets giữ đúng|

Các project test là executable suite. Chạy `dotnet run --project BE/Tests/<suite> -c Release --no-build --no-launch-profile` sau build; không thay bằng `dotnet test` rồi suy rằng đã chạy assertions.

## Provider, migrations và artifact

- `dotnet ef migrations has-pending-model-changes` với từng provider: không pending model changes.
- SQL Server idempotent script tạo thành công tại `docs/sqlserver-migrations.sql`, có6 migrations. Chưa thực thi lên SQL Server.
- `dotnet publish BE/Services/Core/LMS.Core.API -c Release --no-build --no-restore --output artifacts/publish-upgrade --verbosity quiet`: exit0. Publish không chứa .env/SQLite/Uploads; quét mẫu key Google không tìm thấy trong publish/FE dist. Đây là artifact local, chưa deploy.
- NuGet audit13 projects, kể cả transitive: không báo package vulnerable hiện biết; npm audit:0 vulnerabilities. Logs: `artifacts/verification/final-nuget-audit.log`, `final-npm-audit.log`.
- `docker compose config --quiet`: exit0 với password tạm không in ra; `final-compose-config.log`. Compose validation không chứng minh SQL Server runtime.

## Gemini live — một fixture TXT

Đã chạy `python scripts/verify_gemini_live.py --run-live`, dùng API Release và SQLite/Uploads riêng, tài liệu mẫu419 ký tự, tối đa2 request provider. Không thay key/model, không gửi tài liệu người dùng. Cấu hình hiệu lực từ private .env là `gemini-3.5-flash-lite`; HTTP200 sau4,11 giây,3 câu Draft, mỗi câu4 lựa chọn/1 đúng, có giải thích/Material/Topic/difficulty/chunk offsets. Log Success ghi1 provider send; đọc lại DB xác nhận3 hàng và provenance.

Bằng chứng: `artifacts/verification/gemini-live-20261029T054651Z/{result.json,generated-drafts.json,live-fixture.sqlite}`. Sau sửa helper xác nhận PASS chỉ sau kiểm DB, đã kiểm độc lập evidence đã lưu, không gọi lại provider chỉ để tăng số lần thử.

Chỉ PASS phạm vi upload TXT mẫu → plan → Gemini → validation → lưu Draft. Chưa kiểm Gemini live cho tất cả5 định dạng, tài liệu dài/nhiều đoạn, retry thành công hoặc mọi model/quota. Lần503 cũ với `gemini-3.8-flash` được giữ trong GEMINI_DIAGNOSIS_2026-10-29.md; không suy rằng model đó đã hồi phục. Các suite offline tiếp tục ghi NOT RUN/liveUNVERIFIED vì bản thân chúng không gọi Google.

## Nâng cấp database local và HTTP thật

Sau gate bản sao và backup nhất quán mới, đã chạy API `--migrate-only` trên DB local. Backup: `artifacts/backups/upgrade_before_apply_20261029T055404Z`, gồm DB/Uploads/manifest. Trước2 migrations, sau6. Đối chiếu tất cả cột cũ của13 bảng/38 hàng giữ nguyên;1 file Uploads hash không đổi; integrity ok/FK0; provenance cũ null và Revision cũ0. Kết quả: `artifacts/verification/local-upgrade-result.json`, log `local-database-migration.log`.

Chạy API tại localhost5088 bằng launch profile và Vite127.0.0.1:5173: health200, ready200, Swagger JSON200, FE HTML200, src/main.jsx200. CORS origin đúng127.0.0.1:5173 và expose Content-Disposition/Retry-After. Log `final-network-smoke.json`. Hai tiến trình đã dừng sau kiểm tra. Lệnh mở nền ban đầu bị automatic review chặn (`blocked by policy`, không có chi tiết); phép kiểm vẫn hoàn tất trong phiên có quản lý.

## Chưa xác minh

| Gate | Bằng chứng/giới hạn |
|---|---|
|Browser UI3 vai trò/visual/mobile/E2E|Browser đã thử lại theo skill: No browser is available; discovery[].68 ca ở KIEM_THU_COBAN.md còn UNVERIFIED|
|SQL Server migration/query/backup/restore runtime|docker info lỗi pipe dockerDesktopLinuxEngine không tồn tại; không chạy daemon|
|Git/remote CI|Workspace không có.git; không commit/push/merge|
|VPS/domain/production acceptance|Chưa deploy; local publish/HTTP không thay thế production nghiệm thu|
|OCR|Chưa triển khai; PDF scan và slide chỉ ảnh cần OCR. Một số codec PDF chưa hỗ trợ cần xuất lại tài liệu|

Không dùng checklist UI, source review hoặc advisory audit làm bằng chứng thay cho các gate còn thiếu.
