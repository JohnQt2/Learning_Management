# Audit nâng cấp LMS-AI —29/10/2026

Đã có: Auth/JWT/SecurityVersion, owner/member authorization, Course/Topic, manual Questions/Quiz, scoring/results/recommendation và các tests. Chưa đủ yêu cầu mới: profile metadata/edit, Admin search/filter, multi-format extraction/metadata/delete, bounded long-source chunk/retry, dashboards/statistics.

Baseline thực chạy: Release build0 warning/error; Smoke96, Security73, HTTP105, Hosting53; FE13 tests và Vite build38 modules đạt. Không dùng kết quả này để chứng minh UI browser hoặc Gemini live.

Schema local lúc audit:4 Users,1 Course,1 Enrollment,1 Topic,1 Material,3 Questions/12 Options,1 Quiz/3 QuizQuestions,1 Attempt/3 Answers,7 AIGenerationLogs,0 Recommendations. Integrity/FKs đạt. DB hash trước nâng cấp c4bc92b9b67e05dc337c80d2ca490a7d30fecdf82c0cdf0b95774684859601b0. Backup nhất quán tại artifacts/backups/upgrade_20261029_015812 cùng1 file học liệu, không chứa trong Git.

P1 cần nâng cấp: hồ sơ/roster/admin filters;5-format extraction; retry/chunk budget; progress/statistics. P2: metadata/preview/delete, filter Question, UX; tài liệu cũ có đoạn encoding bị lỗi và ghi trạng thái quá cũ, cần làm sạch khi Phase6.

Migrations phải additive, legacy timestamp/size/uploader chưa biết giữ null, không tạo lịch sử giả. Material deletion dự kiến soft-delete, giữ nguyên file/Question FK. Không tạo Course status khi thiết kế hiện không có.

Giới hạn môi trường: Docker CLI có nhưng daemon không chạy; browser tool lỗi khởi tạo assets os error3; no .git. Google Gemini live mới chỉ có lỗi503/high demand từ chẩn đoán thật trước, chưa có generation thành công. Không tự bật billing/đổi model.

Duy trì6 phase theo PROJECT_TASKS.md; thực hiện gate mỗi phase trước phần phụ thuộc tiếp theo.

