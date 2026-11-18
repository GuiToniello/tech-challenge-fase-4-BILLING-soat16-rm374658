using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.Pagamentos.Mappings;

public sealed class PagamentoProfileTests
{
    private readonly IMapper _mapper;

    public PagamentoProfileTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(PagamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Map_PagamentoParaPagamentoViewModel_DeveMapearStatusProvedorELink()
    {
        var criadoEm = new DateTime(2026, 11, 18, 10, 00, 00, DateTimeKind.Utc);
        var pagamento = Pagamento.Criar(Guid.NewGuid(), 120m, ProvedorPagamento.MercadoPago, "pref-1", "https://checkout/pref-1", criadoEm);
        pagamento.Aprovar("123456", criadoEm.AddMinutes(5));

        var viewModel = _mapper.Map<PagamentoViewModel>(pagamento);

        Assert.Equal(pagamento.Id, viewModel.Id);
        Assert.Equal(pagamento.OrdemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(120m, viewModel.Valor);
        Assert.Equal(2, viewModel.Status);
        Assert.Equal("Aprovado", viewModel.StatusDescricao);
        Assert.Equal("MercadoPago", viewModel.Provedor);
        Assert.Equal("pref-1", viewModel.PreferenciaId);
        Assert.Equal("123456", viewModel.PagamentoExternoId);
        Assert.Equal("https://checkout/pref-1", viewModel.LinkPagamento);
        Assert.Equal(criadoEm, viewModel.CriadoEm);
        Assert.Equal(criadoEm.AddMinutes(5), viewModel.AtualizadoEm);
    }
}
