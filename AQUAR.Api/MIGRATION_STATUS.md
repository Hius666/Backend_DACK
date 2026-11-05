# RequirePasswordChange: kết quả lần áp dụng được yêu cầu

Người dùng đã xác nhận cập nhật AQUAR_DB trên TUAN\SQLEXPRESS. Chưa thực hiện backup, database update hoặc thay đổi dữ liệu vì kết nối SQL Server thất bại ngay ở truy vấn kiểm tra chỉ đọc. Không thử giảm bảo mật TLS hoặc đổi xác thực để vượt lỗi.

Đã xác minh source/config:

- ConnectionStrings:Default: đúng TUAN\SQLEXPRESS / AQUAR_DB, Windows Authentication.
- Local EF CLI 8.0.11.
- Up migration chỉ thêm Staff.MustChangePassword bit NOT NULL DEFAULT 0 và Staff.TokenVersion int NOT NULL DEFAULT 0. SQL chỉ thêm hai cột và một dòng lịch sử EF; không có lệnh xóa/reset tài khoản. Down không được chạy.
- Build API và Checks (configuration Verification): 0 warnings, 0 errors.
- 10 checks cũ và 39 checks workflow/middleware đạt.
- Full runner --integration trả exit code 1 do không mở được kết nối SQL, trước khi tạo transaction hoặc fixture. Các trường hợp SQL/API không được coi là đã đạt.

Lỗi sqlcmd:

```text
Encryption not supported on the client.
SSL Provider: No credentials are available in the security package
Client unable to establish connection.
```

Lỗi integration:

```text
Microsoft.Data.SqlClient.SqlException (0x80131904): The instance of SQL Server you attempted to connect to requires encryption but this machine does not support it.
   at Microsoft.Data.SqlClient.SqlInternalConnection.OnError(...)
   at Microsoft.Data.SqlClient.TdsParser.Connect(...)
   ...
   at AuthIntegrationChecks.Run() in D:\LTDD_FINAL\AQUAR.Backend.Checks\AuthIntegrationChecks.cs:line 31
   at AuthIntegrationChecks.Run() in D:\LTDD_FINAL\AQUAR.Backend.Checks\AuthIntegrationChecks.cs:line 216
   at Program.<Main>$(String[] args) in D:\LTDD_FINAL\AQUAR.Backend.Checks\Program.cs:line 45
Error Number:20,State:0,Class:20
```

Phiên hiện tại không có quyền yêu cầu Terminal chạy ngoài sandbox (approval policy: never); không thể chuyển sang ngữ cảnh Windows Authentication trước đây từng kết nối thành công. Lỗi không đủ để kết luận SQL Server/database hỏng hoặc SQL login thiếu quyền BACKUP/ALTER vì chưa kết nối được.

Đã chuẩn bị Scripts/Apply-RequirePasswordChange.ps1 cho PowerShell tương tác có quyền truy cập SQL Server. Script được kiểm tra cú pháp; sao lưu và các thao tác SQL trong script chưa được xác minh thực tế. Hướng dẫn Swagger: PASSWORD_WORKFLOW.md. Không thay đổi Flutter hoặc schema của database trong lượt này.
