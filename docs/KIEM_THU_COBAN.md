# Kiểm thử và nghiệm thu giao diện LMS-AI

Cập nhật ngày 29/10/2026. **Tất cả dòng kiểm thử trình duyệt trong tài liệu này vẫn `[!] UNVERIFIED`.** Browser runtime trả `No browser available` và danh sách browser rỗng sau khi kiểm tra kết nối. Chưa thực hiện thao tác UI, visual QA, responsive thực tế hoặc E2E UI. Checklist là hướng dẫn nghiệm thu cần chạy, không phải nhật ký PASS.

## Bằng chứng tự động và live được ghi riêng

Frontend đã chạy `npm test` **23/23 đạt**, gồm một lượt với `TZ=Asia/Bangkok`, và `npm run build` **đạt, 39 modules**. Test Node kiểm tra HTTP client, token/phiên, validation, payload Quiz, năm loại upload, hủy request, lỗi/Retry-After, bộ lọc câu hỏi, điểm trung bình trống/0, UTC và tên tải về. Không có trình duyệt tham gia các test này.

Backend đã có gate Release và chín suite .NET, kiểm tra migration trên bản sao và các helper database. Phạm vi/kết quả cụ thể xem [`PROJECT_AUDIT.md`](../PROJECT_AUDIT.md), [`PROJECT_TASKS.md`](../PROJECT_TASKS.md) và log trong `artifacts/verification`. Các suite offline dùng fake HTTP/provider để kiểm tra lỗi Gemini, JSON, retry/budget và luồng năm định dạng; chúng không chứng minh live Google đã chạy cho cả năm định dạng.

Gemini live đã thành công ở **một fixture TXT tổng hợp qua Backend**: HTTP 200, model hiệu lực `gemini-3.5-flash-lite`, 3 Draft hợp lệ, khoảng 4,11 giây, một lượt gửi provider. Bằng chứng: [`result.json`](../artifacts/verification/gemini-live-20261029T054651Z/result.json), [`generated-drafts.json`](../artifacts/verification/gemini-live-20261029T054651Z/generated-drafts.json). Đây không phải nghiệm thu UI-G02 trên trình duyệt hoặc bằng chứng tài liệu thực của đơn vị/live cả năm định dạng. Không tự đổi model hoặc gọi thêm live chỉ để giả lập lỗi quota.

SQL Server runtime chưa được xác minh vì Docker daemon chưa kết nối được. SQLite local, script SQL Server và kiểm thử HTTP không thay thế bằng chứng SQL Server đang chạy.

Chạy lại gate tự động từ thư mục gốc:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify_all.ps1
```

Script restore/build Release, chạy chín suite .NET, helper/migration tests và frontend tests/build theo thứ tự; log được lưu vào `artifacts/verification`. Script không gọi live Gemini, chạy browser hoặc triển khai. Có thể truyền `-DatabasePath` với đường dẫn tuyệt đối để kiểm tra nâng cấp **trên bản sao** database hiện có; không dùng lệnh reset/xóa database để nghiệm thu.

Chạy riêng frontend:

```powershell
cd FE
npm ci
npm test
npm run build
```

## Chuẩn bị nghiệm thu thủ công

1. Dùng database và thư mục upload kiểm thử riêng. Backup dữ liệu trước khi thử migration; giữ nguyên database/học liệu đang sử dụng. Cách cấu hình xem [`DEPLOYMENT.md`](../DEPLOYMENT.md).
2. Chạy Backend và `npm run dev` theo README. Dùng đúng origin `http://127.0.0.1:5173` hoặc `http://localhost:5173` và cấu hình CORS tương ứng. Nếu dùng preview cổng 4173, thêm đúng origin đó; không bỏ giới hạn CORS để kiểm thử.
3. Chuẩn bị Admin theo cơ chế bootstrap, dùng Admin tạo Lecturer A/B và đăng ký Student A/B. Không dựa vào mật khẩu hardcode hoặc coi demo seed mặc định bật.
4. Chuẩn bị TXT UTF-8 có tiếng Việt, Markdown, PDF có text layer, PDF scan hợp lệ, DOCX có đoạn/bảng, PPTX có chữ/bảng và slide chỉ có ảnh; thêm file rỗng, sai định dạng, giả phần mở rộng, PDF lỗi/mã hóa và tệp quá giới hạn cấu hình. Giới hạn upload lấy từ API policy, mặc định 5.000.000 byte.
5. Chỉ gửi nội dung được phép chia sẻ đến Google. Dùng fixture kiểm thử cho các lỗi 400/403/404/429/503, timeout và JSON sai; không cố tiêu quota/chi phí để tạo lỗi live. Cấu hình retry/budget phải được ghi lại.
6. Dùng cửa sổ/hồ sơ trình duyệt riêng cho ba vai trò. Ghi người thực hiện, thời gian, phiên bản code, database/provider, request/status, kết quả mong đợi/thực tế và ảnh đã che thông tin nhạy cảm. Chỉ đổi `[!]` thành `[x]` khi có bằng chứng trực tiếp.

## Public, phiên đăng nhập và hồ sơ

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-A01 / TEST 01 | Đăng ký Student hợp lệ với hoặc không có email; vào Tổng quan đúng vai trò, hồ sơ hiển thị đúng họ tên/username/email. | [!] UNVERIFIED |
| UI-A02 | Thử username trùng/sai ký tự, họ tên trống, email sai, mật khẩu ngắn hoặc xác nhận khác; báo lỗi cụ thể, không tạo tài khoản lỗi/trùng. | [!] UNVERIFIED |
| UI-A03 | Đăng nhập Student/Lecturer/Admin; chỉ hiện điều hướng và chức năng của vai trò tương ứng. | [!] UNVERIFIED |
| UI-A04 | Sai mật khẩu hoặc tài khoản khóa; không vào dashboard, thông báo không lộ dữ liệu nhạy cảm. | [!] UNVERIFIED |
| UI-A05 | Xem hồ sơ cả ba vai trò: họ tên, username, email, vai trò, ngày tạo, trạng thái và các khóa học khớp dữ liệu; không lộ password/hash/token. Tài khoản cũ không có ngày tạo ghi rõ chưa có dữ liệu. | [!] UNVERIFIED |
| UI-A06 | Đổi mật khẩu với mật khẩu cũ sai, xác nhận khác hoặc giá trị không hợp lệ; báo lỗi và không báo thành công. | [!] UNVERIFIED |
| UI-A07 | Đổi mật khẩu đúng; về đăng nhập, mật khẩu mới dùng được và mật khẩu cũ bị từ chối. JWT cũ ở cửa sổ khác bị thu hồi tại request kế tiếp. | [!] UNVERIFIED |
| UI-A08 | Đăng xuất rồi refresh vẫn ở đăng nhập. Khi API mất kết nối, thông báo rõ chỉ kết thúc phiên thiết bị và chưa xác nhận thu hồi phía server. | [!] UNVERIFIED |
| UI-A09 | Refresh phiên hợp lệ kiểm tra lại `/api/auth/me`; token hết hạn/bị sửa/bị thu hồi đưa về đăng nhập, không trắng trang. | [!] UNVERIFIED |
| UI-A10 | Sửa họ tên/email, lưu và mở lại; dữ liệu giữ đúng. Gửi thêm role/isLocked bằng DevTools không thay đổi quyền/trạng thái tài khoản. | [!] UNVERIFIED |

## Admin

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-D01 | Mở danh sách, chuyển trang và xem chi tiết; tổng số, trạng thái và ngày tạo/email khớp dữ liệu thật. | [!] UNVERIFIED |
| UI-D02 | Tạo Lecturer hợp lệ; tài khoản xuất hiện và đăng nhập được. Username trùng/email sai bị từ chối; không có lựa chọn tạo Admin/đổi role. | [!] UNVERIFIED |
| UI-D03 / TEST 14 | Khóa Student/Lecturer đang ở cửa sổ khác; danh sách cập nhật Đã khóa, phiên cũ không tiếp tục thao tác được. | [!] UNVERIFIED |
| UI-D04 | Mở khóa cho phép đăng nhập mới; JWT đã thu hồi không tự hợp lệ lại. Nút khóa chính Admin đang dùng bị vô hiệu hóa. | [!] UNVERIFIED |
| UI-D05 | Tìm theo tên/username/email, kết hợp vai trò/trạng thái, áp dụng và xóa bộ lọc; tổng/trang phản ánh tập dữ liệu đã lọc, empty state rõ. | [!] UNVERIFIED |

## Lecturer: khóa học, học viên và học liệu

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-L01 / TEST 02 | Tạo Course có mã/tên/mô tả; sidebar và chi tiết hiển thị dữ liệu đã lưu, JoinCode thật; mã trùng/input trắng/quá dài báo lỗi. | [!] UNVERIFIED |
| UI-L02 | Sửa tên/mô tả rồi mở lại; dữ liệu đúng, form tạo mới không bị lẫn dữ liệu đang sửa. | [!] UNVERIFIED |
| UI-L03 | Sao chép JoinCode báo thành công/lỗi clipboard rõ; số sinh viên và danh sách ghi danh khớp DB sau tải lại, gồm email/ngày tham gia/trạng thái học tập. | [!] UNVERIFIED |
| UI-L04 | Tạo/sửa Topic; tên và quan hệ học liệu giữ đúng, input trống/quá dài bị từ chối. | [!] UNVERIFIED |
| UI-L05 / TEST 03 | Upload lần lượt TXT, MD, PDF có text, DOCX và PPTX vào Topic; kiểm tra tên/định dạng/dung lượng/người upload/thời gian/trạng thái trích xuất; xem text và tải gốc đúng nội dung/tên/phần mở rộng. | [!] UNVERIFIED |
| UI-L06 | Upload rỗng/quá policy, giả PDF/DOCX/PPTX, MIME/định dạng không hợp lệ; báo lỗi, không tạo metadata thành công sai hoặc làm mất học liệu cũ. | [!] UNVERIFIED |
| UI-L07 | Chuyển nhanh hai Course khi mạng chậm; kết quả request cũ không ghi đè Topic/Material/Question/Quiz của Course mới. | [!] UNVERIFIED |
| UI-L08 | PDF scan báo Cần OCR; slide chỉ có ảnh báo không có text. Không hiển thị text giả hoặc nút sinh AI cho nội dung rỗng; file gốc vẫn tải được. OCR chưa triển khai. | [!] UNVERIFIED |
| UI-L09 | Preview giữ tiếng Việt, đoạn/bảng DOCX và thứ tự slide PPTX; PDF gốc xem được hoặc có hướng dẫn tải. Preview bị giới hạn phải báo rõ chỉ xem một phần. | [!] UNVERIFIED |
| UI-L10 | Học liệu cũ chưa đủ metadata hiển thị chưa có dữ liệu; yêu cầu trích xuất lại cập nhật text/trạng thái, không bịa người/ngày upload cũ. | [!] UNVERIFIED |
| UI-L11 | Xóa học liệu cần xác nhận; tài liệu bị ẩn và không tải/sinh AI được, nhưng Question/Quiz/Attempt liên quan còn nguyên. Hủy xác nhận không xóa. | [!] UNVERIFIED |

## Lecturer: ngân hàng câu hỏi, AI và Quiz

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-Q01 / TEST 05 | Soạn ít nhất 5 câu thủ công: đúng Topic, 4 phương án/1 đáp án đúng, lời giải và độ khó tùy chọn; lưu Draft, Source Manual, nội dung đúng. | [!] UNVERIFIED |
| UI-Q02 | Sửa Draft gồm nội dung/phương án/đáp án/lời giải/độ khó; lưu rồi mở lại đúng. Hủy sửa không ghi thay đổi; có thể để độ khó Chưa xác định. | [!] UNVERIFIED |
| UI-Q03 | Câu trắng, phương án thiếu/trùng hoặc độ khó sai bị từ chối; không lưu nháp sai cấu trúc. | [!] UNVERIFIED |
| UI-Q04 | Duyệt Draft hợp lệ thành Approved và xuất hiện khi chọn Quiz. Câu Approved không có thao tác sửa Draft làm đổi lịch sử bài nộp. | [!] UNVERIFIED |
| UI-Q05 | Kết hợp tìm nội dung, Topic, Draft/Approved, Manual/AI và độ khó; số lượng đúng. Chưa xác định lọc được dữ liệu cũ mà không gán độ khó giả. Xóa bộ lọc trả đầy đủ câu trong Course. | [!] UNVERIFIED |
| UI-G01 | Disabled/thiếu key hiển thị chưa cấu hình và giữ luồng thủ công; Mock không được gọi là kết quả Gemini thật. | [!] UNVERIFIED |
| UI-G02 / TEST 15 | Với cấu hình live được phép: mỗi loại học liệu có text → lập kế hoạch → chủ động bấm sinh → nhận/lưu Draft → sửa/duyệt → đưa Quiz. Đối chiếu số Draft/nguồn DB. Một live TXT fixture Backend đã đạt riêng; chưa nghiệm thu UI hoặc live đủ năm định dạng. | [!] UNVERIFIED |
| UI-G03 | Qua môi trường fixture, kiểm tra 400/401/403/404/429/500/503, timeout/mất mạng và JSON sai/đáp án lỗi; thông báo đúng, loading kết thúc, không lưu câu hỏi lỗi hoặc báo thành công giả. | [!] UNVERIFIED |
| UI-G04 | Kế hoạch hiển thị toàn bộ số ký tự, các đoạn/phạm vi có thứ tự, phân bổ số câu, budget/retry/deadline. Network lập kế hoạch không gọi Gemini. Tài liệu quá giới hạn bị từ chối trước provider, không âm thầm bỏ phần cuối. | [!] UNVERIFIED |
| UI-G05 | Qua fixture 429/503, Backend retry có giới hạn và tôn trọng Retry-After trong budget; frontend không tự lặp generation. Bấm nhanh không gửi trùng; lỗi đoạn sau không lưu thành công phần trước. | [!] UNVERIFIED |
| UI-G06 | Draft AI ghi đúng Material/Topic/độ khó/đoạn/phạm vi ký tự và luôn cần duyệt. Xóa học liệu trong lúc sinh gây xung đột 409 rõ ràng, không lưu Draft từ tài liệu đã xóa. | [!] UNVERIFIED |
| UI-Z01 | Tạo Quiz Draft, chọn Approved, lưu rồi mở lại; tên/mô tả/số câu/danh sách khớp dữ liệu đã lưu. | [!] UNVERIFIED |
| UI-Z02 / TEST 05 | Phát hành Quiz hợp lệ thành Published. Quiz rỗng/câu Draft/câu Course khác bị từ chối. | [!] UNVERIFIED |
| UI-Z03 | Quiz Published không có form sửa Draft; lịch sử điểm/câu trả lời không đổi sau thao tác khác trên ngân hàng. | [!] UNVERIFIED |
| UI-Z04 | Một cửa sổ publish trong khi cửa sổ khác còn giữ Draft cũ để sửa: request sửa sau publish nhận 409 rõ ràng, có thể tải lại; không đổi nội dung Quiz đã công bố hoặc phát hành Quiz thiếu câu. | [!] UNVERIFIED |

## Student: ghi danh, làm bài, kết quả và dashboard

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-S01 / TEST 04 | Nhập JoinCode đúng, vào Course mới không cần ID. Mã sai/ghi danh trùng báo lỗi, không thêm khóa học trùng. | [!] UNVERIFIED |
| UI-S02 | Xem Topic/học liệu thuộc Course đã tham gia; preview text hoặc PDF và tải đủ năm định dạng; Course chưa ghi danh không lộ nội dung. | [!] UNVERIFIED |
| UI-S03 | Chỉ thấy Quiz Published; trước khi nộp, API đề không chứa đáp án đúng/lời giải; mỗi câu chọn được một phương án. | [!] UNVERIFIED |
| UI-S04 | Chưa chọn đủ không nộp được; tiến độ trả lời đúng. Rời/refresh mất lựa chọn chưa nộp đúng thông báo. | [!] UNVERIFIED |
| UI-S05 / TEST 06 | Nộp đủ đáp án chỉ tạo một Attempt; điểm/số đúng/sai/tỷ lệ/lựa chọn/đáp án/lời giải khớp đáp án biết trước và Backend. | [!] UNVERIFIED |
| UI-S06 / TEST 13 | Bấm nhanh, gửi lại hoặc hai cửa sổ nộp cùng Quiz; chỉ một Attempt/chấm điểm được lưu; request còn lại nhận xung đột 409 phù hợp, không báo hai lần thành công. | [!] UNVERIFIED |
| UI-S07 / TEST 07 | Lịch sử chứa bài vừa nộp; sau đăng xuất/đăng nhập, điểm và câu trả lời còn đúng; Quiz đã nộp có nút xem kết quả. | [!] UNVERIFIED |
| UI-S08 / TEST 07 | Gợi ý tính đúng Topic dưới 60%, có nhãn Theo quy tắc/lý do; chỉ tài liệu liên quan thuộc Course đã tham gia, không trùng, không gồm học liệu đã xóa. Download giữ tên/phần mở rộng gốc. | [!] UNVERIFIED |
| UI-S09 | Topic yếu chưa có tài liệu vẫn có thông báo; chưa có bài nộp hoặc không có Topic yếu dùng empty state, không bịa gợi ý. | [!] UNVERIFIED |
| UI-S10 / TEST 08 | Lecturer xem Sinh viên & kết quả của Course mình và mở chi tiết bài nộp đúng; không thấy Course người khác. | [!] UNVERIFIED |
| UI-S11 | Dashboard Student khớp số Course, Quiz hoàn thành/tổng Published, trung bình, tiến độ từng Course, 10 bài gần nhất và Topic yếu. Chưa nộp khác điểm 0; chưa có Quiz ghi rõ, không gọi tiến độ đọc tài liệu. Nút Mở khóa học vào đúng Course. | [!] UNVERIFIED |

## Lecturer: dashboard và thống kê

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-T01 | Tổng quan chỉ liệt kê Course đang sở hữu; số Course/lượt tham gia đúng, không gọi tổng lượt tham gia là số sinh viên phân biệt. Nút quản lý/thống kê mở đúng Course/tab. | [!] UNVERIFIED |
| UI-T02 | Thống kê Course đối chiếu DB: số học viên hiện ghi danh, Quiz Published, bài Submitted, trung bình; bảng từng Quiz có số người nộp/điểm đúng. | [!] UNVERIFIED |
| UI-T03 | Bảng từng sinh viên đối chiếu Quiz đã nộp/tổng Published/tỷ lệ/trung bình; Topic dưới 60% có số câu sai/tổng/sinh viên bị ảnh hưởng và lý do đúng. | [!] UNVERIFIED |
| UI-T04 | Course chưa có sinh viên/Quiz/bài nộp có empty state và trung bình Chưa có bài nộp, không hiển thị số giả; nút Cập nhật lấy dữ liệu mới. | [!] UNVERIFIED |

## Quyền, persistence và trải nghiệm chung

Thực hiện các bước request qua DevTools trong môi trường nghiệm thu, ngoài các test HTTP tự động đã ghi riêng.

| ID | Thao tác và kết quả cần đạt | Trạng thái |
|---|---|---|
| UI-X01 / TEST 09 | Student thử tạo/sửa Course, lấy ngân hàng đáp án, tạo/publish Quiz, statistics Lecturer hoặc quản lý tài khoản; Backend từ chối, UI không hiện chức năng trái quyền. | [!] UNVERIFIED |
| UI-X02 / TEST 10 | Lecturer B đổi ID đọc/sửa Course/Topic/Material/Question/Quiz/kết quả/statistics của A; Backend từ chối, không lộ dữ liệu. | [!] UNVERIFIED |
| UI-X03 / TEST 11 | Student B chưa ghi danh đổi ID lấy material/đề/điểm A; bị từ chối, không nhận file/đáp án/kết quả riêng. | [!] UNVERIFIED |
| UI-X04 / TEST 12 | Lưu dữ liệu thử nghiệm, khởi động lại cùng database/storage; Course/Topic/Material/Question/Quiz/Attempt còn nguyên. Migration trên bản sao giữ dữ liệu cũ; đăng nhập lại nếu JWT key Development đổi. | [!] UNVERIFIED |
| UI-X05 | Các danh sách chưa có dữ liệu/bộ lọc không khớp/trang cuối hiển thị empty state theo ngữ cảnh, không trắng hoặc lỗi JavaScript. | [!] UNVERIFIED |
| UI-X06 | Mạng chậm có loading/nút submit chống lặp; success/error đọc được; sau refresh dữ liệu đã lưu vẫn đúng. | [!] UNVERIFIED |
| UI-X07 | Ngắt mạng/API có lỗi kết nối rõ; Thử lại tải được sau phục hồi, không thay dữ liệu thật bằng mẫu. | [!] UNVERIFIED |
| UI-X08 | Điều hướng/chuyển Course/refresh/quay lại không crash hoặc hiện dữ liệu phiên trước; token lỗi về đăng nhập đúng cách. | [!] UNVERIFIED |
| UI-X09 | Desktop 1440px, tablet 768px, mobile 360px: nav/sidebar/form/metrics/bộ lọc/Quiz/preview dùng được. Bảng statistics cuộn trong khung, không đẩy tràn toàn trang hoặc che nút. Rà CSS chưa thay thế bước này. | [!] UNVERIFIED |
| UI-X10 | Tab/Enter/Space dùng được; input/radio có nhãn/focus, lỗi/loading đọc được; tiếng Việt và tên tệp dài xuống dòng phù hợp. | [!] UNVERIFIED |
| UI-X11 | HTML trong câu hỏi/Topic/tên tệp hiển thị dạng text, không thực thi; bundle/Network/log không chứa Gemini key/JWT signing key. | [!] UNVERIFIED |
| UI-X12 | Timestamp có/không hậu tố Z và có offset hiển thị cùng thời điểm theo giờ thiết bị; ngày không có hiển thị rõ. Cross-origin đọc được Content-Disposition/Retry-After, download tên gốc và lỗi có thời gian chờ. | [!] UNVERIFIED |

Nghiệm thu UI hoàn tất khi các dòng phù hợp đã được thực hiện và có bằng chứng. SQL Server live, HTTPS/reverse proxy, restore backup và CI remote cần kiểm tra riêng theo hướng dẫn triển khai; không suy ra từ checklist UI hoặc SQLite local.
