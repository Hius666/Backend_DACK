using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AQUAR.Api;

public class AdminBootstrapService(AppDb db, PasswordService passwords)
{
    public async Task<Staff> Create(string email, string name, string password)
    {
        email = email.Trim(); name = name.Trim();
        if (!new EmailAddressAttribute().IsValid(email) || email.Length > 254 || name.Length is < 1 or > 200)
            throw new ApiException(400, "Email hoặc tên Admin không hợp lệ.");
        var hash = passwords.Hash(password);
        // Use a transaction-owned SQL Server lock to serialize bootstrap attempts across processes.
        var ownsTransaction = db.Database.CurrentTransaction == null;
        var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'AQUAR.FirstAdmin', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Cannot acquire Admin bootstrap lock', 1;");
            if (await db.Staff.AnyAsync(s => s.Role == UserRole.Admin))
                throw new ApiException(409, "Đã có Admin. Không được khởi tạo lại, kể cả khi Admin bị khóa.");
            if (await db.Staff.AnyAsync(s => s.Email == email))
                throw new ApiException(409, "Email đã tồn tại. Không thay đổi tài khoản hiện có.");
            var admin = new Staff { Name = name, Email = email, PasswordHash = hash, Role = UserRole.Admin, Active = true };
            db.Staff.Add(admin);
            await db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return admin;
        }
        finally { if (transaction != null) await transaction.DisposeAsync(); }
    }

    public static async Task<int> RunConsole(IServiceProvider services)
    {
        try
        {
            if (Console.IsInputRedirected) throw new ApiException(400, "Hãy chạy trong terminal tương tác; không truyền mật khẩu bằng tham số hoặc pipe.");
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            Console.WriteLine($"Khởi tạo Admin đầu tiên: {db.Database.GetDbConnection().DataSource} / {db.Database.GetDbConnection().Database}");
            Console.Write("Email: "); var email = Console.ReadLine() ?? "";
            Console.Write("Họ tên: "); var name = Console.ReadLine() ?? "";
            Console.Write("Mật khẩu: "); var password = ReadPassword();
            Console.Write("Nhập lại mật khẩu: "); var confirmation = ReadPassword();
            if (password != confirmation) throw new ApiException(400, "Hai mật khẩu không khớp.");
            await scope.ServiceProvider.GetRequiredService<AdminBootstrapService>().Create(email, name, password);
            Console.WriteLine("Đã tạo Admin. Không hiển thị hoặc lưu mật khẩu dạng rõ.");
            return 0;
        }
        catch (ApiException e) { Console.Error.WriteLine(e.Message); return 1; }
        catch (Exception) { Console.Error.WriteLine("Không khởi tạo được Admin. Kiểm tra kết nối SQL Server và migration."); return 1; }
    }

    private static string ReadPassword()
    {
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new ApiException(400, "Đã hủy khởi tạo.");
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
            else if (!char.IsControl(key.KeyChar) && buffer.Length < 256) buffer.Append(key.KeyChar);
        }
    }
}
