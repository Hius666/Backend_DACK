using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace AQUAR.Api;

public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public StrongPasswordAttribute() => ErrorMessage = "Mật khẩu cần ít nhất 12 ký tự, tối đa 72 byte UTF-8 và không chứa ký tự NUL.";
    public override bool IsValid(object? value) => value is string password &&
        password.Length >= 12 && Encoding.UTF8.GetByteCount(password) <= 72 && !password.Contains('\0');
}

public class PasswordService
{
    private readonly string dummyHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), 12);
    public string Hash(string password)
    {
        if (!new StrongPasswordAttribute().IsValid(password)) throw new ApiException(400, new StrongPasswordAttribute().ErrorMessage!);
        return BCrypt.Net.BCrypt.HashPassword(password, 12);
    }
    public bool Verify(string password, string? hash)
    {
        if (Encoding.UTF8.GetByteCount(password) > 72 || password.Contains('\0')) return false;
        try { return BCrypt.Net.BCrypt.Verify(password, hash ?? dummyHash) && hash != null; }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
    public static string Stamp(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));
}
