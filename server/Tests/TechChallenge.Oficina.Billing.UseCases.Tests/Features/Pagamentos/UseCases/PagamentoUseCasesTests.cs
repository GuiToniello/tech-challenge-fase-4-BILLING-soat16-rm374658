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
    private readonly Mock<IHistoricoPagamentoGateway> _historicoPagamentoGatewayMock = new();
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
        _historicoPagamentoGatewayMock.Object,
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

    // Eventos enviados ao historico, na ordem; o registro e best-effort e nunca usa o token da requisicao
    private List<EventoHistoricoPagamento> ObterEventosDoHistorico()
    {
        var registros = _historicoPagamentoGatewayMock.Invocations
            .Where(chamada => chamada.Method.Name == nameof(IHistoricoPagamentoGateway.RegistrarAsync))
            .ToList();

        Assert.All(registros, chamada => Assert.Equal(CancellationToken.None, (CancellationToken)chamada.Arguments[1]));
        return registros.Select(chamada => (EventoHistoricoPagamento)chamada.Arguments[0]).ToList();
    }

    // Ordem entre gravacao, publicacao e historico (mocks diferentes): cada chamada anota o seu passo
    private List<string> RegistrarOrdemDasChamadas()
    {
        var ordem = new List<string>();
        _pagamentoGatewayMock
            .Setup(g => g.AtualizarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("gravacao"))
            .Returns(Task.CompletedTask);
        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<PagamentoAprovado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("publicacao"))
            .Returns(Task.CompletedTask);
        _historicoPagamentoGatewayMock
            .Setup(h => h.RegistrarAsync(It.IsAny<EventoHistoricoPagamento>(), It.IsAny<CancellationToken>()))
            .Callback<EventoHistoricoPagamento, CancellationToken>((evento, _) => ordem.Add(evento.Tipo))
            .Returns(Task.CompletedTask);
        return ordem;
    }

    private void VerificarEvento(EventoHistoricoPagamento evento, Pagamento pagamento, string tipo, string origem, string? pagamentoExternoId)
    {
        Assert.Equal(_ordemServicoId, evento.OrdemServicoId);
        Assert.Equal(tipo, evento.Tipo);
        Assert.Equal(origem, evento.Origem);
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal("MercadoPago", evento.Provedor);
        Assert.Equal(120m, evento.Valor);
        Assert.Equal(pagamentoExternoId, evento.PagamentoExternoId);
        Assert.Null(evento.PreferenciaId);
        Assert.Equal(DateTimeKind.Utc, evento.OcorridoEm.Kind);
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
    public async Task VerificarAsync_DeveRegistrarPagamentoAprovadoNoHistoricoDepoisDaGravacaoEDaPublicacao()
    {
        CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Aprovado);
        var ordem = RegistrarOrdemDasChamadas();

        await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal(["gravacao", "publicacao", TiposHistoricoPagamento.PagamentoAprovado], ordem);
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.PagamentoAprovado, OrigensHistoricoPagamento.Verificacao, PagamentoExternoId);
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveRegistrarNoHistorico_QuandoPublicacaoFalha()
    {
        CriarOrcamentoAguardandoPagamento();
        CriarUltimoPagamento();
        ConfigurarSituacaoPorReferencia(StatusPagamento.Aprovado);
        var falha = new InvalidOperationException("SQS indisponivel");
        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<PagamentoAprovado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveConsultarNemPublicarNovamente_QuandoPagamentoJaAprovado()
    {
        CriarUltimoPagamento(StatusPagamento.Aprovado);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Aprovado, resultado.Status);
        Assert.Empty(_gatewayPagamentoMock.Invocations);
        VerificarSemAlteracaoNemPublicacao();
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
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
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.PagamentoRecusado, OrigensHistoricoPagamento.Verificacao, PagamentoExternoId);
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
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.PagamentoPendente, OrigensHistoricoPagamento.Verificacao, PagamentoExternoId);
    }

    [Fact]
    public async Task VerificarAsync_NaoDeveAlterar_QuandoProvedorAindaNaoTemPagamento()
    {
        var pagamento = CriarUltimoPagamento();
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorReferenciaAsync(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SituacaoPagamentoExterno?)null);

        var resultado = await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        Assert.Equal((int)StatusPagamento.Pendente, resultado.Status);
        VerificarSemAlteracaoNemPublicacao();
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.PagamentoPendente, OrigensHistoricoPagamento.Verificacao, null);
    }

    [Fact]
    public async Task VerificarAsync_DeveLancarKeyNotFoundException_QuandoOrdemSemCobranca()
    {
        var action = async () => await CriarUseCases().VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = _ordemServicoId });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Pagamento nao encontrado.", exception.Message);
        Assert.Empty(_gatewayPagamentoMock.Invocations);
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
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
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.PagamentoAprovado, OrigensHistoricoPagamento.Verificacao, PagamentoExternoId);
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
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
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
    public async Task ProcessarNotificacaoAsync_DeveRegistrarNotificacaoEResultadoNoHistoricoDepoisDaPublicacao()
    {
        CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento();
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));
        var ordem = RegistrarOrdemDasChamadas();

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        Assert.Equal(["gravacao", "publicacao", TiposHistoricoPagamento.NotificacaoRecebida, TiposHistoricoPagamento.PagamentoAprovado], ordem);
        var eventos = ObterEventosDoHistorico();
        Assert.Equal(2, eventos.Count);
        VerificarEvento(eventos[0], pagamento, TiposHistoricoPagamento.NotificacaoRecebida, OrigensHistoricoPagamento.Notificacao, PagamentoExternoId);
        VerificarEvento(eventos[1], pagamento, TiposHistoricoPagamento.PagamentoAprovado, OrigensHistoricoPagamento.Notificacao, PagamentoExternoId);
        // A notificacao fica com o horario de chegada: antes do resultado na ordem cronologica
        Assert.True(eventos[0].OcorridoEm <= eventos[1].OcorridoEm);
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
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveIgnorar_QuandoOrdemSemCobranca()
    {
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        VerificarSemAlteracaoNemPublicacao();
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarNotificacaoAsync_DeveIgnorarSemPublicarNovamente_QuandoPagamentoJaAprovado()
    {
        CriarOrcamentoAguardandoPagamento();
        var pagamento = CriarUltimoPagamento(StatusPagamento.Aprovado);
        _gatewayPagamentoMock
            .Setup(g => g.ConsultarPorIdAsync(PagamentoExternoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SituacaoPagamentoExterno(PagamentoExternoId, _ordemServicoId, StatusPagamento.Aprovado, 120m));

        await CriarUseCases().ProcessarNotificacaoAsync(PagamentoExternoId);

        VerificarSemAlteracaoNemPublicacao();
        // Notificacao repetida: so a notificacao vai para o historico, sem novo resultado
        var evento = Assert.Single(ObterEventosDoHistorico());
        VerificarEvento(evento, pagamento, TiposHistoricoPagamento.NotificacaoRecebida, OrigensHistoricoPagamento.Notificacao, PagamentoExternoId);
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
        Assert.Equal(
            [TiposHistoricoPagamento.NotificacaoRecebida, TiposHistoricoPagamento.PagamentoRecusado],
            ObterEventosDoHistorico().Select(evento => evento.Tipo));
    }

    [Fact]
    public async Task ListarHistoricoAsync_DeveRetornarOsEventosEmOrdemCronologica()
    {
        var pagamentoId = Guid.NewGuid();
        var criadaEm = new DateTime(2026, 11, 24, 10, 00, 00, DateTimeKind.Utc);
        var cobranca = new EventoHistoricoPagamento(_ordemServicoId, criadaEm, TiposHistoricoPagamento.CobrancaCriada, OrigensHistoricoPagamento.Aprovacao, pagamentoId, "Simulado", 120m, null, "SIM-PREF");
        var aprovado = cobranca with { OcorridoEm = criadaEm.AddMinutes(1), Tipo = TiposHistoricoPagamento.PagamentoAprovado, Origem = OrigensHistoricoPagamento.Verificacao, PagamentoExternoId = "SIM-PAY", PreferenciaId = null };
        _historicoPagamentoGatewayMock
            .Setup(h => h.ListarAsync(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([aprovado, cobranca]);

        var historico = await CriarUseCases().ListarHistoricoAsync(_ordemServicoId);

        Assert.Equal([TiposHistoricoPagamento.CobrancaCriada, TiposHistoricoPagamento.PagamentoAprovado], historico.Select(evento => evento.Tipo));
        var primeiro = historico.First();
        Assert.Equal(criadaEm, primeiro.OcorridoEm);
        Assert.Equal("aprovacao", primeiro.Origem);
        Assert.Equal(pagamentoId, primeiro.PagamentoId);
        Assert.Equal("Simulado", primeiro.Provedor);
        Assert.Equal(120m, primeiro.Valor);
        Assert.Equal("SIM-PREF", primeiro.PreferenciaId);
        Assert.Equal("SIM-PAY", historico.Last().PagamentoExternoId);
    }

    [Fact]
    public async Task ListarHistoricoAsync_DeveRetornarListaVazia_QuandoNaoHaHistorico()
    {
        _historicoPagamentoGatewayMock
            .Setup(h => h.ListarAsync(_ordemServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var historico = await CriarUseCases().ListarHistoricoAsync(_ordemServicoId);

        Assert.Empty(historico);
    }
}
