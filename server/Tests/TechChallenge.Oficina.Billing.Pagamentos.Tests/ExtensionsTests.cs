using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.Pagamentos.Features;
using TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Billing.Pagamentos.Tests;

public sealed class ExtensionsTests
{
    private static ServiceProvider CriarProvider(MercadoPagoSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfraPagamentos(settings);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfraPagamentos_DeveUsarGatewaySimulado_QuandoAccessTokenNaoConfigurado(string accessToken)
    {
        using var provider = CriarProvider(new MercadoPagoSettings { AccessToken = accessToken });

        Assert.IsType<GatewayPagamentoSimulado>(provider.GetRequiredService<IGatewayPagamento>());
        Assert.Null(provider.GetService<IMercadoPagoClient>());
    }

    [Fact]
    public void AddInfraPagamentos_DeveUsarMercadoPago_QuandoAccessTokenConfigurado()
    {
        var settings = new MercadoPagoSettings { AccessToken = "TEST-token", NotificationUrl = "https://billing.exemplo.com/api/pagamentos/notificacoes" };

        using var provider = CriarProvider(settings);

        Assert.IsType<GatewayPagamentoMercadoPago>(provider.GetRequiredService<IGatewayPagamento>());
        Assert.IsType<MercadoPagoClientAdapter>(provider.GetRequiredService<IMercadoPagoClient>());
        Assert.Same(settings, provider.GetRequiredService<MercadoPagoSettings>());
    }
}
