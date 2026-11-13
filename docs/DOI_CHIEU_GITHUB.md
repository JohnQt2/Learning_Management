# Đồng bộ GitHub

Thư mục hiện tại không chứa .git; git status trả fatal:not a git repository. Phiên này không fetch/push/merge, không ghi đè thay đổi trên remote và không xác nhận branch/SHA GitHub hiện hành.

Tài liệu cũ từng mô tả JohnQt2/Learning_Management@develop và một bản ZIP khác nhau. Đây là ghi chú lịch sử chưa được đối chiếu lại, không dùng làm bằng chứng remote hiện tại.

Khi nhóm tích hợp, clone repository vào thư mục riêng, tạo branch thực, so sánh từng module với bản làm việc, bảo toàn dữ liệu và commit riêng công việc đã hiểu. Build/test các gate trong README, mở PR review rồi mới merge theo quy trình nhóm. Không tạo commit giả cho6 thành viên.

Không commit bin/obj/node_modules/dist, SQLite/WAL/backups/Uploads, environment secrets hoặc file kết quả kiểm thử chứa dữ liệu người dùng.

