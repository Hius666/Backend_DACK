# Đăng nhập và khởi tạo Admin

Đang có bản nâng cấp mật khẩu tạm chờ duyệt migration. Xem PASSWORD_WORKFLOW.md trước khi triển khai bản code mới. API tự đổi mật khẩu mới bắt buộc thêm confirmNewPassword; logout mới thu hồi mọi phiên. Chưa áp dụng migration RequirePasswordChange lên AQUAR_DB.

## Khởi tạo Admin đầu tiên

Mở PowerShell tương tác bằng tài khoản Windows có quyền truy cập AQUAR_DB. Từ D:\LTDD_FINAL chạy:

```powershell
dotnet run --project .\AQUAR.Api --no-launch-profile -- --init-admin
```

Kiểm tra server/database hiển thị là TUAN\SQLEXPRESS / AQUAR_DB, rồi nhập email, họ tên, mật khẩu và xác nhận mật khẩu. Mật khẩu không hiển thị khi gõ, không truyền bằng command line/pipe và không lưu vào file. Nhấn Escape khi nhập mật khẩu để hủy.

Mật khẩu mới cần ít nhất 12 ký tự, tối đa 72 byte UTF-8 (tiếng Việt có thể dùng nhiều byte/ký tự), không có ký tự NUL. Nên dùng mật khẩu dài, duy nhất, do trình quản lý mật khẩu tạo. Backend lưu BCrypt có salt ngẫu nhiên, work factor 12; không lưu mật khẩu rõ và vẫn xác minh được hash BCrypt cũ.

Lệnh không khởi động web server, không tạo database hoặc chạy migration. Chỉ tạo một tài khoản Admin nếu chưa từng có bản ghi Admin; nếu có Admin kể cả bị khóa, hoặc email đã tồn tại, lệnh từ chối và giữ nguyên dữ liệu. SQL transaction và application lock ngăn hai tiến trình bootstrap cùng tạo Admin. Không có endpoint HTTP công khai để chạy bootstrap hoặc đăng ký tài khoản.

Lượt phát triển này chưa tạo Admin thật: tất cả tài khoản dùng trong kiểm thử đã rollback. Bạn tự nhập mật khẩu bằng lệnh trên.

## Đăng nhập bằng Swagger

Khởi động API để sử dụng cục bộ:

```powershell
dotnet run --project .\AQUAR.Api --no-launch-profile --urls http://127.0.0.1:5080
```

1. Mở http://127.0.0.1:5080/swagger.
2. Chọn POST /api/auth/login → Try it out; nhập email và mật khẩu vừa khởi tạo vào JSON, rồi Execute.
3. Response 200 có token và user.role là Admin. Sao chép riêng chuỗi token.
4. Bấm Authorize, dán token (không thêm chữ Bearer), rồi Authorize/Close.
5. Gọi GET /api/me hoặc GET /api/accounts để kiểm tra. Không chia sẻ token hoặc ảnh Swagger chứa mật khẩu.

HTTP localhost ở trên chỉ dùng trên máy phát triển. Khi truy cập qua mạng/triển khai, dùng HTTPS và bảo vệ JWT signing key bằng biến môi trường hoặc secret store. Không dùng signing key trong source làm bí mật triển khai. Nếu dùng cấu hình mặc định có sẵn, hãy thay Jwt:Key bằng giá trị ngẫu nhiên mạnh trước khi đưa API ra mạng; cần tối thiểu 32 byte. Các khóa cấu hình khác: Jwt:Issuer và Jwt:Audience.

## Vai trò và xác thực

- Admin: quản trị tài khoản/nhân viên/phòng ban. Chỉ Admin đã đăng nhập mới có thể tạo tài khoản bằng POST /api/staff (role 0=Admin, 1=Manager, 2=Employee).
- Manager: xem nhân viên được gán ManagerId; không truy cập danh sách tài khoản quản trị hoặc nhân viên thuộc quản lý khác.
- Employee: xem hồ sơ và lịch sử của bản thân, không có quyền quản trị.

JWT hết hạn sau 8 giờ, kiểm tra chữ ký, issuer, audience, hạn dùng và trạng thái/vai trò tài khoản trong SQL Server. Đổi/reset mật khẩu, khóa tài khoản hoặc đổi vai trò làm token cũ bị từ chối. Token phát hành trước khi bổ sung credential_stamp cũng cần đăng nhập lại. Đổi mật khẩu dùng PUT /api/account/password; sau đó đăng nhập lại và thay token trong Swagger. Admin reset mật khẩu qua PUT /api/accounts/{id}/password.

Đăng nhập sai email/mật khẩu hoặc tài khoản bị khóa trả 401 chung; thiếu quyền trả 403. Login giới hạn 10 yêu cầu/phút/IP trên mỗi tiến trình API, vượt giới hạn trả 429. Nếu triển khai nhiều instance/reverse proxy cần cấu hình IP tin cậy và giới hạn dùng chung phù hợp.

## Kiểm thử

```powershell
dotnet restore .\AQUAR.Backend.Checks
dotnet build .\AQUAR.Backend.Checks --no-restore
dotnet run --project .\AQUAR.Backend.Checks --no-build --no-restore
dotnet run --project .\AQUAR.Backend.Checks --no-build --no-restore -- --integration
```

Integration chạy pipeline thật qua ASP.NET Core TestServer, xác thực JWT thật và EF Core SQL Server thật tới AQUAR_DB. Tạo fixture có email riêng trong transaction và rollback, không sửa tài khoản sẵn có hoặc giữ lại tài khoản thử. Không reset database/migration. Việc insert rồi rollback có thể tạo khoảng trống trong số identity; không được giả định Id liên tục. Chỉ chạy integration khi database không có công việc đồng thời quan trọng vì transaction có thể giữ khóa trong thời gian kiểm thử.

Nếu đã có Admin, kiểm thử không sửa Admin đó và vẫn kiểm tra bootstrap từ chối; nhánh tạo Admin đầu tiên được kiểm tra khi database chưa có Admin. Assertion thất bại in exception/stack trace và exit code 1; không bị bỏ qua. Không kiểm thử SMTP hoặc Flutter ở bước này.
