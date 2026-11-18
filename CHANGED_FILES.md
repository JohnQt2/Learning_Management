# File tạo/sửa — Nâng cấp LMS-AI ngày29/10/2026

Workspace không có .git. Đây là manifest theo công việc đã thực hiện, không phải git diff/commit history. Giữ N-Tier và dữ liệu đang có; lịch sử pha trước ở docs/archive.

## Backend

- `BE/LMS.sln`: thêm5 executable test projects Profile/Material/AiAdapter/AiWorkflow/Progress;13 projects toàn solution.
- `BE/Services/Core/LMS.Core.Domain/Entities/LmsEntities.cs`: Email/CreatedAt; material metadata/soft-delete; Question difficulty/chunk range; generation metrics; Quiz.Revision.
- `BE/Services/Core/LMS.Core.Infrastructure/Persistence/LmsDbContext.cs`: lengths/defaults/FK/concurrency của các trường mới.
- `BE/Services/Core/LMS.Core.Infrastructure/Persistence/Migrations/{Sqlite,SqlServer}`: UserProfileMetadata, MaterialExtractionMetadata, AiGenerationProvenance, QuizRevision và snapshots riêng; tổng6/provider.
- `BE/Services/Core/LMS.Core.Infrastructure/LMS.Core.Infrastructure.csproj`: PdfPig0.1.16 Apache2.0.
- Infrastructure `Adapters/{LocalFileStorage,MaterialTextExtractor,MaterialStorageOptions,BoundedPdfFilterProvider}.cs`:5format extraction, bounded archives/PDF decoded bytes, file validation/metadata/storage.
- Infrastructure `Adapters/{GeminiAiClient,MockAiClient,AiRequestBudget}.cs`: retry/budget/cancel/Retry-After và compatible interface overload.
- ApplicationServices `AuthenticationModule/{Abstracts/IAuthService,Dtos/AuthDtos,Implements/AuthService}.cs`, `AdminModule/Implements/AdminService.cs`, `CourseModule/{Dtos/CourseDtos,Implements/CourseService}.cs`: profile/update/email/filters/roster.
- ApplicationServices `MaterialModule/{Dtos/MaterialDtos,Implements/MaterialService}.cs`: metadata/list/preview/policy/extract/delete/download.
- ApplicationServices `AIModule/{Dtos/AiDtos,Implements/AiGenerationPlanner,Implements/AiQuestionService,Implements/QuestionJsonValidator}.cs`: metadata-only plan, full coverage/allocation, allformats Draft atomic/provenance/metrics.
- ApplicationServices `QuestionModule/{Dtos/QuestionDtos,Implements/QuestionService}.cs`: optional difficulty, validation và DTO provenance.
- ApplicationServices `QuizModule/Implements/QuizService.cs`: Revision chống stale selection/publish/edit; recommendations query không materialize fullsource.
- ApplicationServices `ResultModule/{Dtos/ResultDtos,Implements/ResultService}.cs`: dashboard/statistics, RuleBased reasons và Submitted/enrollment scope.
- API `Program.cs`, `appsettings.json`: configurable storage/request limits, CORS expose, AI limits.
- API Controllers `{AuthController,AdminController,MaterialsController,AiQuestionsController,ResultsController}.cs`: contracts mới và quyền; Course roster qua service hiện có.
- Tests mới `BE/Tests/LMS.{ProfileTests,MaterialTests,AiAdapterTests,AiWorkflowTests,ProgressTests}`.
- Tests cũ `LMS.IntegrationTests/Program.cs`: migration count theo source; `LMS.SecurityTests/Program.cs`: validPDF fixture và retry0 trong fixtureconfig; `LMS.HostingTests/Program.cs`: CORS exposedheaders assertion.

## Frontend

- `FE/src/pages/Dashboard.jsx` mới; cập nhật `Account.jsx`, `Admin.jsx`, `Courses.jsx`, `Materials.jsx`, `Questions.jsx`, `Results.jsx`.
- `FE/src/{main.jsx,shared.jsx,api.js,utils.js,styles.css}`: routes/role navigation, profiles/material/plan/statistics UI, loading/error/empty, filters, UTC, authenticated originalfilename download.
- `FE/tests/client.test.js`:23 tests hiện tại; `FE/README.md`.

## Công cụ và tài liệu

- `.env.example`: storage/AI defaults và model mẫu đã kiểm live; không chứa key. Private.env/model/key người dùng không bị đổi.
- `scripts/verify_legacy_upgrade.py`: mọi versioned migration, backup khi history đã có, bảo toàn dữ liệu cũ/default checks.
- `scripts/verify_all.ps1` mới:9.NET suites/22Python/FE/copy-upgrade; không tự gọi Gemini hay migrate DB thật.
- `scripts/verify_gemini_live.py` mới: opt-in tối đa2provider sends, isolatedDB, syntheticTXT, kiểm response+persisted Draft/log trước PASS.
- README/DEPLOYMENT/PROJECT_TASKS/PROJECT_AUDIT/CHANGED_FILES và docs API/DATABASE/ENV_SETUP/AI_SECURITY/VERIFICATION/KIEN_TRUC/KIEM_THU_COBAN/TRANG_THAI_VA_GIOI_HAN/GEMINI_DIAGNOSIS/sqlserver-migrations.sql cập nhật.
- `docs/UPGRADE_AUDIT_2026-10-29.md`, `docs/archive/*before_upgrade*`: audit lúc đầu và lịch sử giữ nguyên.

## Dữ liệu và artifact riêng

Đã nâng cấp database local sau backup/copy validation;13 bảng/38 hàng cũ và1 file Uploads giữ nguyên. Bằng chứng/log/backup/publish ở artifacts (bỏ qua Git). Không gửi .env, database, Uploads, backup hoặc log chứa dữ liệu riêng trong source ZIP. Không tạo ZIP mới trong yêu cầu nâng cấp này.

Kết quả và giới hạn: docs/VERIFICATION.md. Không claim browser UI/SQL Server runtime/remote deployment đã PASS.
