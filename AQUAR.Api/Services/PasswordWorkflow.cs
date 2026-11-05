namespace AQUAR.Api;

public sealed class PasswordWorkflow(PasswordService passwords)
{
    public void Change(Staff staff, PasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
            throw new ApiException(400, "Xác nhận mật khẩu mới không khớp.");
        if (!passwords.Verify(request.CurrentPassword, staff.PasswordHash))
            throw new ApiException(400, "Mật khẩu hiện tại không đúng.");
        if (passwords.Verify(request.NewPassword, staff.PasswordHash))
            throw new ApiException(400, "Mật khẩu mới phải khác mật khẩu hiện tại.");
        var hash = passwords.Hash(request.NewPassword);
        staff.PasswordHash = hash;
        staff.MustChangePassword = false;
        RevokeAllTokens(staff);
    }

    public void Reset(Staff staff, string temporaryPassword)
    {
        if (passwords.Verify(temporaryPassword, staff.PasswordHash))
            throw new ApiException(400, "Mật khẩu tạm thời phải khác mật khẩu hiện tại.");
        staff.PasswordHash = passwords.Hash(temporaryPassword);
        staff.MustChangePassword = true;
        RevokeAllTokens(staff);
    }

    public static void RevokeAllTokens(Staff staff) => staff.TokenVersion = checked(staff.TokenVersion + 1);
}
