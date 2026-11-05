# AQUAR Backend

ASP.NET Core .NET 8, EF Core SQL Server. Models ở Models.cs; DTOs, Data, Controllers và Services ở các thư mục tương ứng.

## Database

Không có lệnh tự động tạo/cập nhật/xóa database hoặc seed dữ liệu khi khởi động. Ngày 2026-11-05 đã tạo database mới AQUAR_DB trên TUAN\SQLEXPRESS bằng Windows Authentication và áp dụng migration 20261105060157_InitialCreate với EF Core 8.0.11. Có các bảng dbo.Departments, dbo.Staff, dbo.Attendances và dbo.__EFMigrationsHistory. Ba bảng nghiệp vụ đều rỗng; chưa tạo Admin hoặc dữ liệu mẫu. Không thay đổi database cũ. Không dùng EnsureCreated để thay thế migration. Admin đầu tiên cần được chuẩn bị riêng; không mở đăng ký công khai.

Công cụ EF được cố định phiên bản 8.0.11 trong ../dotnet-tools.json. Từ thư mục D:\LTDD_FINAL dùng `dotnet tool restore` và `dotnet tool run dotnet-ef -- --version`. SQL đã kiểm tra nằm ở Migrations/InitialCreate.sql; script chỉ tạo bảng, khóa/index và ghi lịch sử migration. Khi tạo database mới, provider EF còn bật READ_COMMITTED_SNAPSHOT riêng cho AQUAR_DB. Phương thức Down của migration là rollback mặc định, chưa được chạy.

Đã xác minh 3 khóa ngoại hoạt động, filtered unique index cho ca chưa kết thúc, không có model changes sau migration, build 0 lỗi/0 cảnh báo và 10 checks đạt. Các checks hiện tại không ghi dữ liệu thử vào SQL Server; xác minh schema và số bản ghi được thực hiện bằng truy vấn chỉ đọc.

## Cấu hình

Giữ ConnectionStrings:Default, Jwt:Key (ít nhất 32 byte), Jwt:Issuer, Jwt:Audience và Smtp:Host/Port/From/User/Password hiện có. Có thể ghi đè bằng biến môi trường với dấu __ (ví dụ Jwt__Key). SMTP dùng STARTTLS, mặc định cổng 587. Không lưu mật khẩu thật vào source control.

## API

- POST /api/auth/login; GET /api/me
- PUT /api/account/password: currentPassword, newPassword
- GET /api/accounts; PUT /api/accounts/{id}/password: newPassword (Admin)
- GET/POST /api/staff; GET/PUT /api/staff/{id}; PATCH /api/staff/{id}/deactivate
- GET/POST /api/departments; GET/PUT /api/departments/{id}
- POST /api/attendance/check-in, check-out: {"method":"manual"}; vẫn nhận biometric để tương thích client cũ, không coi nhãn đó là bằng chứng sinh trắc học.
- POST /api/attendance/manual: staffId, checkInUtc, checkOutUtc, reason
- GET /api/attendance/history?staffId=1 (200 bản ghi gần nhất)
- GET /api/reports?staffId=1&from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z
- GET /api/statistics?period=quarter&date=2026-11-05&staffId=1
- GET /api/payroll?period=month&date=2026-11-05&staffId=1
- POST /api/reports/email?staffId=1&from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z

Statistics hỗ trợ week/month/quarter/year; payroll hỗ trợ week/month. Ngày lịch theo UTC+7, tuần bắt đầu thứ Hai. Khoảng báo cáo [from,to), tối đa 366 ngày; chỉ tính ca đã kết thúc, cắt giờ ca theo biên kỳ. Lương = giờ chưa làm tròn × đơn giá giờ hiện tại, làm tròn tiền đến 2 chữ số; chưa xử lý lịch sử đơn giá, OT, thuế/bảo hiểm.

Admin quản trị; Manager xem/chấm công thủ công cho nhân viên thuộc ManagerId của mình; Employee chỉ truy cập dữ liệu bản thân. Manager cũng có thể xem/chấm công bản thân. JWT hết hạn sau 8 giờ; mỗi request kiểm tra tài khoản còn hoạt động, vai trò còn khớp và credential stamp của mật khẩu chưa thay đổi. Đổi/reset mật khẩu thu hồi JWT cũ. Xem LOGIN.md để khởi tạo Admin bằng terminal tương tác, đăng nhập Swagger và chạy integration checks với SQL Server.

Giữ đường dẫn, enum số và cấu trúc response mà Flutter hiện tại dùng. Đăng nhập trả role dạng chuỗi, danh sách nhân viên trả role dạng số. Check-in/out thông thường trên Flutter gửi method=manual, với cờ Manual=false; Manual=true dành riêng cho ca do quản lý nhập hộ qua /api/attendance/manual. Bản ghi biometric cũ vẫn đọc được và không bị đổi nhãn. NFC/sinh trắc học chưa được triển khai. Xem ATTENDANCE_METHODS.md.

## Kiểm tra

dotnet restore

dotnet build --no-restore

dotnet run --project ../AQUAR.Backend.Checks

Checks không kết nối SQL Server: kiểm tra biên kỳ tuần/tháng/quý/năm, validation DTO và claim JWT. Chưa kiểm thử tích hợp SQL Server/SMTP hoặc luồng Flutter thực tế. Swagger có ở /swagger khi chạy ứng dụng.

Máy phát triển đã cài Microsoft.AspNetCore.App 8.0.33 và Microsoft.NETCore.App 8.0.33. Checks đã chạy trực tiếp bằng EXE và dotnet run trên runtime 8, không cần DOTNET_ROLL_FORWARD. Khi assertion/exception thất bại, runner in đầy đủ exception và stack trace ra stderr, trả exit code 1; không bỏ qua kiểm tra.
