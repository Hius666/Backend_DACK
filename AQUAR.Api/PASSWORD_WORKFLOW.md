# Mật khẩu tạm thời — migration đã được duyệt, chưa áp dụng

Code và migration đã chuẩn bị, người dùng đã xác nhận cho phép áp dụng; chưa áp dụng lên AQUAR_DB vì phiên công cụ hiện tại không kết nối được SQL Server để sao lưu. Không khởi chạy bản Backend mới với schema cũ: truy vấn Staff sẽ thiếu cột. Chưa chỉnh sửa Flutter.

Script vận hành đã chuẩn bị ở Scripts/Apply-RequirePasswordChange.ps1: kiểm tra server/database/history, build, kiểm tra đúng SQL, sao lưu COPY_ONLY có CHECKSUM và RESTORE VERIFYONLY, áp dụng bằng EF 8.0.11, so sánh fingerprint dữ liệu nghiệp vụ trước/sau, kiểm tra giá trị mặc định và chạy toàn bộ checks. Script chưa được thực thi tới SQL Server, mới kiểm tra cú pháp. Chạy trong PowerShell có quyền Windows Authentication; dừng các phiên AQUAR API, kể cả phiên chạy bằng dotnet, trước khi chạy. Script không tự dừng tiến trình hoặc khôi phục database.

```powershell
cd D:\LTDD_FINAL
.\AQUAR.Api\Scripts\Apply-RequirePasswordChange.ps1
```

Mặc định dùng thư mục backup của SQL Server. Nếu SQL Server không cung cấp đường dẫn mặc định, truyền -BackupDirectory với thư mục đã có và SQL Server service được quyền ghi. Backup là file riêng có tên duy nhất, không ghi đè backup cũ. Nếu lỗi sau khi migration đã áp dụng, script dừng để kiểm tra, không tự chạy migration Down hoặc RESTORE. SQL Server có thể cấp số identity cho fixture kiểm thử dù transaction được rollback; bản ghi/tài khoản hiện có không bị reset.

## Thay đổi database cần xác nhận

Migration: 20261105063815_RequirePasswordChange (EF Core 8.0.11).

```sql
ALTER TABLE [Staff] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
ALTER TABLE [Staff] ADD [TokenVersion] int NOT NULL DEFAULT 0;
```

Migration còn thêm một dòng vào __EFMigrationsHistory. Không xóa bảng/cột/bản ghi, không thay đổi hash mật khẩu hoặc role hiện có. MustChangePassword=false cho tài khoản cũ để không ép đổi mật khẩu hàng loạt; tài khoản do Admin tạo từ bản code mới được đặt true. TokenVersion=0 cho tài khoản cũ; dùng để thu hồi mọi JWT khi đổi/reset mật khẩu hoặc đăng xuất, trạng thái được lưu SQL nên không mất sau restart. JWT cũ thiếu token_version phải đăng nhập lại.

PasswordHash và TokenVersion được cấu hình concurrency token trong EF: nếu hai phiên đổi/reset/logout đồng thời, thao tác ghi với phiên bản cũ trả HTTP 409, không ghi đè thay đổi vừa xảy ra. Cấu hình này không thêm cột khác.

Sau khi người dùng xác nhận mới thực hiện database update. Không chạy Down: Down mặc định là rollback xóa hai cột mới, không nằm trong kế hoạch áp dụng.

## Hành vi API

- POST /api/staff (Admin): trường password là mật khẩu tạm do Admin tự nhập. Lưu BCrypt cost 12, salt ngẫu nhiên; đặt MustChangePassword=true. Không trả hash hoặc mật khẩu trong response.
- POST /api/auth/login: giữ token/user, bổ sung mustChangePassword ở cấp response và user. Dùng trạng thái trả về để hướng dẫn đổi mật khẩu.
- PUT /api/account/password (mọi tài khoản đã xác thực): yêu cầu currentPassword, newPassword, confirmNewPassword. Kiểm tra mật khẩu cũ, xác nhận khớp, mật khẩu mới khác cũ và đáp ứng chính sách. Thành công đặt MustChangePassword=false, tăng TokenVersion, trả 200; phải đăng nhập lại.
- PUT /api/accounts/{id}/password (chỉ Admin): newPassword là mật khẩu tạm mới. Đặt MustChangePassword=true, tăng TokenVersion, thu hồi mọi JWT cũ. Không xem/khôi phục mật khẩu hiện tại. Không dùng endpoint reset cho chính Admin đang gọi; dùng endpoint tự đổi mật khẩu.
- POST /api/auth/logout: cho phép cả tài khoản đang bị ép đổi mật khẩu, tăng TokenVersion để đăng xuất toàn bộ phiên của tài khoản, trả 204. Không bỏ cờ bắt buộc đổi mật khẩu.
- Khi MustChangePassword=true: API nghiệp vụ và /api/me trả 403 với code MUST_CHANGE_PASSWORD (nếu người gọi có quyền vào endpoint đó). Middleware phân quyền vẫn có thể trả 403 sớm khi vai trò vốn không được phép truy cập endpoint. Chỉ đăng nhập, đổi mật khẩu và đăng xuất được cho phép.
- Admin bootstrap bằng --init-admin vẫn dùng mật khẩu do chủ tài khoản tự nhập, không đánh dấu tạm thời. Tài khoản Admin do một Admin khác tạo qua /api/staff cũng phải đổi mật khẩu lần đầu.

Không có endpoint đăng ký công khai. Không bật request-body logging hoặc EF sensitive-data logging. Mật khẩu phải nhập trong request HTTPS khi triển khai qua mạng; không đặt mật khẩu trong URL, log, source hoặc ảnh chụp Swagger.

## Kiểm thử Swagger sau khi migration được duyệt và áp dụng

1. Dừng bản Backend cũ trước khi triển khai migration và rebuild bản mới; chạy API rồi mở /swagger.
2. Đăng nhập Admin ở POST /api/auth/login. Authorize bằng token, không thêm tiền tố Bearer.
3. POST /api/staff: nhập name, email, password tạm do bạn chọn, role=2, hourlyRate; tùy chọn managerId/departmentId. Ghi lại id. Response có mustChangePassword=true.
4. Xóa token Admin trong Swagger; đăng nhập nhân viên bằng mật khẩu tạm. Response 200, mustChangePassword=true. Authorize bằng token này.
5. GET /api/attendance/history: mong đợi 403, code MUST_CHANGE_PASSWORD.
6. PUT /api/account/password: điền currentPassword là mật khẩu tạm, newPassword tự chọn, confirmNewPassword trùng newPassword. Xác nhận sai hoặc mật khẩu cũ sai phải trả 400, không mở khóa tài khoản. Mật khẩu mới cần ít nhất 12 ký tự, tối đa 72 byte UTF-8.
7. Đổi thành công trả 200. Gọi lại /api/me với token cũ phải trả 401. Đăng nhập lại bằng mật khẩu mới, mustChangePassword=false; GET /api/me hoặc history trả 200.
8. Dùng token Employee hoặc Manager gọi PUT /api/accounts/{id}/password: phải trả 403. Đăng nhập lại Admin, reset nhân viên bằng newPassword tạm do bạn chọn: trả 200.
9. Token nhân viên trước reset phải bị từ chối 401. Đăng nhập bằng mật khẩu tạm mới: mustChangePassword=true; lặp bước đổi mật khẩu để mở khóa.
10. Kiểm tra POST /api/auth/logout khi bị ép đổi mật khẩu: trả 204; token vừa dùng bị từ chối 401. Đăng nhập lại vẫn phải đổi mật khẩu.

Nếu gặp HTTP 429, chờ hết cửa sổ giới hạn 10 lần đăng nhập/phút/IP. Không vô hiệu giới hạn để kiểm tra.

## Trạng thái kiểm thử

Build configuration Verification để tránh ghi đè exe đang được dùng. 10 kiểm tra cũ và 39 kiểm tra workflow/middleware cho Admin, Manager, Employee đã đạt, không kết nối database. Model khớp migration mới.

AuthIntegrationChecks đã bổ sung payload xác nhận mật khẩu, tạo mật khẩu tạm qua API, chặn nghiệp vụ, quyền reset, thu hồi token, đổi sau reset và logout. Chưa chạy bộ SQL/API mới: phải đợi migration được xác nhận và áp dụng, đồng thời terminal phải có quyền Windows Authentication với SQL Server. Không coi các test SQL/API chưa chạy là đã đạt.

Sau khi schema sẵn sàng, lệnh từ D:\LTDD_FINAL:

```powershell
dotnet run --project .\AQUAR.Backend.Checks --configuration Verification --no-build --no-restore -- --integration
```

Integration dùng dữ liệu riêng trong transaction rồi rollback, không reset database hoặc sửa tài khoản hiện có. Identity có thể có khoảng trống sau rollback.

Flutter chưa được sửa để hiển thị màn hình bắt buộc đổi mật khẩu hoặc gửi confirmNewPassword; ứng dụng hiện tại sẽ nhận 403 cho tài khoản tạm cho đến khi bổ sung giao diện ở giai đoạn sau.
