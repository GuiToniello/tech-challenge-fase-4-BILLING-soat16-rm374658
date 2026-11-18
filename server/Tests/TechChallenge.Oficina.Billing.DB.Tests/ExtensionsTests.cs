using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Billing.DB.Data.Tests;

public sealed class ExtensionsTests
{
    private const string ConnectionString = "Host=localhost;Port=5434;Database=oficina_billing;Username=sa;Password=senha";

    [Fact]
    public void AddInfraData_DeveRegistrarGatewaysEHealthCheckDoBanco()
    {
        var services = new ServiceCollection();

        services.AddInfraData(ConnectionString);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<OrcamentoGateway>(scope.ServiceProvider.GetRequiredService<IOrcamentoGateway>());
        Assert.IsType<OrdemServicoReferenciaGateway>(scope.ServiceProvider.GetRequiredService<IOrdemServicoReferenciaGateway>());
        Assert.IsType<PagamentoGateway>(scope.ServiceProvider.GetRequiredService<IPagamentoGateway>());

        var healthChecks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var postgres = Assert.Single(healthChecks, registro => registro.Name == "postgres");
        Assert.Contains("ready", postgres.Tags);
    }

    [Fact]
    public void Modelo_DeveMapearTabelasEColunasNoPostgres()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(ConnectionString).Options;
        using var context = new BillingDbContext(options);

        var referencia = context.Model.FindEntityType(typeof(OrdemServicoReferencia))!;
        Assert.Equal("ordens_servico_referencias", referencia.GetTableName());
        Assert.Equal("id", referencia.FindProperty(nameof(OrdemServicoReferencia.Id))!.GetColumnName());
        Assert.Equal("cliente_email", referencia.FindProperty(nameof(OrdemServicoReferencia.ClienteEmail))!.GetColumnName());
        Assert.Equal("estoque_suficiente", referencia.FindProperty(nameof(OrdemServicoReferencia.EstoqueSuficiente))!.GetColumnName());
        Assert.Equal("possui_dados", referencia.FindProperty(nameof(OrdemServicoReferencia.PossuiDadosParaOrcamento))!.GetColumnName());
        Assert.Equal("numeric(18,2)", referencia.FindProperty(nameof(OrdemServicoReferencia.ValorTotal))!.GetColumnType());
        var servicosReferencia = referencia.FindNavigation(nameof(OrdemServicoReferencia.Servicos))!.TargetEntityType;
        Assert.Equal("ordens_servico_referencias_servicos", servicosReferencia.GetTableName());

        var orcamento = context.Model.FindEntityType(typeof(Orcamento))!;
        Assert.Equal("orcamentos", orcamento.GetTableName());
        Assert.Equal("ordem_servico_id", orcamento.FindProperty(nameof(Orcamento.Id))!.GetColumnName());
        Assert.True(orcamento.FindProperty(nameof(Orcamento.Status))!.IsConcurrencyToken);
        Assert.Equal("orcamentos_servicos", orcamento.FindNavigation(nameof(Orcamento.Servicos))!.TargetEntityType.GetTableName());

        var pagamento = context.Model.FindEntityType(typeof(Pagamento))!;
        Assert.Equal("pagamentos", pagamento.GetTableName());
        Assert.Equal("ordem_servico_id", pagamento.FindProperty(nameof(Pagamento.OrdemServicoId))!.GetColumnName());
        Assert.Equal("numeric(18,2)", pagamento.FindProperty(nameof(Pagamento.Valor))!.GetColumnType());
        Assert.Equal("pagamento_externo_id", pagamento.FindProperty(nameof(Pagamento.PagamentoExternoId))!.GetColumnName());
        Assert.Equal("link_pagamento", pagamento.FindProperty(nameof(Pagamento.LinkPagamento))!.GetColumnName());
        Assert.True(pagamento.FindProperty(nameof(Pagamento.Status))!.IsConcurrencyToken);
        Assert.Contains(pagamento.GetIndexes(), indice => indice.Properties.Single().Name == nameof(Pagamento.OrdemServicoId));
    }
}
