# Chuẩn hóa phương thức chấm công thông thường

POST /api/attendance/check-in và /api/attendance/check-out nhận JSON {"method":"manual"}. Hai endpoint giữ JWT/authorization hiện có, thời gian server UTC, transaction Serializable, kiểm tra ca đang mở và filtered unique index chống ca trùng. Không thay đổi cấu trúc xác thực hoặc database.

Giá trị chấp nhận: manual (thông thường) và biometric (tương thích client cũ). Phân biệt hoa thường; các chuỗi khác/rỗng bị trả 400. biometric chỉ là nhãn cũ do client gửi, không phải chứng cứ xác thực thiết bị. Không gọi vân tay/khuôn mặt/GPS/NFC hoặc cấp quyền khác theo nhãn method.

Ý nghĩa hai trường:

- method="manual", manual=false: nhân viên tự Check-in/out bằng tài khoản/JWT, giờ do server quyết định.
- method="manual", manual=true: ca do Admin/Manager nhập qua POST /api/attendance/manual, có recordedBy và reason. Employee không được gọi endpoint nhập hộ này.
- method="biometric": giữ nguyên các bản ghi/client cũ; không tự đổi dữ liệu thật sang manual.

Check-out đóng ca đang mở và giữ Method ban đầu của ca, kể cả client mới gửi manual để đóng ca cũ biometric. Schema hiện chỉ có một Method cho cả ca, chưa có phương thức riêng cho từng lần vào/ra. Không thay đổi ý nghĩa cờ Manual hoặc viết lại bản ghi trước đây.

## Migration

Không cần migration: Attendance.Method đã là chuỗi không có enum/check constraint giới hạn giá trị; không sửa model/DbContext/schema. `dotnet ef migrations has-pending-model-changes` với EF CLI 8.0.11 xác nhận model không đổi so với migration gần nhất. Chưa chạy database update hoặc migration; không thay đổi AQUAR_DB hoặc dữ liệu hiện có.

## Xác minh

- Backend build cấu hình ManualChecks: 0 errors, 1 warning NU1900 vì không tải được dữ liệu vulnerability audit NuGet. Không tắt audit để che cảnh báo.
- 89 checks đạt, exit code 0: 68 checks hiện có và 21 kiểm tra HTTP/JWT chấm công mới. Test mới dùng pipeline thật và SQLite trong RAM, không SQL Server. Kiểm tra manual vào/ra, dữ liệu trả về/lịch sử, ca trùng, phương thức sai, JWT, quyền nhân viên, bắt buộc đổi mật khẩu/tài khoản bị khóa, tương thích biometric cũ.
- Provider SQLite chỉ thêm vào project Checks (PrivateAssets=all), dùng bản 8.0.2 sẵn trong cache; API production vẫn EF Core SQL Server 8.0.11. Không chuyển database ứng dụng sang SQLite/PostgreSQL. Test SQLite không thay thế kiểm thử concurrency/TLS SQL Server thật.
- Không chạy bộ --integration trỏ AQUAR_DB, không sửa dữ liệu thật. EnsureCreated trong AttendanceMethodChecks chỉ tạo bảng ở connection SQLite Data Source=:memory:.
- Flutter đổi payload sang manual, giữ kiểm thử JWT/luồng cũ; thêm kiểm tra nhận manual, cờ manual=false và vẫn đọc đúng lịch sử biometric/manager correction. Tổng số test Flutter dự kiến 32 sau khi thêm một trường hợp, chưa xác nhận PASS trên source mới.
- Đã thử flutter pub get/analyze/test, wrapper không vượt qua bước khởi động SDK ngoài workspace; đã dừng các phiên tự khởi chạy. Dart analyze báo CreateFile failed 5 / Access is denied khi khởi chạy analysis server. Dart format --output=none lib test thành công, không lỗi parse; không coi đây là analyze/test PASS.

Chạy lại Backend không SQL Server:

```powershell
cd D:\LTDD_FINAL
dotnet build .\AQUAR.Backend.Checks --configuration ManualChecks --no-restore
dotnet .\AQUAR.Backend.Checks\bin\ManualChecks\net8.0\AQUAR.Backend.Checks.dll
```

Chạy lại Flutter trên PowerShell có quyền SDK:

```powershell
cd D:\LTDD_FINAL\AQUAR_Flutter_Complete\aquar_app
$flutterCmd = 'D:\LTDD\flutter\flutter\bin\flutter.bat'
& $flutterCmd pub get
& $flutterCmd analyze
& $flutterCmd test
```

Cần cập nhật Backend trước khi dùng Flutter mới: Backend cũ sẽ từ chối manual với 400. Không cần migration mới cho thay đổi này. Tình trạng các migration cũ trên SQL Server thật không được xác minh trong đợt này. Không deploy Render hoặc sửa giao diện ngoài nội dung giải thích phương thức liên quan.
