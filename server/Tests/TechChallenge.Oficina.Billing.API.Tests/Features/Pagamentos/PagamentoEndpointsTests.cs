using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Adapters.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.API.Tests.Features.Pagamentos;

public sealed class PagamentoEndpointsTests
{
    private readonly Mock<IPagamentoUseCases> _pagamentoUseCasesMock = new();

    private PagamentosController CriarController()
        => new(_pagamentoUseCasesMock.Object, new PagamentosAdapter(), NullLogger<PagamentosController>.Instance);

    [Fact]
    public async Task MapPagamentoEndpoints_GetById_DeveRetornarOkComACobranca()
    {
        var ordemServicoId = Guid.NewGuid();
        var pagamento = new PagamentoViewModel { OrdemServicoId = ordemServicoId, Status = 1, StatusDescricao = "Pendente" };
        _pagamentoUseCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.Is<ObterPagamentoPorOrdemServicoIdQuery>(q => q.OrdemServicoId == ordemServicoId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagamento);

        var ok = Assert.IsType<Ok<PagamentoViewModel>>(await CriarController().GetById(ordemServicoId, CancellationToken.None));

        Assert.Same(pagamento, ok.Value);
    }

    [Fact]
    public async Task MapPagamentoEndpoints_Verificar_DeveRetornarOkComPagamentoAprovado()
    {
        var ordemServicoId = Guid.NewGuid();
        _pagamentoUseCasesMock
            .Setup(u => u.VerificarAsync(It.Is<VerificarPagamentoCommand>(c => c.OrdemServicoId == ordemServicoId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoViewModel { OrdemServicoId = ordemServicoId, Status = 2, StatusDescricao = "Aprovado" });

        var ok = Assert.IsType<Ok<PagamentoViewModel>>(await CriarController().Verificar(ordemServicoId, CancellationToken.None));

        Assert.Equal("Aprovado", ok.Value!.StatusDescricao);
    }

    [Fact]
    public async Task MapPagamentoEndpoints_Verificar_DeveRetornarNotFound_QuandoOrdemSemCobranca()
    {
        _pagamentoUseCasesMock
            .Setup(u => u.VerificarAsync(It.IsAny<VerificarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Pagamento nao encontrado."));

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await CriarController().Verificar(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task MapPagamentoEndpoints_GetHistorico_DeveRetornarOkComOsEventos()
    {
        var ordemServicoId = Guid.NewGuid();
        IReadOnlyCollection<EventoHistoricoPagamentoViewModel> historico =
        [
            new EventoHistoricoPagamentoViewModel { OrdemServicoId = ordemServicoId, Tipo = "CobrancaCriada" },
            new EventoHistoricoPagamentoViewModel { OrdemServicoId = ordemServicoId, Tipo = "PagamentoAprovado" }
        ];
        _pagamentoUseCasesMock.Setup(u => u.ListarHistoricoAsync(ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(historico);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>>>(await CriarController().GetHistorico(ordemServicoId, CancellationToken.None));

        Assert.Equal(["CobrancaCriada", "PagamentoAprovado"], ok.Value!.Select(evento => evento.Tipo));
    }

    [Fact]
    public async Task MapPagamentoEndpoints_Notificacao_DeveRetornarOkMesmoQuandoOProcessamentoFalha()
    {
        _pagamentoUseCasesMock
            .Setup(u => u.ProcessarNotificacaoAsync("123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mercado Pago indisponivel"));

        Assert.IsType<Ok>(await CriarController().ReceberNotificacao("payment", "123", CancellationToken.None));
    }
}
