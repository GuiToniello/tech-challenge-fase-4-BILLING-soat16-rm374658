using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.API.Tests.Features.Orcamentos;

public sealed class OrcamentoEndpointsTests
{
    private readonly Mock<IOrcamentoUseCases> _orcamentoUseCasesMock = new();

    private OrcamentosController CriarController()
        => new(_orcamentoUseCasesMock.Object, new OrcamentosAdapter(), NullLogger<OrcamentosController>.Instance);

    [Fact]
    public async Task MapOrcamentoEndpoints_Gerar_DeveRetornarOkComOrcamento()
    {
        var controller = CriarController();
        var ordemServicoId = Guid.NewGuid();
        var orcamento = new OrcamentoViewModel { OrdemServicoId = ordemServicoId, ValorTotal = 50m, Status = 1, StatusDescricao = "Gerado" };
        _orcamentoUseCasesMock
            .Setup(u => u.GerarAsync(It.Is<GerarOrcamentoCommand>(c => c.OrdemServicoId == ordemServicoId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orcamento);

        var resultado = await controller.Gerar(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(resultado);
        Assert.Same(orcamento, ok.Value);
    }

    [Fact]
    public async Task MapOrcamentoEndpoints_Enviar_DeveRetornarNoContent()
    {
        var controller = CriarController();

        var resultado = await controller.Enviar(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContent>(resultado);
    }

    [Fact]
    public async Task MapOrcamentoEndpoints_Aprovar_DeveRetornarOkComOLinkDePagamento()
    {
        var controller = CriarController();
        var aprovacao = new AprovacaoOrcamentoViewModel
        {
            Orcamento = new OrcamentoViewModel { Status = 2, StatusDescricao = "Aguardando pagamento" },
            Pagamento = new PagamentoViewModel { Status = 1, LinkPagamento = "https://simulado.mercadopago.local/checkout/1" }
        };
        _orcamentoUseCasesMock
            .Setup(u => u.AprovarAsync(It.IsAny<AprovarOrcamentoCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aprovacao);

        var resultado = await controller.Aprovar(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(resultado);
        Assert.Equal("https://simulado.mercadopago.local/checkout/1", ok.Value!.Pagamento.LinkPagamento);
    }

    [Fact]
    public async Task MapOrcamentoEndpoints_Aprovar_DeveRetornarBadRequest_QuandoRegraDeNegocioViolada()
    {
        var controller = CriarController();
        _orcamentoUseCasesMock
            .Setup(u => u.AprovarAsync(It.IsAny<AprovarOrcamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Somente orcamento gerado ou aguardando pagamento pode ser aprovado."));

        var resultado = await controller.Aprovar(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(resultado);
    }

    [Fact]
    public async Task MapOrcamentoEndpoints_Recusar_DeveRetornarNotFound_QuandoOrcamentoNaoExiste()
    {
        var controller = CriarController();
        _orcamentoUseCasesMock
            .Setup(u => u.RecusarAsync(It.IsAny<RecusarOrcamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Orcamento nao encontrado."));

        var resultado = await controller.Recusar(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFound<Dictionary<string, string?>>>(resultado);
    }
}
