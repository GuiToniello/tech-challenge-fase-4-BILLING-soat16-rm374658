using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechChallenge.Oficina.Billing.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Adapters.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Eventos;
using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Pagamentos;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;

/// <summary>
/// Fixture de integração: DI real com EF Core InMemory e o gateway de pagamento simulado (Mercado Pago sem access token).
/// Só as integrações externas são substituídas: publicação de eventos no SQS e envio de emails pelo Resend (mocks) e o histórico de pagamentos no DynamoDB (lista em memória).
/// Cada instância possui banco de dados isolado (nome único por GUID) e cada controller é obtido de um escopo novo, como numa requisição.
/// </summary>
public sealed class IntegrationTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly List<IServiceScope> _escopos = [];

    public IntegrationTestFixture()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(dbName));

        services.AddScoped<IOrcamentoGateway, OrcamentoGateway>();
        services.AddScoped<IOrdemServicoReferenciaGateway, OrdemServicoReferenciaGateway>();
        services.AddScoped<IPagamentoGateway, PagamentoGateway>();

        EventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<It.IsAnyType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        services.AddSingleton(EventosPublisherMock.Object);
        services.AddSingleton(OrcamentoEmailSenderMock.Object);
        services.AddSingleton<IHistoricoPagamentoGateway>(HistoricoPagamento);
        services.AddInfraPagamentos(new MercadoPagoSettings());

        services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);
        services.AddScoped<IOrcamentoUseCasesFacade, OrcamentoUseCasesFacade>();
        services.AddScoped<IOrcamentoUseCases, OrcamentoUseCases>();
        services.AddScoped<IPagamentoUseCases, PagamentoUseCases>();
        services.AddScoped<IOrdemServicoEventosUseCases, OrdemServicoEventosUseCases>();
        services.AddScoped<IOrcamentosAdapter, OrcamentosAdapter>();
        services.AddScoped<OrcamentosController>();
        services.AddScoped<IPagamentosAdapter, PagamentosAdapter>();
        services.AddScoped<PagamentosController>();
        services.AddScoped<IEventosController, EventosController>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public Mock<IEventosPublisher> EventosPublisherMock { get; } = new();
    public Mock<IOrcamentoEmailSender> OrcamentoEmailSenderMock { get; } = new();
    public HistoricoPagamentoEmMemoria HistoricoPagamento { get; } = new();

    private T Obter<T>() where T : notnull
    {
        var escopo = _serviceProvider.CreateScope();
        _escopos.Add(escopo);
        return escopo.ServiceProvider.GetRequiredService<T>();
    }

    public OrcamentosController CriarOrcamentosEndpoints()
        => Obter<OrcamentosController>();

    public PagamentosController CriarPagamentosEndpoints()
        => Obter<PagamentosController>();

    public IEventosController CriarEventosController()
        => Obter<IEventosController>();

    public BillingDbContext ObterContexto()
        => Obter<BillingDbContext>();

    public void Dispose()
    {
        foreach (var escopo in _escopos)
        {
            escopo.Dispose();
        }

        _serviceProvider.Dispose();
    }
}
