using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Billing.Email.Configuration;
using TechChallenge.Oficina.Billing.Email.Features;
using TechChallenge.Oficina.Billing.Email.Features.Orcamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;

namespace TechChallenge.Oficina.Billing.Email;

public static class Extensions
{
    public static IServiceCollection AddInfraEmail(this IServiceCollection services, ResendSettings resendSettings)
    {
        services.AddSingleton(resendSettings);
        services.AddSingleton<Resend.IResend>(_ => Resend.ResendClient.Create(
            string.IsNullOrWhiteSpace(resendSettings.ApiKey) ? "dummy" : resendSettings.ApiKey));
        services.AddScoped<IResendClient, ResendClientAdapter>();
        services.AddScoped<IOrcamentoEmailSender, OrcamentoEmailSender>();

        return services;
    }
}
