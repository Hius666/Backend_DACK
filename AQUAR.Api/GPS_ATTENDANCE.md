# GPS tùy chọn cho Check-in/Check-out — chờ xác nhận migration

Đã chuẩn bị code và migration. Chưa kết nối/cập nhật AQUAR_DB, chưa chạy database update hoặc deploy. Không sửa Flutter. Không chạy bản API mới với schema cũ: các bảng GPS phải được tạo bằng migration đã được duyệt trước khi dùng API nghiệp vụ mới.

## Request và response

Hai API giữ nguyên POST /api/attendance/check-in và POST /api/attendance/check-out, JWT và method manual/biometric. Request cũ {"method":"manual"} vẫn hợp lệ. Để gửi GPS, thêm latitude và longitude dạng số lấy tại lần bấm nút; hai trường đều tùy chọn nhưng nếu có thì phải đủ cặp.

- latitude trong [-90,90], longitude trong [-180,180], đều phải hữu hạn.
- Thiếu cả hai hoặc cả hai null: chấm công bình thường, trạng thái Unverified, reason NoGps, distanceMeters=null, không tạo cảnh báo.
- Sai tọa độ hoặc thiếu một trường: HTTP 400; không lưu một phần ca/vị trí/cảnh báo.
- Có GPS nhưng chưa cấu hình tọa độ công ty: vẫn ghi công, lưu GPS và trạng thái Unverified, reason CompanyNotConfigured, không tự giả định công ty ở (0,0).
- Đủ GPS và cấu hình: Haversine với bán kính Trái Đất trung bình 6371008.8 m; khoảng cách <= radius là InRange, lớn hơn radius là OutOfRange. So sánh giá trị đầy đủ, không làm tròn trước khi xét phạm vi.

Response giữ các trường ca hiện có và thêm checkInLocation/checkOutLocation. Mỗi đối tượng gồm latitude, longitude, recordedAtUtc, status (InRange/OutOfRange/Unverified), distanceMeters, radiusMeters, verificationReason. recordedAtUtc lấy đúng thời gian server ghi check-in hoặc check-out, không dùng giờ client; đây là thời điểm server ghi nhận, không phải chứng thực thời điểm cảm biến thu GPS.

Ca chưa ra: checkOutLocation=null. Bản ghi cũ không có dòng GPS: trả trạng thái Unverified, reason NoLocationRecord, tọa độ/khoảng cách/thời điểm vị trí null. Không tạo tọa độ hoặc backfill GPS cho dữ liệu cũ. GET /api/attendance/history cũng trả các trường vị trí này, giữ nguyên quyền đọc lịch sử hiện có.

## Cấu hình công ty

Các biến môi trường:

| Biến | Giá trị |
|---|---|
| CompanyLocation__Latitude | Vĩ độ công ty thật do bạn cung cấp; mặc định không có |
| CompanyLocation__Longitude | Kinh độ công ty thật do bạn cung cấp; mặc định không có |
| CompanyLocation__RadiusMeters | Số mét > 0, mặc định 100 |

Cũng có thể đặt section CompanyLocation với Latitude/Longitude/RadiusMeters trong cấu hình riêng của môi trường. Không có tọa độ thật nào được tự điền trong source/appsettings. Chỉ bỏ trống cả hai tọa độ là trạng thái chưa cấu hình; cấu hình sai phạm vi, chỉ có một tọa độ hoặc radius không hợp lệ sẽ báo lỗi cấu hình khi khởi động. Tọa độ (0,0) trong test là dữ liệu tổng hợp chỉ nằm trong test, không phải cấu hình công ty.

Mỗi dòng vị trí lưu snapshot tọa độ công ty/bán kính lúc tính. Thay cấu hình sau này không tính lại hoặc thay kết quả lịch sử.

## Lưu vị trí và cảnh báo

- AttendanceLocations: FK AttendanceId; Event=CheckIn/CheckOut; tọa độ, thời gian server, trạng thái, khoảng cách, snapshot cấu hình và reason. Unique index (AttendanceId,Event) ngăn nhiều dòng GPS cùng một lần vào/ra.
- AttendanceLocationAlerts: FK duy nhất đến dòng vị trí ngoài phạm vi, Kind=OutsideCompanyRadius và CreatedAtUtc. Một ca có thể có hai cảnh báo nếu cả vào và ra đều ngoài phạm vi.
- Lưu ca + vị trí + cảnh báo trong cùng transaction Serializable hiện có. Ngoài phạm vi chỉ tạo cảnh báo, không từ chối hoặc hủy ca. Các kiểm tra JWT, vai trò, bắt buộc đổi mật khẩu, ca trùng và filtered index ca đang mở vẫn giữ nguyên.
- Admin có thể đọc dữ liệu cảnh báo qua chức năng sẽ xây dựng sau; chưa thêm API/danh sách Admin, gửi email hoặc push notification.
- Endpoint quản lý nhập hộ /api/attendance/manual không nhận GPS giả cho ca nhập lại; không đổi hành vi của endpoint này.

Các tọa độ là dữ liệu client khai báo. InRange chỉ nghĩa là tọa độ đó nằm trong bán kính tính toán, không chứng minh vị trí thiết bị thật. Chưa có accuracy, timestamp cảm biến, chống giả mạo hoặc giám sát GPS liên tục. Không bật logging nội dung GPS; cần quyết định quyền truy cập, thời hạn lưu và chính sách dữ liệu vị trí trước khi triển khai sử dụng thật.

## Migration và dữ liệu cũ

Migration: 20261105080604_AddAttendanceLocations, EF CLI 8.0.11. Có file SQL AddAttendanceLocations.sql để review.

Up chỉ CREATE hai bảng và index/khóa ngoại; không ALTER/xóa bảng Attendances, Staff hoặc đổi bản ghi cũ. Khóa ngoại dùng Restrict. EF history được thêm một dòng khi áp dụng. Down là rollback xóa hai bảng GPS mới; không chạy trong đợt này.

Đã kiểm tra các UpOperations chỉ là CreateTable/CreateIndex. Đã thực thi các forward operations với SQLite SQL generator trên database SQLite trong RAM có sẵn bản ghi ca cũ, xác nhận ca cũ giữ nguyên và không có GPS backfill. Các API GPS cũng được chạy bằng pipeline HTTP/JWT thật với SQLite trong RAM. Đây không phải kiểm chứng DDL/identity/concurrency native SQL Server; vẫn cần staging SQL Server riêng trước production.

## Kết quả kiểm tra

- Backend build configuration GpsChecks: 0 lỗi, 1 cảnh báo NU1900 do không tải được dữ liệu audit NuGet.
- 121 checks đạt, exit code 0, log cục bộ .verification/gps-checks.log; không kết nối hoặc ghi SQL Server thật.
- Bao gồm trong/ngoài 100 m, thiếu GPS/cấu hình công ty, tọa độ sai/NaN/Infinity/thiếu cặp, Haversine qua kinh tuyến 180 độ, GPS tách biệt lúc vào/ra, thời gian server, cảnh báo ngoài phạm vi, trùng ca, quyền truy cập và bảo toàn ca cũ.
- EF xác nhận model khớp migration mới, không còn thay đổi model chưa được đưa vào migration.
- Chưa chạy Flutter, GPS Android thật hoặc SQL Server integration trong đợt này.

Chạy lại checks tạm không dùng SQL Server:

```powershell
cd D:\LTDD_FINAL
dotnet build .\AQUAR.Backend.Checks --configuration GpsChecks --no-restore
dotnet .\AQUAR.Backend.Checks\bin\GpsChecks\net8.0\AQUAR.Backend.Checks.dll
```

## Trước khi áp dụng thật

1. Bạn review/duyệt hai bảng, kiểu dữ liệu, trạng thái, chính sách quyền và lưu GPS.
2. Kiểm tra các migration đã có trên SQL Server, backup AQUAR_DB và xác minh backup thành công; chưa biết trạng thái thực tế của các migration cũ từ lượt này.
3. Thử script EF trên SQL Server staging/tạm cùng phiên bản, kiểm tra khóa/index/identity và so sánh dữ liệu trước/sau.
4. Bạn cung cấp tọa độ công ty thật; nếu chưa có, giữ trống và hệ thống ghi Unverified.
5. Chỉ khi được xác nhận mới áp dụng AddAttendanceLocations lên đúng AQUAR_DB, rồi triển khai Backend mới và kiểm tra bằng tài khoản/môi trường được phép. Chưa có lệnh tự chạy migration lúc startup.

Đang dừng chờ xác nhận; không tự chạy database update trên AQUAR_DB.
