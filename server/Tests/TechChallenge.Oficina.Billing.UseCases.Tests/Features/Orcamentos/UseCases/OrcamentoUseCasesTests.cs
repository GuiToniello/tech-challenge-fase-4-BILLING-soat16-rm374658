using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.Orcamentos.UseCases;

public sealed class OrcamentoUseCasesTests
{
    private const string LinkCheckout = "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=pref-1";

    private static readonly IMapper Mapper = CriarMapper();

    private readonly Mock<IOrcamentoGateway> _orcamentoGatewayMock = new();
    private readonly Mock<IOrdemServicoReferenciaGateway> _referenciaGatewayMock = new();
    private readonly Mock<IPagamentoGateway> _pagamentoGatewayMock = new();
    private readonly Mock<IOrcamentoEmailSender> _orcamentoEmailSenderMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<IGatewayPagamento> _gatewayPagamentoMock = new();
    private readonly Mock<IHistoricoPagamentoGateway> _historicoPagamentoGatewayMock = new();
    private readonly Mock<IOrcamentoUseCasesFacade> _facadeMock = new();

    private static IMapper CriarMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IMapper>();
    }

    private OrcamentoUseCases CriarUseCases(ILogger<OrcamentoUseCases>? logger = null)
    {
        _facadeMock.SetupGet(f => f.OrcamentoEmailSender).Returns(_orcamentoEmailSenderMock.Object);
        _facadeMock.SetupGet(f => f.EventosPublisher).Returns(_eventosPublisherMock.Object);
        _facadeMock.SetupGet(f => f.GatewayPagamento).Returns(_gatewayPagamentoMock.Object);
        _facadeMock.SetupGet(f => f.HistoricoPagamentoGateway).Returns(_historicoPagamentoGatewayMock.Object);

        return new OrcamentoUseCases(
            Mapper,
            _orcamentoGatewayMock.Object,
            _referenciaGatewayMock.Object,
            _pagamentoGatewayMock.Object,
            _facadeMock.Object,
            logger ?? NullLogger<OrcamentoUseCases>.Instance);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(Mock<ILogger<OrcamentoUseCases>> loggerMock, EventId evento)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    private OrdemServicoReferencia CriarReferencia(
        StatusOrdemServico status = StatusOrdemServico.Recebida,
        string? email = "cliente@teste.com",
        bool estoqueSuficiente = true,
        decimal valor = 50m)
    {
        var servico = ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca de oleo", valor);
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, estoqueSuficiente, [servico], valor);
        referencia.AtualizarStatus(status);

        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);

        return referencia;
    }

    private Orcamento CriarOrcamento(OrdemServicoReferencia referencia)
    {
        var orcamento = Orcamento.Criar(referencia.Id, referencia.Servicos, new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);
        return orcamento;
    }

    private Pagamento CriarUltimoPagamento(Guid ordemServicoId, StatusPagamento status)
    {
        var pagamento = Pagamento.Criar(ordemServicoId, 50m, ProvedorPagamento.MercadoPago, "pref-anterior", "https://checkout/anterior");

        if (status == StatusPagamento.Recusado)
        {
            pagamento.Recusar("111");
        }
        else if (status == StatusPagamento.Aprovado)
        {
            pagamento.Aprovar("111");
        }

        _pagamentoGatewayMock.Setup(g => g.ObterUltimoPorOrdemServicoIdAsync(ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(pagamento);
        return pagamento;
    }

    private void ConfigurarCobranca()
    {
        _gatewayPagamentoMock
            .Setup(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CobrancaCriada("pref-1", LinkCheckout, ProvedorPagamento.MercadoPago));
    }

    private void VerificarSemCobrancaNemGravacao()
    {
        _gatewayPagamentoMock.Verify(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()), Times.Never);
        _pagamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveRetornarViewModel_QuandoOrcamentoExiste()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia();
        CriarOrcamento(referencia);

        var resultado = await useCases.ObterPorOrdemServicoIdAsync(new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = referencia.Id });

        Assert.Equal(referencia.Id, resultado.OrdemServicoId);
        Assert.Equal(50m, resultado.ValorTotal);
        Assert.Equal((int)StatusOrcamento.Gerado, resultado.Status);
        Assert.Equal("Gerado", resultado.StatusDescricao);
        Assert.Single(resultado.Servicos);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.ObterPorOrdemServicoIdAsync(new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task GerarAsync_DeveCriarOrcamentoComOSnapshotPublicarEventoERegistrarLog()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia(StatusOrdemServico.EmDiagnostico);

        var resultado = await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(referencia.Id, resultado.OrdemServicoId);
        Assert.Equal(50m, resultado.ValorTotal);
        Assert.Equal((int)StatusOrcamento.Gerado, resultado.Status);
        Assert.Equal("Troca de oleo", Assert.Single(resultado.Servicos).NomeServico);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.Is<Orcamento>(o => o.Id == referencia.Id && o.ValorTotal == 50m), It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(referencia.Id, 50m), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoGerado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
        Assert.Equal(false, propriedades["Regerado"]);
    }

    [Fact]
    public async Task GerarAsync_DeveRegerarOrcamentoExistenteComOSnapshotAtual()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia);
        var servicoAtualizado = ServicoOrdemServico.Criar(referencia.Servicos.Single().ServicoId, "Troca de oleo", 80m);
        referencia.AtualizarDados(referencia.ClienteId, referencia.VeiculoId, referencia.ClienteEmail, true, [servicoAtualizado], 80m);

        var resultado = await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(80m, resultado.ValorTotal);
        Assert.Equal(80m, orcamento.ValorTotal);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(referencia.Id, 80m), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
        var (_, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoGerado);
        Assert.Equal(true, propriedades["Regerado"]);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemPersistir_QuandoEstoqueInsuficiente()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(estoqueSuficiente: false);

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Estoque insuficiente para gerar orcamento.", exception.Message);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarKeyNotFoundException_QuandoOrdemNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Ordem de servico nao encontrada.", exception.Message);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemPersistir_QuandoOrdemFinalizada()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.Finalizada);

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento gerado.", exception.Message);
        _orcamentoGatewayMock.Verify(g => g.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemPersistir_QuandoOrcamentoJaFoiAprovadoParaPagamento()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia).AprovarParaPagamento();

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser gerado novamente.", exception.Message);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainException_QuandoOrdemAindaNaoFoiValidadaPelaManagement()
    {
        var useCases = CriarUseCases();
        var referencia = OrdemServicoReferencia.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico);
        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("A ordem de servico ainda nao foi validada pela Management. Tente novamente em instantes.", exception.Message);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveEnviarSemLinkERegistrarLog_QuandoOrcamentoEEmailExistem()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);

        await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        _orcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", null, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoEnviado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveIncluirLinkDePagamento_QuandoHaCobrancaPendente()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);
        var pagamento = CriarUltimoPagamento(referencia.Id, StatusPagamento.Pendente);

        await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        _orcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", pagamento.LinkPagamento, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveLancarDomainException_QuandoClienteSemEmail()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(email: null);
        CriarOrcamento(referencia);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("O cliente da ordem de servico nao possui email cadastrado.", exception.Message);
        _orcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioFalhar()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);
        var falha = new HttpRequestException("Falha de comunicacao com o provedor de email.");
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", null, It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Same(falha, exception);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("resend", propriedades["Integracao"]);
        Assert.Equal("envio_orcamento", propriedades["Operacao"]);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioExpirarSemCancelamento()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);
        var timeout = new TaskCanceledException("Timeout do HttpClient no envio de email", new TimeoutException("Tempo esgotado"));
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", null, It.IsAny<CancellationToken>())).ThrowsAsync(timeout);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(action);
        Assert.Same(timeout, exception);
        var (nivel, _, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(timeout, excecaoRegistrada);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRelancarSemRegistrarFalhaDeIntegracao_QuandoRequisicaoCancelada()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", null, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id }, cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
    }

    [Fact]
    public async Task AprovarAsync_DeveCriarCobrancaGravarPagamentoPendenteEAguardarPagamento()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia);
        ConfigurarCobranca();
        Pagamento? pagamentoGravado = null;
        _pagamentoGatewayMock
            .Setup(g => g.AdicionarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()))
            .Callback<Pagamento, CancellationToken>((pagamento, _) => pagamentoGravado = pagamento)
            .Returns(Task.CompletedTask);

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(StatusOrcamento.AguardandoPagamento, orcamento.Status);
        Assert.Equal((int)StatusOrcamento.AguardandoPagamento, resultado.Orcamento.Status);
        Assert.Equal("Aguardando pagamento", resultado.Orcamento.StatusDescricao);
        Assert.Equal(LinkCheckout, resultado.Pagamento.LinkPagamento);
        Assert.Equal((int)StatusPagamento.Pendente, resultado.Pagamento.Status);
        Assert.Equal("MercadoPago", resultado.Pagamento.Provedor);
        Assert.NotNull(pagamentoGravado);
        Assert.Equal(pagamentoGravado!.Id, resultado.Pagamento.Id);
        Assert.Equal(50m, pagamentoGravado.Valor);
        Assert.Equal("pref-1", pagamentoGravado.PreferenciaId);
        _gatewayPagamentoMock.Verify(g => g.CriarCobrancaAsync(
            It.Is<NovaCobranca>(c => c.OrdemServicoId == referencia.Id && c.Valor == 50m && c.EmailPagador == "cliente@teste.com" && c.Descricao.Contains("Orçamento")),
            It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        // A OS so segue para execucao com o pagamento confirmado: a aprovacao nao publica evento
        Assert.Empty(_eventosPublisherMock.Invocations);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.PagamentoCriado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
        Assert.Equal(pagamentoGravado.Id, propriedades["PagamentoId"]);
        _historicoPagamentoGatewayMock.Verify(h => h.RegistrarAsync(
            It.Is<EventoHistoricoPagamento>(evento =>
                evento.OrdemServicoId == referencia.Id &&
                evento.Tipo == TiposHistoricoPagamento.CobrancaCriada &&
                evento.Origem == OrigensHistoricoPagamento.Aprovacao &&
                evento.PagamentoId == pagamentoGravado.Id &&
                evento.Provedor == "MercadoPago" &&
                evento.Valor == 50m &&
                evento.PreferenciaId == "pref-1" &&
                evento.PagamentoExternoId == null &&
                evento.OcorridoEm.Kind == DateTimeKind.Utc),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AprovarAsync_DeveRegistrarCobrancaCriadaNoHistoricoDepoisDaGravacao()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia);
        ConfigurarCobranca();
        var ordem = new List<string>();
        _pagamentoGatewayMock
            .Setup(g => g.AdicionarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("pagamento"))
            .Returns(Task.CompletedTask);
        _orcamentoGatewayMock
            .Setup(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()))
            .Callback(() => ordem.Add("orcamento"))
            .Returns(Task.CompletedTask);
        _historicoPagamentoGatewayMock
            .Setup(h => h.RegistrarAsync(It.IsAny<EventoHistoricoPagamento>(), It.IsAny<CancellationToken>()))
            .Callback<EventoHistoricoPagamento, CancellationToken>((evento, _) => ordem.Add(evento.Tipo))
            .Returns(Task.CompletedTask);

        await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(["pagamento", "orcamento", TiposHistoricoPagamento.CobrancaCriada], ordem);
    }

    [Fact]
    public async Task AprovarAsync_DeveDevolverACobrancaPendenteSemCriarOutra()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia).AprovarParaPagamento();
        var pagamento = CriarUltimoPagamento(referencia.Id, StatusPagamento.Pendente);

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(pagamento.Id, resultado.Pagamento.Id);
        Assert.Equal(pagamento.LinkPagamento, resultado.Pagamento.LinkPagamento);
        Assert.Equal((int)StatusOrcamento.AguardandoPagamento, resultado.Orcamento.Status);
        VerificarSemCobrancaNemGravacao();
        Assert.Empty(_historicoPagamentoGatewayMock.Invocations);
    }

    [Fact]
    public async Task AprovarAsync_DeveCriarNovaCobranca_QuandoUltimoPagamentoFoiRecusado()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia);
        orcamento.AprovarParaPagamento();
        var recusado = CriarUltimoPagamento(referencia.Id, StatusPagamento.Recusado);
        ConfigurarCobranca();

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.NotEqual(recusado.Id, resultado.Pagamento.Id);
        Assert.Equal(LinkCheckout, resultado.Pagamento.LinkPagamento);
        Assert.Equal(StatusOrcamento.AguardandoPagamento, orcamento.Status);
        _pagamentoGatewayMock.Verify(g => g.AdicionarAsync(It.Is<Pagamento>(p => p.Status == StatusPagamento.Pendente), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AprovarAsync_DevePermitirAprovacao_QuandoOrdemAindaNaoRefletiuOrcamentoGerado()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.EmDiagnostico);
        CriarOrcamento(referencia);
        ConfigurarCobranca();

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.AguardandoPagamento, resultado.Orcamento.Status);
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarDomainExceptionSemCobrar_QuandoPagamentoJaFoiConfirmado()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia);
        orcamento.AprovarParaPagamento();
        orcamento.ConfirmarPagamento();

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado ou aguardando pagamento pode ser aprovado.", exception.Message);
        VerificarSemCobrancaNemGravacao();
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarDomainExceptionSemCobrar_QuandoOrdemJaEstaEmExecucao()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.EmExecucao);
        CriarOrcamento(referencia);

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento aprovado.", exception.Message);
        VerificarSemCobrancaNemGravacao();
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task AprovarAsync_DeveRelancarSemGravar_QuandoProvedorDePagamentoFalhar()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia();
        var orcamento = CriarOrcamento(referencia);
        var falha = new InvalidOperationException("Mercado Pago indisponivel");
        _gatewayPagamentoMock.Setup(g => g.CriarCobrancaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        _pagamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Pagamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecusarAsync_DeveRecusarEPublicarEvento()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia);

        var resultado = await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.Recusado, resultado.Status);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoRecusado(referencia.Id), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecusarAsync_DevePermitirRecusa_QuandoOrcamentoAguardaPagamento()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia).AprovarParaPagamento();

        var resultado = await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.Recusado, resultado.Status);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoRecusado(referencia.Id), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecusarAsync_DeveLancarDomainException_QuandoOrcamentoJaFoiRecusado()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia).Recusar();

        var action = async () => await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado ou aguardando pagamento pode ser recusado.", exception.Message);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoRecusado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecusarAsync_DeveLancarDomainException_QuandoOrdemFinalizada()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferencia(StatusOrdemServico.Finalizada);
        var orcamento = CriarOrcamento(referencia);

        var action = async () => await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento recusado.", exception.Message);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
