using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Controllers.Tests.Features.Pagamentos;

public sealed class PagamentosControllerTests
{
    private readonly Mock<IPagamentoUseCases> _useCasesMock = new();
    private readonly Mock<IPagamentosAdapter> _adapterMock = new();
    private readonly Mock<ILogger<PagamentosController>> _loggerMock = new();
    private readonly object _recebido = new();

    public PagamentosControllerTests()
    {
        _adapterMock.Setup(a => a.Recebido()).Returns(_recebido);
    }

    private PagamentosController CriarController(ILogger<PagamentosController>? logger = null) =>
        new(_useCasesMock.Object, _adapterMock.Object, logger ?? _loggerMock.Object);

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades) ObterRegistroDeFalha()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.OsFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades);
    }

    [Fact]
    public async Task GetById_DeveRepassarQueryERetornarAdaptado()
    {
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.Is<ObterPagamentoPorOrdemServicoIdQuery>(q => q.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoViewModel { OrdemServicoId = id });
        _adapterMock
            .Setup(a => a.Adapt(It.Is<PagamentosResult<PagamentoViewModel, Exception>>(r => r.Value!.OrdemServicoId == id)))
            .Returns(adaptado);

        var resposta = await CriarController().GetById(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task GetById_DeveAdaptarErroSemRegistrarFalha_QuandoKeyNotFoundException()
    {
        var excecao = new KeyNotFoundException("Pagamento nao encontrado.");
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.IsAny<ObterPagamentoPorOrdemServicoIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(excecao);

        await CriarController().GetById(Guid.NewGuid(), CancellationToken.None);

        _adapterMock.Verify(a => a.Adapt(It.Is<PagamentosResult<PagamentoViewModel, Exception>>(r => r.Error == excecao)), Times.Once);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public async Task Verificar_DeveRepassarComandoERetornarAdaptado()
    {
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.VerificarAsync(It.Is<VerificarPagamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagamentoViewModel { OrdemServicoId = id, Status = 2 });
        _adapterMock.Setup(a => a.Adapt(It.IsAny<PagamentosResult<PagamentoViewModel, Exception>>())).Returns(adaptado);

        var resposta = await CriarController().Verificar(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task Verificar_DeveRegistrarFalhaDeNaoEncontrado_QuandoOrdemSemCobranca()
    {
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.VerificarAsync(It.IsAny<VerificarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Pagamento nao encontrado."));

        await CriarController().Verificar(id, CancellationToken.None);

        var (nivel, propriedades) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("pagamento_verificacao", propriedades["Etapa"]);
        Assert.Equal(id, propriedades["OrdemServicoId"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        _adapterMock.Verify(a => a.Adapt(It.Is<PagamentosResult<PagamentoViewModel, Exception>>(r => r.Error is KeyNotFoundException)), Times.Once);
    }

    [Fact]
    public async Task Verificar_DevePropagarFalhaDoProvedor()
    {
        _useCasesMock
            .Setup(u => u.VerificarAsync(It.IsAny<VerificarPagamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Mercado Pago indisponivel"));

        var action = async () => await CriarController().Verificar(Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    [Fact]
    public async Task GetHistorico_DeveRepassarOrdemERetornarAdaptado()
    {
        var id = Guid.NewGuid();
        var adaptado = new object();
        IReadOnlyCollection<EventoHistoricoPagamentoViewModel> historico = [new EventoHistoricoPagamentoViewModel { OrdemServicoId = id, Tipo = "CobrancaCriada" }];
        _useCasesMock.Setup(u => u.ListarHistoricoAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(historico);
        _adapterMock
            .Setup(a => a.Adapt(It.Is<PagamentosResult<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>, Exception>>(r => r.Value == historico)))
            .Returns(adaptado);

        var resposta = await CriarController().GetHistorico(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task GetHistorico_DevePropagarFalhaDoDynamoDb()
    {
        // A falha ja sai como integracao.falha no gateway; o tratamento global responde 500
        _useCasesMock
            .Setup(u => u.ListarHistoricoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DynamoDB indisponivel"));

        var action = async () => await CriarController().GetHistorico(Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("PAYMENT")]
    public async Task ReceberNotificacao_DeveProcessarPagamentoEConfirmar(string tipo)
    {
        var resposta = await CriarController().ReceberNotificacao(tipo, "123456", CancellationToken.None);

        Assert.Same(_recebido, resposta);
        _useCasesMock.Verify(u => u.ProcessarNotificacaoAsync("123456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("merchant_order", "123")]
    [InlineData(null, "123")]
    [InlineData("payment", null)]
    [InlineData("payment", " ")]
    public async Task ReceberNotificacao_DeveConfirmarSemProcessar_QuandoNaoEPagamento(string? tipo, string? recursoId)
    {
        var resposta = await CriarController().ReceberNotificacao(tipo, recursoId, CancellationToken.None);

        Assert.Same(_recebido, resposta);
        Assert.Empty(_useCasesMock.Invocations);
    }

    [Fact]
    public async Task ReceberNotificacao_DeveConfirmarERegistrarFalha_QuandoRegraDeNegocio()
    {
        _useCasesMock
            .Setup(u => u.ProcessarNotificacaoAsync("123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("O pagamento ja foi aprovado."));

        var resposta = await CriarController().ReceberNotificacao("payment", "123", CancellationToken.None);

        Assert.Same(_recebido, resposta);
        var (nivel, propriedades) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("pagamento_notificacao", propriedades["Etapa"]);
        Assert.Equal("123", propriedades["PagamentoExternoId"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
    }

    [Fact]
    public async Task ReceberNotificacao_DeveConfirmarERegistrarErro_QuandoFalhaInesperada()
    {
        var falha = new InvalidOperationException("Mercado Pago indisponivel");
        _useCasesMock.Setup(u => u.ProcessarNotificacaoAsync("123", It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var resposta = await CriarController().ReceberNotificacao("payment", "123", CancellationToken.None);

        Assert.Same(_recebido, resposta);
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && (LogLevel)chamada.Arguments[0] == LogLevel.Error);
        Assert.Same(falha, invocacao.Arguments[3]);
    }

    [Fact]
    public async Task ReceberNotificacao_DevePropagarCancelamentoDaRequisicao()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _useCasesMock
            .Setup(u => u.ProcessarNotificacaoAsync("123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await CriarController(NullLogger<PagamentosController>.Instance).ReceberNotificacao("payment", "123", cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
    }
}
