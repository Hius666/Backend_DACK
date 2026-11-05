using System.Globalization;
using MimeKit;
namespace AQUAR.Api;

public class EmailService(IConfiguration configuration, IReportEmailTransport transport, ILogger<EmailService> logger)
{
    public async Task Send(string email, string name, WorkReport report, CancellationToken cancellationToken = default)
    {
        var settings = SmtpSettings.Read(configuration);
        if (!MailboxAddress.TryParse(email, out var recipient) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(recipient.Address))
            throw new ApiException(400, "Email nhân viên không hợp lệ; cần quản trị viên kiểm tra lại.");
        using var message = new MimeMessage();
        message.From.Add(settings.From); message.To.Add(recipient);
        message.Subject = "Báo cáo chấm công AQUAR";
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        var from = report.From.AddHours(7).ToString("dd/MM/yyyy HH:mm", culture);
        var to = report.To.AddHours(7).ToString("dd/MM/yyyy HH:mm", culture);
        message.Body = new TextPart("plain") { Text = $"Nhân viên: {name}\nKỳ báo cáo: [{from}, {to}) UTC+7\nSố ca: {report.Shifts}\nGiờ làm: {report.Hours.ToString("0.##", culture)}\nLương gộp dự kiến: {report.GrossSalary.ToString("N0", culture)} VND\n{report.Note}" };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try { await transport.Send(message, settings, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(504, "Hết thời gian chờ SMTP. Chưa xác nhận được kết quả gửi; không tự động gửi lại.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Report SMTP delivery failed ({ErrorType}).", exception.GetType().Name);
            throw new ApiException(502, "SMTP không gửi được báo cáo. Liên hệ quản trị viên kiểm tra dịch vụ và cấu hình.");
        }
    }
}
