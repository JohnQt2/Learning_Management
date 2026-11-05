# Database, migrations và bảo toàn dữ liệu

Mã ngày 29/10/2026 dùng 13 bảng nghiệp vụ, không tạo thêm mô hình quản lý khoa/ngành/lớp hành chính. SQLite dùng local; SQL Server là provider riêng. Đổi `Database:Provider` không chuyển dữ liệu giữa hai DB.

## Bảng và quan hệ

| Bảng | Vai trò/cột đáng chú ý |
|---|---|
| Users | Username duy nhất; PasswordHash, Role, IsLocked, SecurityVersion; Email?, CreatedAt? |
| Courses | Code/JoinCode duy nhất; LecturerId → Users; Title/Description |
| Enrollments | CourseId → Courses, StudentId → Users, EnrolledAt; UNIQUE(CourseId,StudentId) |
| Topics | CourseId → Courses, Name |
| LearningMaterials | Course/Topic, Title/OriginalFileName, StoragePath nội bộ, ExtractedText, FileFormat/ContentType, SizeBytes?, UploadedById?, UploadedAt?, ExtractionStatus/Message, IsDeleted |
| Questions | Course/Topic/Material?, Content/Explanation, Source/Status, Difficulty?, SourceChunkIndex?/SourceStartCharacter?/SourceEndCharacter? |
| AnswerOptions | QuestionId → Questions, Content, IsCorrect |
| Quizzes | CourseId → Courses, Title/Description, Status/PublishedAt, Revision |
| QuizQuestions | PK(QuizId,QuestionId), liên kết câu được chọn |
| QuizAttempts | Quiz/Student, Status, Score decimal(5,2), SubmittedAt; UNIQUE(QuizId,StudentId) |
| StudentAnswers | Attempt/Question/SelectedOption, IsCorrect do Backend ghi |
| MaterialRecommendations | Attempt/Material/Topic, gợi ý tại thời điểm Submit |
| AIGenerationLogs | Material, Status/ErrorMessage/CreatedAt; RequestedQuestionCount?, GeneratedQuestionCount?, ChunkCount?, ProviderRequestCount?, DurationMilliseconds? |

FK dùng Restrict để giữ lịch sử và tránh multiple cascade paths của SQL Server. Validation Service kiểm tra Course/Topic/Question/option cùng phạm vi; không thêm hàng loạt composite FK làm hỏng legacy. Index/FK do migrations thể hiện chính xác.

Không có endpoint xóa Course, Topic, User hoặc bài nộp. DELETE học liệu là xóa mềm: IsDeleted=true, giữ bản ghi và tệp vật lý. List/preview/download/newAI/recommendations không sử dụng học liệu đã xóa; câu hỏi, Quiz và kết quả trước đó còn nguyên.

## Metadata và dữ liệu legacy

Email/CreatedAt của User cũ nullable. Ngày tạo mới được ứng dụng ghi bằng UTC, không dùng ngày migrate để giả ngày tạo tài khoản cũ.

Học liệu cũ có thể chưa biết size/uploader/upload time. Migration giữ null; FileFormat/ContentType mặc định rỗng, ExtractionStatus=Legacy. Học liệu cũ đã có ExtractedText hợp lệ được hiển thị trạng thái trích xuất tương ứng; owner có thể trích xuất lại từ tệp gốc để bổ sung metadata biết được. Re-extract không gán ngày/uploader giả và không ghi đè tệp gốc.

Question/AI log cũ giữ nguồn/dữ liệu đã có; field provenance/độ khó/count mới có thể null. SourceChunkIndex bắt đầu 1; range ký tự bắt đầu 0, cuối không bao gồm, đơn vị UTF-16 trên văn bản trích xuất lúc sinh. Không có bảng snapshot toàn bộ nguồn riêng cho mỗi generation; nguồn truy theo Material và tệp gốc được giữ.

## Sáu migration cho mỗi provider

`SqliteLmsDbContext` và `SqlServerLmsDbContext` cùng model `LmsDbContext` nhưng có snapshot/migration riêng trong `Infrastructure/Persistence/Migrations/Sqlite` và `SqlServer`.

| Thứ tự | Tên | Thay đổi |
|---|---|---|
| 1 | InitialBaseline | 13 bảng theo schema EnsureCreated legacy |
| 2 | AccountSecurityAndQuizDescription | Users.SecurityVersion default 0; Quizzes.Description default rỗng |
| 3 | UserProfileMetadata | Users.Email, CreatedAt nullable |
| 4 | MaterialExtractionMetadata | Metadata/status của học liệu; IsDeleted default false, uploader FK nullable |
| 5 | AiGenerationProvenance | Question difficulty/chunk/range và số liệu vận hành generation log nullable |
| 6 | QuizRevision | Quizzes.Revision default 0, concurrency guard cho thay đổi Draft/publish |

Các migration mới bổ sung cột/FK/index theo provider, không chủ động xóa hàng nghiệp vụ. SQLite có thể phải rebuild bảng để áp dụng FK/cột; vì vậy không suy diễn an toàn chỉ từ tên ADD COLUMN: luôn kiểm tra bản sao và dữ liệu sau migrate.

Migration ID từng provider có timestamp khác nhau. Trạng thái hiện tại của DB local và backup thực tế xem [VERIFICATION](VERIFICATION.md); có source migration không đồng nghĩa nó đã được áp dụng vào mọi database.

DB SQLite local đã áp dụng từ 2 lên 6 migrations sau full gate và backup mới `artifacts/backups/upgrade_before_apply_20261029T055404Z`. Đối chiếu 13 bảng/38 hàng cũ giữ nguyên từng giá trị; 1 tệp Uploads giữ hash; integrity đúng, không có FK lỗi; metadata legacy nullable và Revision=0 đúng dự kiến. Bằng chứng: `artifacts/verification/local-upgrade-result.json`. Đây là trạng thái DB local được kiểm tra, không thay thế quy trình upgrade cho DB người nhận khác.

## Tính nhất quán và thống kê

- Unique index bảo vệ enrollment và submission, kể cả hai request đồng thời; request thua trả 409.
- Submit lưu Attempt, StudentAnswers và MaterialRecommendations trong một transaction; không nhận score/isCorrect từ client.
- Question.Status là concurrency token; Draft edit chạm parent để chống sửa sau approve.
- Quiz.Status và Revision là concurrency token; Update/Publish tăng Revision để chặn stale request làm mất lựa chọn hoặc phát hành sai bộ câu hỏi.
- Material.IsDeleted là concurrency token. Sinh AI chỉ commit sau toàn bộ đoạn; nếu học liệu bị xóa trong lúc gọi provider thì transaction bị từ chối.
- User.SecurityVersion là concurrency token và được đối chiếu với JWT mỗi request. Logout/đổi mật khẩu/lock thay đổi version.
- Approved Question/Published Quiz không sửa nội dung qua API, giữ đáp án và kết quả lịch sử.
- Dashboard/statistics chỉ tính bài Submitted và người hiện ghi danh. Tiến độ dùng Quiz Published, điểm thiếu trả null, zero thật vẫn là 0.
- Topic yếu theo tỷ lệ đúng dưới 60%; gợi ý tối đa 2 học liệu hoạt động/Topic, ghi rõ RuleBased/reason. Lịch sử MyAttempts/CourseAttempts vẫn giữ kết quả cũ sau khi enrollment bị gỡ.

## Nâng cấp database có history

DatabaseBootstrap kiểm tra DB có bảng nhưng chưa có migration history và từ chối trước migration. DB đã có history hoặc DB mới dùng EF migrations bình thường.

Bản chụp trước nâng cấp ngày 29/10 có 13 bảng nghiệp vụ và 38 hàng đã tồn tại. Thử nâng cấp trên bản sao đã áp dụng 6 migration, giữ từng giá trị cột cũ/số hàng, integrity và FK đúng; file nguồn không bị thay đổi bởi công cụ copy-only. Kết quả cuối cùng được tập hợp tại [VERIFICATION](VERIFICATION.md), không dùng số hàng này để mô tả mọi lần chạy sau khi người dùng tiếp tục nhập dữ liệu.

Từ gốc dự án, sau khi build Release:

```powershell
python scripts/verify_legacy_upgrade.py --database BE/Services/Core/LMS.Core.API/lms-ai.sqlite --configuration Release
```

Công cụ dùng bản sao tạm, gọi API `--migrate-only` và đối chiếu dữ liệu cũ. Không truyền --database sẽ dựng fixture legacy. Thêm test công cụ bằng:

```powershell
python -m unittest discover -s scripts -p "test_*.py" -v
```

Trước áp dụng thật: dừng writer, backup SQLite nhất quán cùng Uploads, thử copy upgrade, rồi chạy migration rõ ràng bằng connection runtime. [Lệnh đầy đủ](../DEPLOYMENT.md).

## Nhận schema EnsureCreated cũ

`scripts/baseline_sqlite.py` mặc định mở chỉ đọc, so sánh chính xác DDL/table/index/collation/hidden columns/FK, integrity và dữ liệu orphan. Chỉ với baseline legacy đúng mới chấp nhận `--apply`: khóa ghi, tạo SQLite backup rồi ghi migration history cho schema đã tồn tại. Không chạy lại CREATE TABLE.

Schema tùy chỉnh/view/trigger/history lạ bị từ chối; không ép nhận baseline hoặc xóa DB. SQL Server legacy cần quy trình baseline manual được DBA review. [Biên bản xử lý startup cũ](STARTUP_DATABASE_FIX.md) là lịch sử trước bản nâng cấp này.

## SQL Server và rollback

`docs/sqlserver-migrations.sql` là script idempotent từ bộ migrations SQL Server. Provider snapshots/build/script đã được kiểm tra; SQL Server runtime chưa được chạy vì Docker daemon chưa hoạt động trong môi trường hiện tại. Cần thử create/migrate/upgrade/query/restore trên SQL Server thật trước nghiệm thu provider này.

Design factory chỉ phục vụ scaffold/script; runtime dùng connection môi trường qua API. Không sử dụng factory connection để ngầm update DB đích. Ngoài Development, AutoMigrate không tự chạy; migration được áp dụng chủ động trước start.

Down xóa cột mới và có thể mất dữ liệu. Rollback DB có dữ liệu cần dùng backup DB/Uploads khớp binary, không sửa migration history tùy tiện.
