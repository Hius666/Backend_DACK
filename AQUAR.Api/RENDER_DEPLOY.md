# AQUAR API .NET 8 — chuẩn bị deploy Render bằng Docker

Đợt này chỉ chuẩn bị source/cấu hình. Không tạo dịch vụ Render, chọn gói trả phí, deploy, chạy migration, sửa database hoặc Flutter.

## 1. Image và build context

Dockerfile dùng sdk:8.0 để restore/publish Release và aspnet:8.0 để chạy. Runtime chỉ chứa output publish, chạy user app không phải root. Không dùng build ARG hoặc ENV để ghi secret vào layer.

Build context phải là thư mục AQUAR.Api. .dockerignore loại bin/obj, appsettings*.json, .env, secret files, chứng chỉ/private keys, backup/database files, scripts và tài liệu. Cấu hình Windows local trong appsettings.json không được đưa vào image. Không build với context toàn bộ repo và một Dockerfile Path khác mà chưa điều chỉnh COPY/.dockerignore.

Từ D:\LTDD_FINAL trên máy đã chạy Docker Desktop Linux engine:

```powershell
docker build -t aquar-api:render-check .\AQUAR.Api
docker run --rm -p 8080:8080 --env-file <file-env-cuc-bo-khong-commit> aquar-api:render-check
```

Không truyền giá trị mật khẩu vào câu lệnh hoặc commit file env. File env phải có đủ các biến bắt buộc bên dưới; PORT=8080 chỉ là giá trị thử cục bộ. Sau khi container chạy, GET http://127.0.0.1:8080/health phải trả 200 và {"status":"ok"}. Liveness vẫn có thể OK khi SQL không truy cập được: đây không phải kiểm tra database hoặc toàn bộ nghiệp vụ.

Docker có metadata EXPOSE 8080 để thuận tiện chạy local, không cố định cổng production. Khi có PORT, Program.cs bind http://0.0.0.0:{PORT}; sai định dạng hoặc ngoài 1..65535 sẽ dừng khởi động. Không có PORT thì giữ cơ chế bind mặc định của ASP.NET Core (base image .NET 8 sử dụng cổng HTTP 8080).

## 2. Biến môi trường Render

Thiết lập runtime secrets trong Render Dashboard. ASP.NET Core ánh xạ __ thành dấu : trong cấu hình; không thêm secret vào Dockerfile hoặc source.

| Biến | Bắt buộc | Nội dung |
|---|---|---|
| ASPNETCORE_ENVIRONMENT | Có | Production (Dockerfile đã đặt mặc định) |
| ConnectionStrings__Default | Có, secret | Kết nối SQL Server trung tâm có thể truy cập an toàn; đúng database và tài khoản ứng dụng |
| Jwt__Key | Có, secret | Chuỗi ngẫu nhiên mạnh ít nhất 32 byte; tạo riêng ngoài source, lưu secret store |
| Jwt__Issuer | Có | Issuer ổn định của AQUAR API |
| Jwt__Audience | Có | Audience ổn định của client AQUAR |
| PORT | Render cung cấp | Giữ giá trị runtime do Render cung cấp; không đặt ASPNETCORE_URLS cố định để thay thế |
| Cors__AllowedOrigins__0 | Nếu có frontend web | Origin HTTPS được phép, không có dấu / cuối hoặc path |
| Cors__AllowedOrigins__1 | Tùy chọn | Origin hợp lệ tiếp theo; thêm các chỉ số liên tục nếu cần |
| Swagger__Enabled | Tùy chọn | false mặc định trong Production; chỉ bật true khi chủ động cần kiểm thử, rồi tắt |
| Smtp__Host | Khi gửi email | Máy chủ SMTP |
| Smtp__Port | Khi gửi email | Cổng STARTTLS của nhà cung cấp (code mặc định 587) |
| Smtp__From | Khi gửi email | Địa chỉ gửi được phép |
| Smtp__User | Khi SMTP cần đăng nhập | Tài khoản gửi |
| Smtp__Password | Khi SMTP cần đăng nhập, secret | Mật khẩu/app password do nhà cung cấp cấp |

Mẫu cấu trúc connection string, phải thay các placeholder trong Dashboard, không dùng chuỗi này làm cấu hình thật:

```text
Server=tcp:<sql-host>,<tcp-port>;Database=<database>;User ID=<app-login>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;
```

Không dùng Trusted_Connection=True / Integrated Security=True từ Windows local. Production hiện từ chối Windows Authentication, connection string không có server/database, tắt encryption, bỏ kiểm tra chứng chỉ SQL và JWT key rỗng/ngắn/placeholder CHANGE hoặc REPLACE. Issuer/audience cũng phải được cấu hình. SQL Server phải có certificate hợp lệ, hostname khớp và chain được container tin cậy; không sửa thành TrustServerCertificate=True để che lỗi production.

CORS mặc định không cấp quyền cho origin nào, chỉ chấp nhận danh sách cụ thể; không dùng wildcard hoặc AllowCredentials. Native Flutter Android/iOS không chịu cơ chế CORS trình duyệt. Flutter Web cần origin nằm trong danh sách này. CORS không thay thế JWT/phân quyền, và không ngăn client ngoài trình duyệt gửi request.

## 3. SQL Server dùng chung — điều kiện phải giải quyết trước deploy nghiệp vụ

TUAN\SQLEXPRESS là instance Windows local, không phải địa chỉ mà container Render tự truy cập được. Không mở port SQL Server máy cá nhân, cấu hình router port-forward, hoặc công khai SQL Browser ra Internet.

Cần chọn một phương án được phê duyệt riêng:

1. SQL Server/SQL Server-compatible managed endpoint được quản lý phù hợp (không phải PostgreSQL), với TLS, tài khoản ứng dụng quyền tối thiểu và chính sách mạng giới hạn phù hợp; hoặc
2. Kết nối mạng riêng/VPN được thiết kế và kiểm chứng từ môi trường chạy container tới SQL Server hiện có. Repo này chưa cung cấp hạ tầng đó; không giả định Render private network tự nối được vào mạng gia đình.

Muốn Backend local và Render dùng chung dữ liệu thì cả hai phải trỏ cùng một SQL endpoint/database trung tâm bằng cấu hình phù hợp. Không có cơ chế đồng bộ hai database tự động trong đợt này. Việc chuyển dữ liệu/schema từ AQUAR_DB local phải được lập kế hoạch, backup, xác nhận và thực hiện ở một bước riêng. Không tự thay đổi database hoặc chạy migration khi khởi động container.

Tài khoản runtime chỉ nên có quyền nghiệp vụ cần thiết; quyền tạo/sửa schema dành cho tài khoản triển khai riêng. Trước khi chạy login/password workflow, xác minh schema đích đã có InitialCreate và RequirePasswordChange; đợt này không xác nhận hoặc áp dụng chúng trên SQL Server thật.

## 4. Cấu hình dịch vụ trên Render khi bạn sẵn sàng

1. Kiểm tra repo không chứa secret thật; .dockerignore chỉ loại khỏi image/build context, không tự xóa secret khỏi lịch sử Git. Đưa code lên repo do bạn quản lý.
2. Tự tạo Web Service và chọn Language/Runtime Docker. Root Directory: AQUAR.Api. Dockerfile Path: ./Dockerfile; Docker build context là thư mục hiện tại sau khi đặt Root Directory.
3. Chọn region/plan sau khi xem điều kiện và chi phí; đợt này không chọn gói hoặc tạo dịch vụ thay bạn. Có thể tắt auto-deploy trong giai đoạn chuẩn bị.
4. Nhập các biến runtime ở trên; không đặt Docker Command/pre-deploy command để chạy migration. Giữ ENTRYPOINT mặc định của image.
5. Health Check Path: /health. API không bật HTTPS listener bên trong container; TLS public được xử lý ở lớp dịch vụ Render.
6. Khi bạn chủ động deploy: kiểm tra log bind đúng PORT, HTTPS /health trả 200. Sau đó xác minh kết nối SQL và đăng nhập/phân quyền bằng tài khoản được phép, không tạo Admin/mật khẩu mẫu.
7. Nếu tạm bật Swagger để thử: dùng HTTPS /swagger và tắt lại sau kiểm tra. Swagger mặc định không công khai ở Production.
8. Rate limit login hiện theo IP nhìn thấy bởi API. Trước khi phục vụ nhiều người, kiểm tra proxy thực tế; chưa cấu hình trusted forwarded headers trong đợt này, nên nhiều client có thể bị tính chung bucket 10 lần/phút. Không bật tin cậy mọi X-Forwarded-For chỉ để đổi IP.
9. Chọn SQL endpoint và SMTP/provider có kết nối thực tế phù hợp với môi trường/plan trước khi tuyên bố các chức năng phụ thuộc hoạt động online. Chưa kiểm thử SMTP trên Render.

Với local Windows Authentication, chạy môi trường Development rõ ràng; Production mới có kiểm tra cấu hình chặt hơn. Không mang các giá trị dev/placeholder lên Render.

## 5. Xác minh đã thực hiện trong đợt này

- Build API và Checks với configuration Deployment: 0 lỗi, 0 cảnh báo. Configuration Verification đang bị một tiến trình khác giữ AQUAR.Api.exe; không dừng tiến trình đó.
- 49 kiểm tra logic/password trước đây và 19 kiểm tra triển khai mới đều đạt (68 tổng cộng), exit code 0. Kiểm tra mới có HTTP TestServer thật trong Production: health anonymous không cần SQL, Swagger mặc định tắt, CORS preflight cho phép/từ chối đúng origin và API nghiệp vụ vẫn yêu cầu JWT.
- Không chạy --integration, vì bộ đó ghi fixture vào SQL rồi rollback; đợt này không tác động database. Không chạy EF database update/migration.
- Docker CLI có sẵn, nhưng Engine không truy cập được: named pipe dockerDesktopLinuxEngine không tồn tại. docker build còn bị Access is denied tại C:\Users\Tuan\.docker\buildx\.lock. Chưa build hoặc chạy image thành công; cần xác minh lại trên máy có Docker Engine đang chạy và quyền Docker phù hợp.
- Chưa deploy Render, chưa thiết lập SQL Server trung tâm/mạng riêng, chưa kiểm thử kết nối online hoặc sửa Flutter.

Lệnh chạy lại checks không dùng SQL:

```powershell
cd D:\LTDD_FINAL
dotnet build .\AQUAR.Backend.Checks\AQUAR.Backend.Checks.csproj --configuration Deployment --no-restore
dotnet .\AQUAR.Backend.Checks\bin\Deployment\net8.0\AQUAR.Backend.Checks.dll
```

## Tài liệu chính thức đã đối chiếu

- https://render.com/docs/docker
- https://render.com/docs/web-services
- https://render.com/docs/health-checks
- https://render.com/docs/environment-variables
- https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/containers
