using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Billing.Email.Configuration;
using TechChallenge.Oficina.Billing.Email.Features;
using TechChallenge.Oficina.Billing.Email.Features.Orcamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Billing.Email.Tests;

public sealed class ExtensionsTests
{
    [Fact]
    public void AddInfraEmail_DeveRegistrarServicos()
    {
        var services = new ServiceCollection();
        var settings = new ResendSettings { ApiKey = "api-key", FromEmail = "onboarding@resend.dev" };

        services.AddInfraEmail(settings);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<OrcamentoEmailSender>(scope.ServiceProvider.GetService<IOrcamentoEmailSender>());
        Assert.IsType<ResendClientAdapter>(scope.ServiceProvider.GetService<IResendClient>());
        Assert.Same(settings, provider.GetRequiredService<ResendSettings>());
    }

    [Fact]
    public void AddInfraEmail_DeveRegistrarServicosQuandoConfiguracoesVazias()
    {
        var services = new ServiceCollection();
        var settings = new ResendSettings { ApiKey = string.Empty, FromEmail = string.Empty };

        services.AddInfraEmail(settings);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IOrcamentoEmailSender>());
        Assert.NotNull(scope.ServiceProvider.GetService<IResendClient>());
    }
}
