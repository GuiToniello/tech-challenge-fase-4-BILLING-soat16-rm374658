using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

namespace TechChallenge.Oficina.Billing.DB.Data;

public static class Extensions
{
    public static IServiceCollection AddInfraData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BillingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks().AddDbContextCheck<BillingDbContext>("postgres", tags: ["ready"]);
        services.AddScoped<IOrcamentoGateway, OrcamentoGateway>();
        services.AddScoped<IOrdemServicoReferenciaGateway, OrdemServicoReferenciaGateway>();
        services.AddScoped<IPagamentoGateway, PagamentoGateway>();
        return services;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        dbContext.Database.Migrate();
    }
}
