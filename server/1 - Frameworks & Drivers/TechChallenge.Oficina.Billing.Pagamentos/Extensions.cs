using MercadoPago.Http;
using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.Pagamentos.Features;
using TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.Pagamentos;

public static class Extensions
{
    private static readonly TimeSpan TempoLimiteRequisicao = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddInfraPagamentos(this IServiceCollection services, MercadoPagoSettings mercadoPagoSettings)
    {
        services.AddSingleton(mercadoPagoSettings);

        // Sem access token (ambiente local ou demonstracao): cobranca simulada, aprovada na primeira consulta
        if (string.IsNullOrWhiteSpace(mercadoPagoSettings.AccessToken))
        {
            services.AddSingleton<IGatewayPagamento, GatewayPagamentoSimulado>();
            return services;
        }

        services.AddSingleton<IMercadoPagoClient>(_ => new MercadoPagoClientAdapter(
            mercadoPagoSettings,
            new DefaultHttpClient(new HttpClient { Timeout = TempoLimiteRequisicao })));
        services.AddSingleton<IGatewayPagamento, GatewayPagamentoMercadoPago>();

        return services;
    }
}
