using Resend;

namespace TechChallenge.Oficina.Billing.Email.Features;

public interface IResendClient
{
    Task SendEmailAsync(EmailMessage emailMessage, CancellationToken cancellationToken = default);
}
