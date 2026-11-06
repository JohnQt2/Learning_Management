# Kiến trúc LMS-AI

Giữ **Layered N-Tier Modular Monolith**,4 project nghiệp vụ trong một Backend và React/Vite riêng.

```text
React 19 / Vite
  → Controllers + JWT / middleware / rate limits
    → ApplicationServices theo module
      → Infrastructure: LmsDbContext / file storage / Gemini adapter
        → SQLite hoặc SQL Server, thư mục Uploads
Domain: entities và các quan hệ nghiệp vụ
```

ApplicationServices sử dụng trực tiếp DbContext thông qua Infrastructure theo kiến trúc source ban đầu. Không thêm Repository, UnitOfWork, CQRS, MediatR hoặc microservices.

- Auth/Admin: PasswordHasher, JWT8 giờ, hồ sơ/password/logout, vai trò cố định, khóa và thu hồi session theo SecurityVersion.
- Course: owner/member kiểm tra Backend, JoinCode ngẫu nhiên8 ký tự, Enrollment unique.
- Learning Content: Topic; TXT/MD/PDF/DOCX/PPTX qua storage và extractor có giới hạn; metadata/preview/download có quyền, soft-delete giữ file và lịch sử. PDF scan/slide ảnh cần OCR; chưa có OCR.
- Quiz/Result: soạn Draft, phát hành câu Approved cùng Course, một Attempt, chấm0–10 và lưu atomic graph. Quiz.Revision chống sửa/publish trên lựa chọn cũ; dashboard/thống kê dùng Submitted và ghi danh hiện tại, lịch sử bài nộp vẫn giữ.
- Recommendation: tỷ lệ đúng tổng hợp theo Topic dưới60%, chỉ học liệu Course còn ghi danh.
- AI: Gemini REST chỉ khi user sinh chủ động. Planner bao phủ toàn bộ văn bản theo thứ tự UTF-16 hoặc từ chối trước request; budget dùng chung các đoạn/retry429/503, có Retry-After/backoff/jitter. Chỉ lưu Draft atomic sau validation, ghi độ khó/đoạn nguồn; giảng viên duyệt. Disabled không ảnh hưởng luồng thủ công; Mock chỉ Development.
- Persistence: hai provider context dùng cùng model, baseline+additive migrations, tiếp nhận SQLite legacy có backup; không tự xóa/reset dữ liệu.
- Frontend: pages theo nghiệp vụ, shared loading/action hooks, API client chuẩn hóa error/401, session expiry và request cancellation.

Backend tests gồm service smoke, security/provider transport fixtures, HTTP WebApplicationFactory và hosting config. Python stdlib kiểm tra schema legacy. FE Node tests kiểm API/session/helpers và Vite build. Các phép này không thay thế UI browser/live Gemini/SQL Server acceptance; trạng thái ở [VERIFICATION](VERIFICATION.md).

