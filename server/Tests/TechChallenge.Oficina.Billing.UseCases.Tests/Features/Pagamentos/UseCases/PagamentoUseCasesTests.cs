using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.Pagamentos.UseCases;

public sealed class PagamentoUseCasesTests
{
    private const string PagamentoExternoId = "1234567890";

    private static readonly IMapper Mapper = CriarMapper();

    private readonly Mock<IPagamentoGateway> _pagamentoGatewayMock = new();
    private readonly Mock<IOrcamentoGateway> _orcamentoGatewayMock = new();
    private readonly Mock<IGatewayPagamento> _gatewayPagamentoMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<ILogger<PagamentoUseCases>> _loggerMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private static IMapper CriarMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(PagamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IMapper>();
    }

    private PagamentoUseCases CriarUseCases() => new(
        Mapper,
        _pagamentoGatewayMock.Object,
        _orcamentoGatewayMock.Object,
        _gatewayPagamentoMock.Object,
        _eventosPublisherMock.Object,
        _loggerMock.Object);

    private Orcamento CriarOrcamentoAguardandoPagamento()
    {
        var orcamento = Orcamento.Criar(_ordemServicoId, [ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca de oleo", 120m)]);
        orcamento.AprovarParaPagamento();
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(_ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);
        return orcamento;
    }

    private Pagamento CriarUltimoPagamento(StatusPagamento status = StatusPagamento.Pendente)
    {
        var pagamento = Pagamento.Criar(_ordemServicoId, 120m, ProvedorPagamento.MercadoPago, "pref-1", "https://checkout/pref-1");

        if (status == StatusPagamento.Aprovado)
        {
            pagamento.Aprovar(PagamentoExternoId);
        }
        else if (status == StatusPagamento.Recusado)
        {
            pagamento.Recusar("111");
        }

        _pagamentoGatewayMock.Setup(g => g.ObterUltimoPorOrdemServicoIdAsync(_ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(pagamento);
        return pagamento;
    }

    private void ConfigurarSituacaoPorReferencia(StatusPagamento situacao)
    {
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorReferenciaAsync(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, situacao, 120m));
    }

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades) ObterRegistroDeLog(EventId evento)
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades);
    }

    private void VerificarSemAlteracaoNemPublicacao()
    {
        _pagamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_eventosPublisherMock.Invocations);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveRetornarACobrancaMaisRecente()
    {
        var pagamento = CriarUltimoPagamento();

        var resultado = await CriarUseCases().ObterPorOrdemServicoIdAsync(new ObterPagamentoPorOrdemServicoIdQuery { OrdemServicoId = _ordemServicoId });

        Assert.Equal(pagamento.Id, resultado.Id);
        Assert.Equal("Pendente", resultado.StatusDescricao);
        Assert.Equal("https://checkout/pref-1", resultado.LinkPagamento);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveLancarKeyNotFoundException_QuandoOrdemSemCobranca()
    {
        var action = async () => await CriarUseCases().ObterPorOrdemServicoIdAsync(new ObterPagamentoPorOrdemServicoIdQuery { OrdemServicoId = _ordemServicoId });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Pagamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task VerificarAsync_DeveAprovarPagamentoConfirmarOrcamentoEPublicarPagamentoAprovado()
    {
        var orcamento = CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Aprovado);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Aprovado, resultado.Status);
        Assert.Equal(PagamentoExternoId, resultado.PagamentoExternoId);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
        _pagamentoGatewayMock.Verify(g => g.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new PagamentoAprovado(_ordemServicoId, pagamento.Id, 120m), _ordemServicoId, CancellationToken.None), Times.Once);
        var (nivel, propriedades) = ObterRegistroDeLog(OrdemServicoLogEventos.PagamentoAprovado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.Equal(pagamento.Id, propriedades["PagamentoId"]);
        Assert.Equal(PagamentoExternoId, propriedades["PagamentoExternoId"]);
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveConsultarNemPublicarNovamente_QuandoPagamentoJaAprovado()
    {
        CriarUltimoPagamento(StatusPagamento.Aprovado);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Aprovado, resultado.Status);
        Assert.Empty(_gatewayPagamentoMock.Invocations);
        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task VerificarAsync_DeveRecusarPagamentoSemPublicar_QuandoProvedorRecusou()
    {
        var orcamento = CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Recusado);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Recusado, resultado.Status);
        Assert.Equal(StatusOrcamento.AguardandoPagamento, orcamento.Status);
        _pagamentoGatewayMock.Verify(g => g.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_eventosPublisherMock.Invocations);
        var (nivel, propriedades) = ObterRegistroDeLog(OrdemServicoLogEventos.PagamentoRecusado);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal(pagamento.Id, propriedades["PagamentoId"]);
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveAlterar_QuandoPagamentoAindaPendenteNoProvedor()
    {
        CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Pendente);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Pendente, resultado.Status);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveAlterar_QuandoProvedorAindaNaoTemPagamento()
    {
        CriarUltimoPagamento();
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorReferenciaAsync(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SituacaoPagamentoExterno?)null);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Pendente, resultado.Status);
        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task VerificarAsync_DeveLancarKeyNotFoundException_QuandoOrdemSemCobranca()
    {
        var action = async () => await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Pagamento nao encontrado.", exception.Message);
        Assert.Empty(_gatewayPagamentoMock.Invocations);
    }

    [Fact]
    public async Task VerificarAsync_DeveAprovar_QuandoNovaTentativaFoiAprovadaDepoisDeUmaRecusa()
    {
        var orcamento = CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento(StatusPagamento.Recusado);
        ConfigurarSituacaoPorReferencia(StatusPagamento.Aprovado);

        await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<PagamentoAprovado>(), _ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerificarAsync_DeveAprovarSemPublicar_QuandoOrcamentoNaoAguardaMaisPagamento()
    {
        var orcamento = Orcamento.Criar(_ordemServicoId, [ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca de oleo", 120m)]);
        orcamento.Recusar();
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(_ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);
        var pagamento = CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Aprovado);

        await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        // O dinheiro entrou: o pagamento fica registrado, mas a OS recusada nao segue para execucao
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
        _pagamentoGatewayMock.Verify(g => g.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_eventosPublisherMock.Invocations);
    }

    [Fact]
    public async Task VerificarAsync_DevePropagarFalhaDoProvedorSemAlterar()
    {
        CriarUltimoPagamento();
        var falha = new InvalidOperationException("Mercado Pago indisponivel");
        _gatewayPagamentoMock.Setup(g => g.ConsultarPorReferenciaAsync(_ordemServicoId, It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveConsultarPeloIdNoProvedorEAprovar()
    {
        var orcamento = CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new PagamentoAprovado(_ordemServicoId, pagamento.Id, 120m), _ordemServicoId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveIgnorar_QuandoPagamentoDesconhecidoNoProvedor()
    {
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync("123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SituacaoPagamentoExterno?)null);

        await CriarUseCases().ProcessarNotificacaoAsync("123");

        _pagamentoGatewayMock.Verify(g => g.ObterUltimoPorOrdemServicoIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveIgnorar_QuandoOrdemSemCobranca()
    {
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveIgnorarSemPublicarNovamente_QuandoPagamentoJaAprovado()
    {
        CriarOrcamentoAguardandoPagamento();
        CriarUltimoPagamento(StatusPagamento.Aprovado);
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        VerificarSemAlteracaoNemPublicacao();
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveRecusarPagamento_QuandoProvedorRecusou()
    {
        var pagamento = CriarUltimoPagamento();
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Recusado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Equal(PagamentoExternoId, pagamento.PagamentoExternoId);
        _pagamentoGatewayMock.Verify(g => g.AtualizarAsync(pagamento, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_eventosPublisherMock.Invocations);
    }
}
