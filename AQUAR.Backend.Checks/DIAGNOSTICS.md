# Kết quả chẩn đoán kiểm thử

Ngày kiểm tra: 2026-11-05.

## Trạng thái ban đầu

- Ba phiên terminal cũ đã kết thúc; phiên 11790 trả exit code 1, hai phiên 15839 và 16038 không còn tồn tại trong session registry.
- Danh sách tiến trình Windows không có AQUAR.Backend.Checks hoặc dotnet bị treo. Không dừng tiến trình nào.
- SDK duy nhất: 10.0.300. TargetFramework của cả hai project: net8.0. Không khai báo RuntimeFrameworkVersion hoặc RollForward riêng.
- Có Microsoft.NETCore.App 8.0.6/8.0.11 nhưng thiếu Microsoft.AspNetCore.App 8.x. Có ASP.NET Core 9.0.16/10.0.8; chúng không tự thay thế major version 8.
- Chạy EXE trước khi sửa trả thông báo: `You must install or update .NET to run this application. Framework: 'Microsoft.AspNetCore.App', version '8.0.0' (x64)`.

## Phân biệt hai lỗi

Thiếu framework xảy ra trước khi code kiểm thử được thực thi, không có managed stack trace. Mã Windows 0xe0434352 riêng lẻ không xác định exception cụ thể. Các truy vấn Application/.NET Runtime log đã thực hiện không tìm được bản ghi AQUAR tương ứng để gắn chắc chắn mã này với một sự kiện cụ thể.

Output terminal được ghi nhận ở lượt phát triển trước từng có exception chưa xử lý:

```text
Unhandled exception. System.Exception: Invalid staff DTO rejected
   at Program.<<Main>$>g__Check|0_0(Boolean value, String name) in D:\LTDD_FINAL\AQUAR.Backend.Checks\Program.cs:line 7
   at Program.<Main>$(String[] args) in D:\LTDD_FINAL\AQUAR.Backend.Checks\Program.cs:line 27
```

Nguyên nhân assertion trước đó: Validator.TryValidateObject đọc metadata thuộc tính trong khi MVC dùng annotation trên tham số constructor của record DTO. Source hiện tại đã kiểm tra đúng metadata constructor; không giảm số assertion hoặc bỏ qua validation. Đoạn stack trên là lịch sử, không phải lỗi tái hiện trên source hiện tại.

## Sửa và xác minh

- Cài bộ runtime x64 chính thức Microsoft: Microsoft.AspNetCore.App 8.0.33 và Microsoft.NETCore.App 8.0.33. Các bộ cài đã được đối chiếu SHA512 từ release metadata Microsoft. Cả hai bộ cài exit code 0.
- Cần cả hai runtime: sau khi chỉ cài ASP.NET Core 8.0.33, host báo thiếu Microsoft.NETCore.App 8.0.33 vì runtime Core 8.0.11 quá cũ so với dependency của ASP.NET Core mới.
- Giữ net8.0, không ép RuntimeFrameworkVersion xuống thấp hoặc bật roll-forward major.
- Thêm catch ngoài cùng: in exception.ToString() và return 1 để lỗi kiểm thử không trở thành exception chưa xử lý/Windows Application Error. Assertion vẫn làm thất bại toàn bộ runner.
- `dotnet build AQUAR.Backend.Checks\AQUAR.Backend.Checks.csproj --no-restore`: build cả API và Checks, 0 warnings, 0 errors.
- Chạy EXE trực tiếp: 10 PASS, exit code 0.
- `dotnet run --project AQUAR.Backend.Checks --no-build --no-restore`: 10 PASS, exit code 0.
- Đã bỏ biến DOTNET_ROLL_FORWARD khỏi process PowerShell kiểm tra trước khi chạy.

Không kết nối hoặc sửa SQL Server, database, migration, dữ liệu hoặc Flutter. Các checks là kiểm tra logic không dùng database, chưa phải kiểm thử tích hợp SQL Server/SMTP.
