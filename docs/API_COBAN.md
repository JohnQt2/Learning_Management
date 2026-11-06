# API LMS-AI

Đối chiếu Controllers/DTO/Services ngày 29/10/2026. Base URL local: `http://localhost:5088`; Swagger Development: `/swagger`. Các ID/JSON dưới đây minh họa contract, không phải dữ liệu seed hoặc kết quả live.

## Quy ước

JSON dùng camelCase; request JSON gửi `Content-Type: application/json`. Endpoint bảo vệ cần `Authorization: Bearer <accessToken>`. JWT có thời hạn 8 giờ; logout, đổi mật khẩu và khóa/mở khóa thu hồi token cũ của tài khoản.

`Owner` là Lecturer sở hữu Course liên quan. `Member` là Student đang ghi danh, Lecturer sở hữu hoặc Admin. Quyền được kiểm tra ở Backend. Đăng ký công khai luôn tạo Student; Admin chỉ có endpoint tạo Lecturer, không có API đổi role/tạo Admin.

Response không chứa password hash, SecurityVersion, key AI/JWT, physical storage path hoặc Quiz revision. Date/time là UTC theo nghiệp vụ; dữ liệu legacy không biết ngày tạo/upload trả null. Frontend chuẩn hóa timestamp do provider trả thiếu timezone thành UTC.

Phần lớn danh sách trả mảng; Admin users trả envelope phân trang. Không tự áp dụng phân trang/query filter của Admin vào endpoint khác.

## Health và tài khoản

| Method | Endpoint | Quyền | Thành công / contract |
|---|---|---|---|
| GET | `/api/health` | Public | 200: `{status,provider,version,aiEnabled,aiMode}`; mode chỉ có giá trị trong Development |
| GET | `/api/health/ready` | Public | 200 `{status:"ready"}`; 503 body rỗng nếu DB lỗi/pending migrations |
| POST | `/api/auth/register` | Public | 201: `{username,fullName,password,email?}` → LoginResponse |
| POST | `/api/auth/login` | Public | 200: `{username,password}` → LoginResponse |
| GET | `/api/auth/me` | Login | 200: AccountProfileDto |
| PUT | `/api/auth/me` | Login | 200: `{fullName,email}` → AccountProfileDto |
| POST | `/api/auth/change-password` | Login | 204: `{currentPassword,newPassword}`, cần đăng nhập lại |
| POST | `/api/auth/logout` | Login | 204, không cần body; thu hồi token |

Username đăng ký/tạo Lecturer được trim/chuyển chữ thường, dài 3–40, thuộc `a-z0-9_.-`. Họ tên 2–100; mật khẩu 8–128; email tùy chọn, tối đa 254 ký tự và cần đúng dạng. Email rỗng thành null. PUT profile không đổi username/role/lock, bỏ email hoặc gửi null sẽ xóa email hiện tại. Seed/bootstrap có chính sách mật khẩu riêng tại [ENV_SETUP](ENV_SETUP.md).

LoginResponse:

```json
{"accessToken":"<JWT>","userId":3,"role":"Student","displayName":"Sinh viên","expiresAtUtc":"2026-10-29T15:00:00Z"}
```

AccountProfileDto:

```json
{
  "userId":3,"username":"student01","fullName":"Sinh viên","role":"Student","isLocked":false,
  "email":"student@example.edu","createdAt":"2026-10-29T07:00:00Z",
  "courses":[{"courseId":10,"code":"IT101","title":"Lập trình Web","relationship":"Enrolled","enrolledAt":"2026-10-29T08:00:00Z"}]
}
```

Lecturer có relationship Teaching và enrolledAt null; Admin có danh sách courses rỗng. Login/register dùng chung rate limit mặc định 30 request/phút/IP.

## Course, Enrollment và Topic

| Method | Endpoint | Quyền | Thành công / contract |
|---|---|---|---|
| GET | `/api/courses` | Login | 200 CourseDto[]; Student đã tham gia, Lecturer sở hữu, Admin tất cả |
| GET | `/api/courses/{id}` | Member | 200 CourseDto |
| POST | `/api/courses` | Lecturer | 201 `{code,title,description?}` → CourseDto |
| PUT | `/api/courses/{id}` | Owner | 200 `{title,description?}` → CourseDto |
| POST | `/api/courses/enroll` | Student | 200 `{joinCode}` → EnrollmentDto |
| POST | `/api/courses/{id}/enroll` | Student | 200 endpoint tương thích nhận thêm ID; vẫn cần JoinCode |
| GET | `/api/courses/{id}/students` | Owner | 200 EnrollmentStudentDto[] |
| GET | `/api/courses/{id}/topics` | Member | 200 TopicDto[] |
| POST | `/api/courses/{id}/topics` | Owner | 201 `{name}` → TopicDto |
| PUT | `/api/courses/{id}/topics/{topicId}` | Owner | 200 `{name}` → TopicDto |

CourseCode duy nhất, chuẩn hóa chữ hoa, tối đa 40; tên Course 1–150, mô tả tối đa 2000. JoinCode ngẫu nhiên 8 ký tự, so khớp không phân biệt hoa/thường. Sai mã → 404; mã rỗng → 400; ghi danh lặp → 409. CourseDto.joinCode chỉ có với owner; Student/Admin nhận null.

```json
{"id":10,"code":"IT101","title":"Lập trình Web","description":"Cơ bản","joinCode":"ABCD2345","lecturerId":2,"enrollmentCount":1}
```

EnrollmentDto: `{courseId,studentId,enrolledAt}`. TopicDto: `{id,courseId,name}`; tên Topic 1–150. Sửa Course không đổi code/JoinCode.

Roster:

```json
[{
  "studentId":3,"username":"student01","fullName":"Sinh viên","enrolledAt":"2026-10-29T08:00:00Z",
  "email":"student@example.edu","isLocked":false,"publishedQuizCount":2,"completedQuizCount":1,
  "quizCompletionPercent":50,"averageScore":0,"learningStatus":"InProgress"
}]
```

LearningStatus là NotStarted/InProgress/Completed theo Quiz Published đã nộp. Chưa có bài nộp thì averageScore null; không suy diễn tiến độ đọc tài liệu.

## Học liệu

| Method | Endpoint | Quyền | Thành công / contract |
|---|---|---|---|
| GET | `/api/courses/{courseId}/materials` | Member | 200 MaterialDto[], bỏ học liệu xóa mềm |
| POST | `/api/courses/{courseId}/materials` | Owner | 201 multipart: `file`, `topicId` → MaterialDto |
| GET | `/api/materials/upload-policy` | Login | 200 `{maxUploadBytes,extensions,maxPreviewCharacters,supportedExtensions}` |
| GET | `/api/materials/capabilities` | Login | Alias upload-policy |
| GET | `/api/materials/{id}/extracted-text` | Member | 200 MaterialTextDto |
| GET | `/api/materials/{id}/download` | Member | 200 stream file + Content-Disposition |
| POST | `/api/materials/{id}/extract` | Owner | 200 trích xuất lại tệp gốc → MaterialDto |
| DELETE | `/api/materials/{id}` | Owner | 204 xóa mềm, giữ file/reference lịch sử |

Hỗ trợ .txt/.md UTF-8, .pdf có lớp chữ, .docx và .pptx. Upload kiểm tra tên/extension, MIME phù hợp và cấu trúc thật; MIME generic hoặc thiếu vẫn phải qua parser. File vật lý dùng tên Backend tạo, không phục vụ Uploads công khai.

Giới hạn file mặc định 5.000.000 byte; request multipart là MaxUploadBytes + 65536. File rỗng/quá lớn/sai định dạng → 400; vượt trần request máy chủ có thể → 413. Giới hạn giải nén, số entry, trang PDF và ký tự trích xuất theo Storage config. PDF scan/slide chỉ có ảnh trả NeedsOcr hoặc NoText; OCR chưa có.

MaterialDto:

```json
{
  "id":30,"courseId":10,"topicId":20,"title":"lesson.docx","originalFileName":"lesson.docx","canGenerateAI":true,
  "fileFormat":"docx","contentType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "sizeBytes":12000,"uploadedById":2,"uploadedAt":"2026-10-29T08:30:00Z",
  "extractionStatus":"Extracted","extractionMessage":"Đã trích xuất văn bản.","extractedCharacterCount":2500
}
```

sizeBytes/uploadedById/uploadedAt có thể null ở học liệu cũ. fileFormat là extension không có dấu chấm. Trích xuất lại cập nhật text/metadata biết được nhưng không tự điền uploader/ngày upload legacy. canGenerateAI phản ánh có text; không xác nhận Gemini đã cấu hình hoặc model khả dụng.

MaterialTextDto: `{materialId,text,status,message,truncated,characterCount}`. Preview tối đa 30000 đơn vị UTF-16 mặc định, báo rõ phần bị cắt; characterCount là độ dài nguồn đầy đủ. Deleted material không còn list/preview/download/newAI; câu hỏi và kết quả cũ vẫn tồn tại. DELETE lặp của owner vẫn trả 204.

## Gemini

| Method | Endpoint | Quyền | Thành công / contract |
|---|---|---|---|
| GET | `/api/ai/questions/status` | Lecturer | 200 cấu hình đã làm sạch, không gọi Google |
| POST | `/api/ai/questions/plan` | Owner material | 200 GenerateRequest → AiGenerationPlanDto; không gọi Google |
| POST | `/api/ai/questions/generate` | Owner material | 200 GenerateRequest → QuestionDto[] Draft |

GenerateRequest: `{"materialId":30,"count":3,"difficulty":"Medium"}`. count 3–10; difficulty Easy/Medium/Hard; cần text trích xuất hợp lệ. Độ khó nội bộ mỗi chunk có thể phân bổ ít câu hơn minimum công khai.

Status trả enabled, mode, model, maxSourceCharacters, maxGenerationChunks, maxProviderRequests, maxRetries, timeoutSeconds, generationTimeoutSeconds, minQuestionCount, maxQuestionCount, message. enabled chỉ phản ánh cấu hình, không kiểm tra Google.

Plan minh họa:

```json
{
  "materialId":30,"totalSourceCharacters":2500,"chunkCount":1,"providerRequestLimit":4,
  "maxRetries":1,"generationTimeoutSeconds":240,
  "chunks":[{"index":1,"startCharacter":0,"endCharacter":2500,"questionCount":3}]
}
```

Ranges dùng UTF-16, start bao gồm/end không bao gồm, index từ 1. Planner phủ toàn bộ nguồn đúng thứ tự hoặc báo 400 trước gọi; không âm thầm truncate. Mặc định 16000 ký tự/đoạn, 3 đoạn, 4 lần gửi tổng cộng gồm retry. Có retry 429/503 tối đa 1 lần mỗi đoạn trong budget chung, tôn trọng Retry-After/backoff/jitter. Không retry request cố định, timeout, mạng hoặc JSON sai. Rate limit local sinh 6 lần/phút/user. [Chi tiết lỗi/cấu hình](AI_SECURITY.md).

Chỉ câu hợp lệ mới được giữ, có thể ít hơn count nếu bỏ dòng sai/trùng. Toàn bộ chunk phải thành công trước transaction lưu Draft; lỗi chunk sau không để lại câu ở chunk trước. Retry trong cùng request không lưu trùng; POST mới sau thành công là một lần sinh độc lập.

## Question bank và Quiz

| Method | Endpoint | Quyền | Thành công / contract |
|---|---|---|---|
| GET | `/api/courses/{courseId}/questions` | Owner | 200 QuestionDto[] có đáp án cho Lecturer |
| POST | `/api/questions` | Owner | 201 QuestionCreateRequest → Draft Manual |
| PUT | `/api/questions/{id}` | Owner | 200 QuestionEditRequest, chỉ Draft chưa dùng trong Quiz |
| POST | `/api/questions/{id}/approve` | Owner | 200 validate lại rồi Approved, không cần body |
| GET | `/api/courses/{courseId}/quizzes` | Member | 200 QuizDto[]; Student chỉ Published |
| POST | `/api/quizzes` | Owner | 201 `{courseId,title,description?,questionIds?}` → Draft |
| GET | `/api/quizzes/{id}/authoring` | Owner | 200 QuizAuthoringDto có câu hỏi/đáp án |
| PUT | `/api/quizzes/{id}` | Owner | 200 `{title,description?,questionIds?}`, chỉ Draft |
| POST | `/api/quizzes/{id}/publish` | Owner | 200 `{questionIds}` → Published |
| GET | `/api/quizzes/{id}` | Student đã ghi danh | 200 QuizTakeDto, chỉ Published |
| POST | `/api/quizzes/{id}/submit` | Student đã ghi danh | 200 `{answers:[{questionId,selectedOptionId}]}` → SubmitResponse |

QuestionCreateRequest:

```json
{
  "courseId":10,"topicId":20,"content":"HTTP request được tầng nào tiếp nhận?",
  "explanation":"API nhận HTTP request.","difficulty":"Easy",
  "options":[
    {"content":"API","isCorrect":true},{"content":"Database","isCorrect":false},
    {"content":"File storage","isCorrect":false},{"content":"Domain entity","isCorrect":false}
  ]
}
```

Nội dung 1–4000; giải thích Manual tùy chọn, tối đa 4000. Đúng 4 phương án khác nhau sau trim/so sánh không phân biệt hoa/thường, mỗi phương án 1–2000, đúng một isCorrect=true. Topic thuộc Course. difficulty tùy chọn: null/Easy/Medium/Hard. PUT không cần Course/Topic; bỏ/null difficulty giữ giá trị hiện có, chuỗi rỗng xóa độ khó.

QuestionDto: id, courseId, topicId, materialId?, content, explanation, status, source, options[{id,content,isCorrect}], difficulty?, sourceChunkIndex?, sourceStartCharacter?, sourceEndCharacter?. Manual không có nguồn chunk; AI giữ nguồn text trích xuất. Approved không sửa được. UI lọc theo Topic/status/source/difficulty trên danh sách thực; GET questions chưa có query filter server riêng.

QuizDto: `{id,courseId,title,status,publishedAt,description,questionCount}`. Tên 1–150, mô tả tối đa 2000, tối đa 200 câu Approved cùng Course. Draft có thể rỗng; publish cần ít nhất 1 câu, không ID trùng. POST bỏ questionIds là rỗng; PUT bỏ/null questionIds giữ lựa chọn, gửi [] xóa lựa chọn Draft. PUT bỏ/null description lưu chuỗi rỗng.

QuizAuthoringDto thêm questionIds và questions có đáp án đúng. QuizTakeDto dùng cho Student không có isCorrect/explanation:

```json
{"id":50,"title":"Quiz HTTP","questions":[{"id":40,"content":"HTTP request được tầng nào tiếp nhận?","options":[{"id":400,"content":"API"},{"id":401,"content":"Database"},{"id":402,"content":"File storage"},{"id":403,"content":"Domain entity"}]}],"description":"Cơ bản"}
```

Submit phải có đúng một câu trả lời cho mỗi câu và option đúng Question; thiếu/thừa/trùng → 400. Backend tính score = round(correctCount × 10 / totalQuestions, 2), midpoint ra xa 0; bỏ qua mọi ý định truyền score/isCorrect từ client.

```json
{"attemptId":60,"score":0,"correctCount":0,"totalQuestions":1}
```

Mỗi Student chỉ một Attempt/Quiz; nộp lặp hoặc cạnh tranh trả 409 cho request còn lại. Attempt, StudentAnswers và recommendations lưu cùng transaction. Status/concurrency và Quiz revision ngăn stale Draft edit/publish đổi nội dung đã phát hành; revision nội bộ không thuộc request/response.

## Kết quả, dashboard và thống kê

| Method | Endpoint | Quyền | Thành công |
|---|---|---|---|
| GET | `/api/attempts/{id}/result` | Student sở hữu / Lecturer owner | 200 AttemptResultDto, chỉ Submitted |
| GET | `/api/students/me/attempts` | Student | 200 AttemptSummaryDto[] Submitted, mới trước |
| GET | `/api/students/me/recommendations` | Student | 200 RecommendationDto[] |
| GET | `/api/students/me/dashboard` | Student | 200 StudentDashboardDto |
| GET | `/api/courses/{courseId}/attempts` | Owner | 200 CourseAttemptDto[] Submitted |
| GET | `/api/courses/{courseId}/statistics` | Owner | 200 CourseStatisticsDto |

AttemptResultDto có attemptId, score, correctCount, totalQuestions, wrongCount, accuracy, topicAccuracies[{topicId,topicName,accuracy,isWeak}], recommendations và answers[{questionId,questionContent,selectedOptionContent,correctOptionContent,isCorrect,explanation,topicName}]. Đáp án/lời giải chỉ lộ sau Submit trong endpoint có quyền.

AttemptSummaryDto: `{id,quizId,quizTitle,score,submittedAt}`. CourseAttemptDto dùng attemptId thay id và thêm studentId/studentName. Hai lịch sử này giữ bài nộp đã có kể cả khi enrollment về sau bị gỡ.

RecommendationDto:

```json
{"topicId":20,"topicName":"HTTP cơ bản","materialId":30,"materialTitle":"lesson.md","accuracy":50,"courseId":10,"downloadUrl":"/api/materials/30/download","source":"RuleBased","reason":"Đúng 1/2 câu (50%), dưới ngưỡng 60%; nên ôn lại chủ đề theo quy tắc kết quả Quiz."}
```

Không có học liệu phù hợp/còn hoạt động thì materialId/materialTitle/downloadUrl null, vẫn có chủ đề và lý do. Gợi ý cá nhân tổng hợp câu trả lời Submitted trong Course hiện ghi danh; chi tiết Attempt dùng tỷ lệ của Attempt đó. Tối đa 2 học liệu/Topic, tỷ lệ đúng dưới 60%, không gọi Gemini.

Dashboard:

```json
{
  "enrolledCourseCount":1,"completedQuizCount":1,"publishedQuizCount":2,"averageScore":0,"quizCompletionPercent":50,
  "courseProgress":[{"courseId":10,"code":"IT101","title":"Lập trình Web","publishedQuizCount":2,"completedQuizCount":1,"quizCompletionPercent":50,"averageScore":0}],
  "recentAttempts":[{"id":60,"quizId":50,"quizTitle":"Quiz HTTP","score":0,"submittedAt":"2026-10-29T09:00:00Z"}],
  "weakTopics":[{"courseId":10,"courseTitle":"Lập trình Web","topicId":20,"topicName":"HTTP cơ bản","accuracy":0,"correctAnswerCount":0,"totalAnswerCount":1,"reason":"Đúng 0/1 câu (0%), dưới ngưỡng 60%; nên ôn lại chủ đề theo quy tắc kết quả Quiz."}],
  "recommendations":[]
}
```

recentAttempts tối đa 10; recommendations có contract ở trên. Dashboard/current statistics chỉ lấy người đang ghi danh và bài Submitted. Hoàn thành là số Quiz Published đã nộp / số Quiz Published, không là đọc tài liệu. Không có dữ liệu thì count=0, averageScore=null, danh sách=[]; score thật bằng 0 vẫn là số 0.

CourseStatisticsDto:

```json
{
  "courseId":10,"studentCount":2,"publishedQuizCount":1,"submittedAttemptCount":1,"averageScore":0,
  "quizStatistics":[{"quizId":50,"title":"Quiz HTTP","publishedAt":"2026-10-29T08:45:00Z","submittedStudentCount":1,"averageScore":0}],
  "studentStatistics":[
    {"studentId":3,"fullName":"Sinh viên A","completedQuizCount":1,"publishedQuizCount":1,"quizCompletionPercent":100,"averageScore":0},
    {"studentId":4,"fullName":"Sinh viên B","completedQuizCount":0,"publishedQuizCount":1,"quizCompletionPercent":0,"averageScore":null}
  ],
  "weakTopics":[{"topicId":20,"topicName":"HTTP cơ bản","accuracy":0,"incorrectAnswerCount":1,"totalAnswerCount":1,"affectedStudentCount":1,"reason":"Đúng 0/1 câu (0%), dưới ngưỡng 60%; nên ôn lại chủ đề theo quy tắc kết quả Quiz."}]
}
```

quizStatistics liệt kê Published, đếm Student đã nộp khác nhau; averageScore là trung bình bài nộp, làm tròn 2 số. Course weakTopics dùng tỷ lệ tổng dưới 60%; affectedStudentCount đếm người có ít nhất một câu sai trong chủ đề đó. Statistics không chứa option/answer key.

## Admin users

| Method | Endpoint | Thành công |
|---|---|---|
| GET | `/api/admin/users?page=1&pageSize=20&search=student&role=Student&isLocked=false` | 200 `{total,items,page,pageSize}` |
| GET | `/api/admin/users/{id}` | 200 UserDto |
| POST | `/api/admin/users/lecturers` | 201 `{username,fullName,password,email?}` → UserDto |
| PATCH | `/api/admin/users/{id}/lock` | 200 `{isLocked:true}` → UserDto |

Các endpoint trên chỉ Admin. search tìm username/fullName/email, tối đa 150 ký tự; role Admin/Lecturer/Student; isLocked bool; lọc trước phân trang, pageSize tối đa 100. UserDto có id, username, fullName, role, isLocked, email?, createdAt?. Admin không tự khóa tài khoản mình. Tạo Lecturer trả UserDto, không trả token.

## HTTP errors

| Status | Trường hợp |
|---|---|
| 400 | Input/state không hợp lệ, file sai định dạng, tài liệu vượt kế hoạch, option/Topic sai phạm vi |
| 401 | Thiếu/sai/hết hạn JWT, tài khoản khóa hoặc security version/role không còn khớp |
| 403 | Sai role/owner/member, xem kết quả người khác |
| 404 | Resource/file không tồn tại, JoinCode sai, Quiz Draft không khả dụng, học liệu xóa mềm |
| 409 | Dữ liệu duy nhất trùng, state/concurrency conflict, nộp lại Attempt |
| 413 | Request vượt trần máy chủ |
| 415 | Content-Type không được endpoint hỗ trợ |
| 422 | AI JSON/câu hỏi sai hoặc không có câu hợp lệ |
| 429 | API local rate limit hoặc Google quota/rate limit |
| 500 | Lỗi bất ngờ đã làm sạch, có traceId |
| 502 | Gemini request/auth/model/network/response lỗi |
| 503 | AI chưa cấu hình, hết budget, Google 500/503 hoặc storage/readiness không khả dụng |
| 504 | Gemini/deadline timeout |

Business/auth errors: `{"error":"Sinh viên đã nộp Quiz này"}`. Model validation thêm `errors`, không phản chiếu giá trị input. Rate limit local có `Retry-After: 60`; Google hint dùng nội bộ adapter, không cam kết header tương ứng ra API. Response 204 và readiness 503 không có JSON body.

Không ghi token/password/key khi thử API. [VERIFICATION](VERIFICATION.md) phân biệt kiểm thử HTTP/offline, Gemini live và môi trường chưa được xác minh.
