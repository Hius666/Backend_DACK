# Kiểm chứng SQL Server staging cho migration GPS

## Kết quả lượt kiểm tra hiện tại

Chỉ đọc source/config, sinh lại SQL vào .verification/gps-staging-review.sql và thử truy vấn master. Không tạo database, seed dữ liệu, chạy database update, kết nối nghiệp vụ hoặc ghi dữ liệu lên AQUAR_DB. Không sửa migration, Backend hoặc Flutter.

- Cấu hình ứng dụng hiện trỏ TUAN\SQLEXPRESS / AQUAR_DB bằng Windows Authentication. Không dùng cấu hình mặc định đó để chạy lệnh kiểm chứng staging.
- EF CLI cục bộ đúng 8.0.11. Đã sinh lại script từ RequirePasswordChange đến AddAttendanceLocations; so sánh nội dung sau chuẩn hóa whitespace: khớp file Migrations/AddAttendanceLocations.sql.
- SQL tiến không có câu lệnh DROP, DELETE, TRUNCATE, UPDATE, ALTER hoặc USE. Hai CREATE TABLE và hai CREATE UNIQUE INDEX đúng dự kiến, cùng INSERT một dòng __EFMigrationsHistory.
- Up chỉ tạo AttendanceLocations, AttendanceLocationAlerts và khóa/index. Không đổi Staff/Attendances; FK chỉ tham chiếu Attendances.Id. Down có DropTable hai bảng mới để rollback, chưa và không được chạy trong kế hoạch này.
- DDL được sinh bởi provider SQL Server: int IDENTITY, float, datetime2, nvarchar(64), khóa ngoại ON DELETE NO ACTION. Không thấy SQL đặc thù SQLite hoặc bất tương thích hiển nhiên. Chưa xác minh phiên bản SQL Server thật hoặc chạy DDL native.
- Chuỗi migration hiện tại: 20261105060157_InitialCreate → 20261105063815_RequirePasswordChange → 20261105080604_AddAttendanceLocations. Script GPS là delta sau RequirePasswordChange, không phải script tạo toàn bộ database.

Kết nối chỉ đọc tới master thất bại:

```text
Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : Encryption not supported on the client..
Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : SSL Provider: No credentials are available in the security package
Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : Client unable to establish connection.
```

Vì chưa xác minh được server thực tế, quyền tạo database hoặc staging có tồn tại, đã dừng trước mọi ghi SQL. Phiên công cụ hiện tại không được phép yêu cầu chạy ngoài sandbox. Không thể coi staging chưa tồn tại, quyền SQL đã đủ, hoặc kiểm thử native đã PASS. Kết quả SQLite tạm ở lượt trước không thay thế SQL Server staging.

## Điều kiện để tiếp tục

Chạy từ PowerShell/SSMS tương tác có Windows Authentication hoạt động. Trước khi ghi, người thực hiện phải xác nhận kết quả preflight đúng server, quyền tạo DB và AQUAR_GPS_STAGING chưa tồn tại. Nếu không xác định được bất kỳ điều kiện nào thì dừng, không đổi kiểu xác thực hoặc tắt bảo mật để né lỗi. Nếu trùng tên, dừng; không DROP, restore đè hoặc tái sử dụng staging đó.

Các bước dưới đây chỉ là hướng dẫn chưa thực thi. Chưa xin phép áp dụng GPS lên AQUAR_DB.

### 1. Preflight chỉ đọc

Trong SSMS, mở kết nối đúng TUAN\SQLEXPRESS, chọn master, chạy:

```sql
SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerName,
       DB_NAME() AS ConnectedDatabase, ORIGINAL_LOGIN() AS LoginName,
       SERVERPROPERTY('ProductVersion') AS SqlVersion;
SELECT name FROM sys.databases WHERE name=N'AQUAR_GPS_STAGING';
SELECT HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE') AS CanCreateDatabase;
```

Chỉ tiếp tục sau khi xác nhận đúng TUAN\SQLEXPRESS / master, kết quả tên staging rỗng và quyền tạo bằng 1. Không suy luận kết quả rỗng từ lỗi truy vấn.

### 2. Tạo staging mới, tuyệt đối không xóa database cũ

Chỉ chạy sau khi điều kiện/môi trường được xác nhận:

```sql
IF COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> N'TUAN\SQLEXPRESS'
    THROW 51000, 'Unexpected SQL Server; stopped.', 1;
IF COALESCE(DB_NAME(),N'') <> N'master'
    THROW 51000, 'Expected master for creation preflight; stopped.', 1;
IF DB_ID(N'AQUAR_GPS_STAGING') IS NOT NULL
    THROW 51000, 'Staging already exists; do not delete/reuse it.', 1;
IF COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE'),0) <> 1
    THROW 51000, 'CREATE DATABASE permission not confirmed.', 1;
EXEC(N'CREATE DATABASE [AQUAR_GPS_STAGING]');
```

Không chạy câu CREATE khác ngoài tên cố định trên. Không sao chép tài khoản hoặc dữ liệu thật vào staging cho bài kiểm tra tối thiểu này.

### 3. Tạo baseline tới RequirePasswordChange bằng EF 8.0.11

Đổi query context trong SSMS sang AQUAR_GPS_STAGING, xác minh DB_NAME() đúng tên và chưa có user tables. Từ PowerShell tại D:\LTDD_FINAL, dùng connection string staging riêng, không sửa appsettings:

```powershell
$stageConnection = 'Server=TUAN\SQLEXPRESS;Database=AQUAR_GPS_STAGING;Trusted_Connection=True;TrustServerCertificate=True'
$savedConnection = $env:ConnectionStrings__Default
$savedEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ConnectionStrings__Default = $stageConnection
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    dotnet tool run dotnet-ef -- --version
    if ($LASTEXITCODE -ne 0) { throw 'EF tool check failed' }
    dotnet build .\AQUAR.Api --configuration GpsChecks --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    # Before this write, independently verify DB_NAME() over stageConnection.
    dotnet tool run dotnet-ef -- database update RequirePasswordChange --project AQUAR.Api --startup-project AQUAR.Api --context AppDb --configuration GpsChecks --no-build --connection $stageConnection
    if ($LASTEXITCODE -ne 0) { throw 'Staging baseline failed; stop and inspect, do not delete DB' }
} finally {
    $env:ConnectionStrings__Default = $savedConnection
    $env:ASPNETCORE_ENVIRONMENT = $savedEnvironment
}
```

Đây là lệnh ghi staging, chưa chạy trong phiên Codex. Trước khi chạy từng lệnh database update, phải xác minh target bằng SqlConnection/SSMS thực tế, không chỉ nhìn chuỗi cấu hình. Nếu có process-env DOTNET_ENVIRONMENT hoặc override khác, kiểm tra chúng trước. Không chạy API thật hoặc bộ --integration vốn trỏ AQUAR_DB trong quy trình này.

### 4. Chuẩn bị dữ liệu ca cũ chỉ trong staging

Trong SSMS connection/query đang chọn AQUAR_GPS_STAGING, xác minh lại server/database trước INSERT. Dùng một Staff fixture mới không đăng nhập được (Active=0, Role=Employee), không tạo Admin; tạo một ca đã kết thúc có Method=manual hoặc biometric và Reason đánh dấu staging. Không dùng tài khoản thật/mật khẩu thật, không seed GPS. Mọi block ghi phải có guard sau ở đầu và không đổi USE trong block:

```sql
IF COALESCE(CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)),N'') <> N'TUAN\SQLEXPRESS'
 OR COALESCE(DB_NAME(),N'') <> N'AQUAR_GPS_STAGING'
    THROW 51000, 'Wrong staging target; stopped.', 1;
```

Trước GPS, xác minh history chỉ gồm InitialCreate và RequirePasswordChange, hai bảng GPS chưa tồn tại. Ghi lại count và fingerprint dữ liệu Staff/Attendances, bao gồm giá trị Method, giờ vào/ra, Reason và hash mật khẩu để so sánh. So sánh hash trong SQL hoặc bộ nhớ, không in/log hash mật khẩu hay xuất tài khoản thật ra file.

### 5. Áp dụng GPS chỉ lên staging

Xác minh lại server/database bằng kết nối thực tế, rồi chạy cùng cấu trúc try/finally ở bước 3 với target migration AddAttendanceLocations và chính stageConnection đó. Cả ConnectionStrings__Default của process và --connection phải là staging. Không bỏ --connection hoặc dùng AQUAR_DB. Nếu không xác minh được target thì dừng trước lệnh.

Không áp dụng script GPS vào database rỗng: cần các bảng và hai migration nền. Không đặt Docker/Render startup hoặc pre-deploy command để tự chạy migration.

### 6. Xác minh sau migration

Chọn query context AQUAR_GPS_STAGING và chạy script chỉ đọc Scripts/Verify-GpsStaging.sql. So sánh lại fingerprint/count ca và tài khoản với baseline bước 4. Kỳ vọng:

- Có hai bảng GPS, ID int IDENTITY; các cột/tính nullable đúng script.
- Hai FK hoạt động, trusted, ON DELETE NO ACTION; hai unique index đúng cột/thứ tự.
- History thêm đúng 20261105080604_AddAttendanceLocations, ProductVersion=8.0.11.
- Các bản ghi Staff và Attendances giữ nguyên; bảng GPS/alert rỗng, không backfill dữ liệu giả.
- Thử riêng trên staging: FK chặn AttendanceId không tồn tại, unique chặn hai dòng cùng (AttendanceId,Event) và hai alert cùng location. Nếu thử insert vị trí, dùng transaction ROLLBACK, guard đúng staging trước mỗi block; không chạy vào AQUAR_DB.

Nếu bất kỳ kiểm tra nào sai, giữ staging để điều tra, không tự DROP database hoặc rollback migration. Ghi lại kết quả/exception trước khi thay đổi tiếp.

## Các đối tượng GPS dự kiến

| Loại | Tên |
|---|---|
| Bảng | AttendanceLocations |
| Bảng | AttendanceLocationAlerts |
| PK | PK_AttendanceLocations |
| PK | PK_AttendanceLocationAlerts |
| FK | FK_AttendanceLocations_Attendances_AttendanceId |
| FK | FK_AttendanceLocationAlerts_AttendanceLocations_AttendanceLocationId |
| Unique index | IX_AttendanceLocations_AttendanceId_Event |
| Unique index | IX_AttendanceLocationAlerts_AttendanceLocationId |

## Trước khi đề nghị áp dụng AQUAR_DB thật

1. Có kết quả SQL Server staging thực tế (không chỉ SQLite), ghi lại SQL Server version, history, metadata, count/fingerprint trước/sau và constraint checks.
2. Sắp xếp thời điểm tạm dừng ghi nghiệp vụ; xác minh đúng TUAN\SQLEXPRESS / AQUAR_DB và migration nền đang có. Chưa làm các thao tác này trong lượt hiện tại.
3. Tạo backup riêng tên duy nhất: BACKUP DATABASE AQUAR_DB ... WITH COPY_ONLY, CHECKSUM; không ghi đè backup cũ. Thư mục backup phải do SQL Server service ghi được.
4. RESTORE VERIFYONLY ... WITH CHECKSUM phải thành công; nên thử restore backup sang database kiểm tra độc lập với tên khác, không ghi đè AQUAR_DB hoặc staging đã tồn tại. VERIFYONLY không thay thế kiểm tra restore thực tế.
5. Trình người dùng review script tiến, kết quả staging, đường dẫn backup/verify và phương án triển khai/khôi phục. Xin xác nhận rõ ràng trước database update vào AQUAR_DB; không tự chạy Down hoặc RESTORE.
6. Sau xác nhận mới cập nhật đúng database, kiểm tra metadata và dữ liệu cũ, rồi mới khởi chạy Backend GPS mới. Lượt hiện tại chưa tới bước này.

Kết luận hiện tại: thiết kế/script tiến chỉ bổ sung và không thấy cần chỉnh migration. Chưa đủ bằng chứng tuyên bố migration đã chạy an toàn trên SQL Server thật. Đang chờ xác nhận môi trường/quyền truy cập để tiếp tục kiểm chứng staging.
