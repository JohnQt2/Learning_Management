# Gemini, học liệu và an toàn dữ liệu

Mã ngày 29/10/2026. Google Gemini là provider AI chính. Adapter gọi REST từ Backend; Frontend không nhận API key. `Disabled` giữ luồng soạn câu thủ công; `Mock` chỉ Development và được đánh dấu mô phỏng.

## Bằng chứng live và tên model

Lần kiểm tra ngày 29/10/2026 dùng cấu hình hiệu lực `gemini-3.5-flash-lite`: HTTP 200, 3 câu hợp lệ được lưu Draft vào SQLite cô lập, 1 lần gửi, 4,11 giây. Nguồn là bài học tổng hợp 419 ký tự về giao dịch DB, không phải học liệu người dùng. Bằng chứng: `artifacts/verification/gemini-live-20261029T054651Z/result.json`.

Lần này chứng minh gọi Google thật → parse/validate → lưu Draft. Duyệt/đưa vào Quiz, lỗi HTTP, chunking/retry và concurrency được kiểm tra bằng fixture/offline riêng; không gộp chúng thành toàn bộ E2E Gemini live. Không bảo đảm mọi model/tài liệu/tài khoản luôn thành công.

`appsettings.json` giữ model `gemini-3.8-flash`; `.env` dùng trong phép thử có model ở trên. Không tự chuyển model khi lỗi, không tự bật billing hoặc đổi key. Cấu hình `Ai:GeminiModel` chỉ chấp nhận tên gồm chữ/số/dấu chấm/gạch dưới/gạch ngang, tối đa 100 ký tự. Model không tồn tại/không có quyền được báo rõ.

## Cấu hình và request

Đặt `Ai__Mode=Gemini`, `Ai__GeminiApiKey` và model riêng qua environment, User Secrets hoặc `.env` Backend được giữ kín. Thứ tự ưu tiên và bảng giới hạn đầy đủ: [ENV_SETUP](ENV_SETUP.md).

Adapter gửi `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`. Key ở header `x-goog-api-key`, không nằm trong URL. Logging mặc định của HttpClient cho adapter đã được gỡ để tránh ghi request chứa thông tin nhạy cảm.

Prompt có `systemInstruction` riêng; `sourceText` nằm trong dữ liệu JSON của user message và được đánh dấu tài liệu không đáng tin cậy. Chỉ dẫn trong tài liệu không được coi là lệnh hệ thống. Adapter không mở URL, chạy tool hoặc thực thi nội dung học liệu. Đây là biện pháp giảm prompt injection; giảng viên vẫn phải rà soát tính đúng và phù hợp của câu hỏi.

Structured output dùng `generationConfig.responseFormat.text`, `mimeType=APPLICATION_JSON` và schema mảng câu hỏi có `content`, `explanation`, `options`. Mỗi câu có 4 phương án, mỗi phương án có `content` và `isCorrect`. Giới hạn 8192 output tokens và 512000 byte phản hồi; chỉ lấy phần text không phải thought của candidate đầu tiên, yêu cầu `finishReason=STOP`.

## Sinh câu hỏi từ 5 định dạng

TXT/Markdown UTF-8, PDF có lớp chữ, DOCX và PPTX được trích xuất ở Backend trước khi tạo kế hoạch. PDF scan/slide chỉ có ảnh không được giả lập text: trả `NeedsOcr` hoặc `NoText` và không cho sinh câu hỏi khi nội dung rỗng. OCR chưa có.

PDF dùng package PdfPig 0.1.16, license Apache-2.0, đã build với .NET 10. Adapter giải mã PDF có budget trước khi ghi dữ liệu giải nén; nội dung dùng codec chưa hỗ trợ an toàn bị từ chối. DOCX/PPTX kiểm tra cấu trúc ZIP/OOXML và giới hạn entry/giải nén; không thực thi macro/ActiveX, DTD hoặc tải tài nguyên bên ngoài. Thư viện/parser không bảo đảm thứ tự đọc hoàn hảo với mọi bố cục phức tạp.

`POST /api/ai/questions/plan` chỉ đọc học liệu, kiểm tra quyền và lập kế hoạch; không gửi tới Google. `POST /api/ai/questions/generate` lấy lại nội dung có quyền và kế hoạch hiện tại, rồi thực hiện.

Giới hạn mặc định:

| Giới hạn | Giá trị |
|---|---:|
| Câu hỏi/request công khai | 3–10 |
| Độ khó | Easy, Medium, Hard |
| Nguồn mỗi đoạn | 16000 đơn vị UTF-16 |
| Số đoạn | 3, không vượt số câu cần sinh |
| Tổng lần gửi Google | 4, gồm mọi đoạn và retry |
| Retry mỗi đoạn | 1, vẫn bị budget tổng chặn |
| Timeout xử lý một đoạn | 45 giây mặc định, local đã cấu hình 120 |
| Deadline sinh trên toàn bộ đoạn | 240 giây |

Planner giữ thứ tự và khoảng `[startCharacter,endCharacter)` trên văn bản trích xuất, chỉ số đoạn bắt đầu 1. Khoảng ký tự dùng UTF-16 như .NET/JavaScript, không cắt đôi surrogate pair. Mọi ký tự được phủ đúng một lần khi kế hoạch hợp lệ. Câu hỏi phân bổ theo độ dài đoạn, ít nhất một câu/đoạn.

Tài liệu vượt số đoạn/budget hoặc tạo đoạn chỉ có khoảng trắng bị từ chối trước request đầu tiên; không cắt mất phần cuối hoặc tạo câu từ một phần mà không báo. Tổng nguồn tối đa phụ thuộc cả giới hạn đoạn, cấu trúc Unicode và số câu. Preview có thể cắt theo cấu hình nhưng luôn báo `truncated`; nó không phải nguồn bị cắt để gửi AI.

## Retry và quota

Chỉ upstream Google HTTP 429 hoặc 503 được thử lại. Không retry 400/401/403/404/500, mạng mất kết nối, timeout, JSON sai, nội dung bị lọc hoặc lỗi budget local.

Retry dùng exponential backoff + jitter, tôn trọng `Retry-After` dạng số giây hoặc HTTP-date. Nếu hint vượt `MaxRetryDelaySeconds`, request kết thúc, không rút ngắn hint để gửi sớm. Delay và HTTP đều bị cancellation/timeout bao phủ. Response lỗi được dispose trước khi chờ.

`MaxProviderRequests` là budget atomic dùng chung toàn lần sinh. Một HTTP send chỉ được reserve sau validation local; retry và các đoạn đều tiêu budget đó. Hết budget thì dừng, không tiếp tục hoặc tự chia sang request mới. Một lần sinh có thể thất bại sau vài lượt gửi đã tính quota; không có cơ chế hoàn quota của Google.

Frontend không tự retry request sinh và khóa form lúc đang xử lý. Thử lại bằng thao tác người dùng là một lần sinh mới, có thể tạo thêm câu; chưa có idempotency key xuyên các POST độc lập. Retry transport bên trong một request không lưu câu lặp.

## Validation và lưu Draft

Validator kiểm tra JSON sâu tối đa 16, tối đa 256000 ký tự, đúng field và kiểu dữ liệu; không nhận field lạ/trùng. Mỗi câu cần:

- Nội dung/giải thích không rỗng, tối đa 4000 ký tự.
- Đúng 4 phương án, mỗi phương án 1–2000 ký tự sau trim và không trùng khi so sánh không phân biệt hoa/thường.
- Đúng một `isCorrect=true`.

Dòng sai bị loại, dòng trùng nội dung được bỏ trong lần sinh. Vì vậy số câu hợp lệ có thể ít hơn yêu cầu; API/log trả số thực tế. Không có dòng hợp lệ hoặc đoạn nào trả JSON không hợp lệ thì không lưu batch.

Câu hỏi và options chỉ được đưa vào transaction sau khi toàn bộ đoạn đã xử lý thành công. Mọi câu luôn `source=AI`, `status=Draft`; lưu Course/Topic/Material, Difficulty và chunk/range nguồn. Không tự approve/publish. Nếu học liệu bị xóa mềm trong lúc chờ provider, concurrency guard từ chối commit với 409, giữ lịch sử cũ.

`AIGenerationLogs` chỉ lưu trạng thái, lý do lỗi cố định/HTTP code đã làm sạch, số câu yêu cầu/thực tế, số đoạn, số lần gửi và thời gian. Không lưu key, JWT, raw provider body hoặc prompt/học liệu trong log này. Nội dung trích xuất và câu hỏi hợp lệ vẫn nằm trong bảng nghiệp vụ để phục vụ LMS; phân quyền/backup phải bảo vệ chúng.

Deadline cấu hình bao phủ gọi provider, chờ retry và validation trên các đoạn. Ghi database sau đó vẫn là transaction và dùng cancellation của request. Nó không phải SLA thời gian tổng bao gồm mọi độ trễ lưu trữ.

## Phân biệt lỗi

| Google/sự cố | HTTP API | Hướng xử lý |
|---|---:|---|
| 400 | 502 | Kiểm tra tham số/schema và model cấu hình; không retry tự động |
| 401/403 | 502 | Kiểm tra API key, project và quyền |
| 402 | 502 | Kiểm tra billing/credit; ứng dụng không tự bật trả phí |
| 404 | 502 | Model/endpoint không tồn tại hoặc chưa được cấp quyền |
| 429 | 429 | Quota/rate limit; retry trong giới hạn nếu có thể |
| 500 | 503 | Lỗi xử lý phía Google; không thuộc tập retry tự động hiện tại |
| 503 | 503 | Google bận/tạm gián đoạn; retry có giới hạn |
| 408/504 hoặc timeout | 504 | Giảm khối lượng hoặc thử sau; không lặp tự động |
| Network | 502 | Kiểm tra kết nối từ máy Backend đến Google |
| Outer JSON/success response hỏng hoặc quá lớn | 502 | Không lưu Draft |
| Câu hỏi sai schema/rỗng/bị lọc | 422 | Sửa học liệu hoặc giảm yêu cầu, kiểm tra lại |
| AI chưa cấu hình/hết budget local | 503 | Kiểm tra cấu hình hoặc giới hạn |
| Rate limit của API local | 429 | `Retry-After: 60` |

Numeric Google HTTP status xuất hiện trong thông báo lỗi upstream để phân biệt với lỗi local. Raw body Google không phản chiếu cho client. Hint Retry-After từ Google dùng nội bộ adapter; không cam kết được chuyển thành header HTTP của API. Log ngoài API cũng không nên thu Authorization, key hoặc request body.

HTTP 503 của lần chẩn đoán trước được Google mô tả model đang có nhu cầu cao. Timeout là một hiện tượng riêng, không đủ để suy ra quota/key sai. Lần live thành công hiện tại dùng model cấu hình khác; không có cơ chế fallback tự động nào được dùng để suy diễn rằng model cũ đã hết sự cố.

## Kiểm chứng lại

Offline: `LMS.AiAdapterTests` kiểm tra transport/retry/budget/cancellation; `LMS.AiWorkflowTests` kiểm tra parser 5 định dạng → Draft → Approve → Quiz, rollback và race; các test không gọi Google.

Live opt-in:

```powershell
dotnet build BE/Services/Core/LMS.Core.API -c Release --disable-build-servers -m:1
python scripts/verify_gemini_live.py --run-live
```

Script dùng cấu hình key/model Backend, database và Uploads riêng, bài học tổng hợp, tối đa 2 lượt gửi. Không in key/token và không chạm DB ứng dụng. Kết quả nằm dưới `artifacts/verification/gemini-live-...`. [Bằng chứng cuối cùng](VERIFICATION.md) ghi cả giới hạn môi trường và kết quả cụ thể.
