using System.Net;
using System.Net.Mail;
using System.Text;
namespace AdminPanel.API.Services;

public interface IPasswordResetEmail
{
    Task Send(string email, string token, string culture, CancellationToken ct);
}

public sealed class PasswordResetEmail(IConfiguration config, IWebHostEnvironment environment) : IPasswordResetEmail
{
    public async Task Send(string email, string token, string culture, CancellationToken ct)
    {
        var baseUrl = config["CustomerEmail:StorefrontUrl"] ?? "https://localhost:7289";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new InvalidOperationException("Configure an HTTPS CustomerEmail:StorefrontUrl.");
        // Fragment data never reaches HTTP access logs. The reset form also supports manual code entry.
        var link = baseUrl.TrimEnd('/') + "/Account/ResetPassword#code=" + Uri.EscapeDataString(token) +
            "&email=" + Uri.EscapeDataString(email);
        var subject = culture == "ar" ? "إعادة تعيين كلمة المرور" : "Reset your password";
        var text = culture == "ar"
            ? $"لإعادة تعيين كلمة المرور خلال 30 دقيقة، افتح الرابط:\n{link}\n\nالبريد: {email}\nالرمز: {token}\nإذا لم تطلب ذلك، تجاهل الرسالة."
            : $"To reset your password within 30 minutes, open:\n{link}\n\nEmail: {email}\nCode: {token}\nIf you did not request this, ignore this email.";
        var provider = config["CustomerEmail:Provider"] ?? (environment.IsDevelopment() ? "DevelopmentFile" : "Disabled");
        if (provider == "DevelopmentFile" && environment.IsDevelopment())
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdminPanel", "DevelopmentMail");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".eml"),
                $"To: {email}\nSubject: {subject}\n\n{text}", Encoding.UTF8, ct);
            return;
        }
        if (provider != "Smtp") throw new InvalidOperationException("Configure customer email delivery before production use.");
        var host = config["CustomerEmail:Smtp:Host"];
        var from = config["CustomerEmail:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Configure the SMTP host and sender.");
        using var message = new MailMessage(from, email, subject, text) { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        using var smtp = new SmtpClient(host, config.GetValue("CustomerEmail:Smtp:Port", 587))
        {
            EnableSsl = true, UseDefaultCredentials = false,
            Credentials = new NetworkCredential(config["CustomerEmail:Smtp:Username"], config["CustomerEmail:Smtp:Password"])
        };
        await smtp.SendMailAsync(message, ct);
    }
}

