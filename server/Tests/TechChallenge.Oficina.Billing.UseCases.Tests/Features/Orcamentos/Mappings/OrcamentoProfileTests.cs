using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.Orcamentos.Mappings;

public sealed class OrcamentoProfileTests
{
    private readonly IMapper _mapper;

    public OrcamentoProfileTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Map_OrcamentoParaOrcamentoViewModel_DeveMapearTotaisStatusEServicos()
    {
        var servico = ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca de oleo", 39.80m);
        var ordemServicoId = Guid.NewGuid();
        var dataGeracao = new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc);
        var orcamento = Orcamento.Criar(ordemServicoId, [servico], dataGeracao);

        var viewModel = _mapper.Map<OrcamentoViewModel>(orcamento);

        Assert.Equal(ordemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(dataGeracao, viewModel.DataGeracao);
        Assert.Equal(39.80m, viewModel.ValorTotal);
        Assert.Equal(1, viewModel.Status);
        Assert.Equal("Gerado", viewModel.StatusDescricao);
        var servicoViewModel = Assert.Single(viewModel.Servicos);
        Assert.Equal(servico.ServicoId, servicoViewModel.ServicoId);
        Assert.Equal("Troca de oleo", servicoViewModel.NomeServico);
        Assert.Equal(39.80m, servicoViewModel.ValorTotal);
    }

    [Fact]
    public void Map_OrcamentoAguardandoPagamento_DeveMapearStatusEDescricao()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [ServicoOrdemServico.Criar(Guid.NewGuid(), "Alinhamento", 30m)]);
        orcamento.AprovarParaPagamento();

        var viewModel = _mapper.Map<OrcamentoViewModel>(orcamento);

        Assert.Equal(2, viewModel.Status);
        Assert.Equal("Aguardando pagamento", viewModel.StatusDescricao);
    }
}
