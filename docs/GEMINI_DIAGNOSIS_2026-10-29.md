# Chẩn đoán Google HTTP503 — 29/10/2026

## Kết quả mới sau nâng cấp

Lúc 12:46 ngày29/10/2026 UTC+7, phép thử qua Backend thật với cấu hình hiệu lực `gemini-3.5-flash-lite` trả HTTP200, sinh/validate/lưu3 Draft trong4,11 giây với1 request provider. Tài liệu TXT mẫu419 ký tự và database thử riêng; không sửa model/key hay dữ liệu người dùng. Đã đối chiếu3 câu trong response với3 hàng Questions, log Success và budget. Bằng chứng: `artifacts/verification/gemini-live-20261029T054651Z/result.json`, `generated-drafts.json` và `live-fixture.sqlite`.

Kết quả này xác minh một lần sinh từ TXT mẫu qua API, không khẳng định model `gemini-3.8-flash` dưới đây đã hết quá tải, không chứng minh độ sẵn sàng lâu dài hoặc Gemini live với mọi định dạng/tài liệu dài. Code hiện có bounded retry429/503 theo yêu cầu nâng cấp; mô tả không-retry dưới đây là phép chẩn đoán lịch sử trước nâng cấp. Chạy lại có chủ đích: `python scripts/verify_gemini_live.py --run-live` (tối đa2 lượt provider, có thể dùng quota).

## Kết luận theo phản hồi trực tiếp

Google báo model `gemini-3.8-flash` đang có nhu cầu cao. Đây là nguyên nhân Google trả về trong lần kiểm tra, không chỉ là diễn giải từ thông báo chung của ứng dụng.

Một request tổng hợp rất nhỏ, dùng cùng key/model đang cấu hình, được gửi trực tiếp tới endpoint generateContent. Request chỉ yêu cầu trả lời OK, giới hạn16 output tokens; không có học liệu người dùng, systemInstruction hoặc JSON schema. Không retry, không đổi model và không sửa key/database.

Kết quả:

- HTTP503 trong1,84 giây.
- error.status: UNAVAILABLE.
- Google message: "This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later."
- Content-Type: application/json; charset=UTF-8.
- Server: scaffolding on HTTPServer2.
- Thời điểm nhận:2026-10-29 01:34:05 UTC+7 (response timer đo tại máy).

Chi tiết đã loại secret nằm trong `artifacts/verification/gemini-root-cause/configured-minimal.json`; không lưu key hoặc nội dung tài liệu. Phản hồi không có candidate thành công.

## Điều đã loại trừ bằng phép thử

Lỗi503 tái hiện với request tối giản không qua React/Controllers/DbContext, không có schema hay học liệu LMS. Vì vậy không thể quy lỗi đang quan sát chỉ cho tài liệu dài hoặc cấu trúc câu hỏi của LMS. Lần này không phải Backend chờ hết120 giây: Google trả503 sau1,84 giây.

Audit độc lập đối chiếu endpoint/header/structured-output với tài liệu Google không tìm thấy mismatch giải thích503. Không có sửa code mới trong lần chẩn đoán này.

GET models200 trước đó chỉ chứng minh đọc được metadata, không chứng minh dự án được phép inference hoặc còn quota/billing. Lần generate trực tiếp này báo UNAVAILABLE/high demand, không có bằng chứng để kết luận key sai hoặc cần nạp tiền.

## Hướng xử lý phù hợp

Tăng timeout không giải quyết phản hồi overload nhanh. Cần chờ model có năng lực phục vụ hoặc đánh giá model khác được tài khoản cho phép, có quota/chi phí phù hợp. Không tự đổi model, bật billing, retry liên tục hoặc trả câu hỏi giả. Chưa có bằng chứng Gemini sinh câu hỏi thành công.

Nguồn chính thức: [Troubleshooting](https://ai.google.dev/gemini-api/docs/troubleshooting), [GenerateContent](https://ai.google.dev/api/generate-content), [structured output](https://ai.google.dev/gemini-api/docs/structured-output).
