using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Messaging.Configuration;
using TechChallenge.Oficina.Billing.Messaging.Features.Eventos;
using TechChallenge.Oficina.Billing.Messaging.Sqs;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.Billing.Messaging.Tests.Features.Eventos;

public sealed class EventosPublisherTests
{
    private const string FilaOsService = "techchallenge-oficina-os-service.fifo";
    private const string FilaManagement = "techchallenge-oficina-management.fifo";
    private const string UrlFilaOsService = "http://elasticmq:9324/000000000000/techchallenge-oficina-os-service.fifo";
    private const string UrlFilaManagement = "http://elasticmq:9324/000000000000/techchallenge-oficina-management.fifo";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly SqsSettings _sqsSettings = new()
    {
        FilaConsumo = "techchallenge-oficina-billing.fifo",
        FilasPublicacao = [FilaOsService, FilaManagement]
    };
    private readonly List<(string Url, string Corpo, string Tipo, string Grupo, string Deduplicacao)> _enviadas = [];

    public EventosPublisherTests()
    {
        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFilaOsService);
        _sqsClientMock.Setup(c => c.GetQueueUrlAsync(FilaManagement, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFilaManagement);
        _sqsClientMock
            .Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((url, corpo, tipo, grupo, deduplicacao, _) => _enviadas.Add((url, corpo, tipo, grupo, deduplicacao)))
            .Returns(Task.CompletedTask);
    }

    private EventosPublisher CriarPublisher(ILogger<EventosPublisher>? logger = null) =>
        new(_sqsClientMock.Object, _sqsSettings, logger ?? NullLogger<EventosPublisher>.Instance);

    private static IReadOnlyDictionary<string, object?> ObterPropriedades(Moq.IInvocation invocacao) =>
        ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

    [Fact]
    public async Task PublicarAsync_DeveEnviarOMesmoEnvelopeEmCamelCaseParaCadaFila()
    {
        var ordemServicoId = Guid.NewGuid();
        var pagamentoId = Guid.NewGuid();

        await CriarPublisher().PublicarAsync(new PagamentoAprovado(ordemServicoId, pagamentoId, 389.90m), ordemServicoId);

        Assert.Equal([UrlFilaOsService, UrlFilaManagement], _enviadas.Select(enviada => enviada.Url));
        Assert.Single(_enviadas.Select(enviada => enviada.Corpo).Distinct());
        Assert.All(_enviadas, enviada => Assert.Equal("PagamentoAprovado", enviada.Tipo));

        var (_, corpo, _, grupo, deduplicacao) = _enviadas[0];
        using var json = JsonDocument.Parse(corpo);
        var envelope = json.RootElement;
        Assert.Equal(["id", "tipo", "versao", "ocorridoEm", "origem", "dados"], envelope.EnumerateObject().Select(propriedade => propriedade.Name));
        Assert.Equal("PagamentoAprovado", envelope.GetProperty("tipo").GetString());
        Assert.Equal(1, envelope.GetProperty("versao").GetInt32());
        Assert.Equal("billing", envelope.GetProperty("origem").GetString());

        var ocorridoEm = envelope.GetProperty("ocorridoEm").GetString()!;
        Assert.EndsWith("Z", ocorridoEm);
        Assert.InRange(DateTime.Parse(ocorridoEm, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var dados = envelope.GetProperty("dados");
        Assert.Equal(["ordemServicoId", "pagamentoId", "valor"], dados.EnumerateObject().Select(propriedade => propriedade.Name));
        Assert.Equal(ordemServicoId, dados.GetProperty("ordemServicoId").GetGuid());
        Assert.Equal(pagamentoId, dados.GetProperty("pagamentoId").GetGuid());
        Assert.Equal(389.90m, dados.GetProperty("valor").GetDecimal());

        // Grupo FIFO = ordem de servico; deduplicacao = id do envelope (o mesmo nas duas filas)
        Assert.Equal(ordemServicoId.ToString("D"), grupo);
        Assert.Equal(envelope.GetProperty("id").GetGuid().ToString("D"), deduplicacao);
        Assert.All(_enviadas, enviada => Assert.Equal(deduplicacao, enviada.Deduplicacao));
    }

    [Fact]
    public async Task PublicarAsync_DeveSerializarOrcamentoGeradoComValorTotal()
    {
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher().PublicarAsync(new OrcamentoGerado(ordemServicoId, 150.5m), ordemServicoId);

        using var json = JsonDocument.Parse(_enviadas[0].Corpo);
        var dados = json.RootElement.GetProperty("dados");
        Assert.Equal(["ordemServicoId", "valorTotal"], dados.EnumerateObject().Select(propriedade => propriedade.Name));
        Assert.Equal(150.5m, dados.GetProperty("valorTotal").GetDecimal());
    }

    [Fact]
    public async Task PublicarAsync_DeveGerarIdDeEnvelopeDiferentePorEvento()
    {
        var publisher = CriarPublisher();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);
        await publisher.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);

        Assert.Equal(4, _enviadas.Count);
        Assert.Equal(2, _enviadas.Select(enviada => enviada.Deduplicacao).Distinct().Count());
        Assert.All(_enviadas, enviada => Assert.Equal(ordemServicoId.ToString("D"), enviada.Grupo));
    }

    [Fact]
    public async Task PublicarAsync_DeveObterUrlDeCadaFilaUmaVez()
    {
        var publisher = CriarPublisher();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);
        await publisher.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);

        _sqsClientMock.Verify(c => c.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(c => c.GetQueueUrlAsync(FilaManagement, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarEventoPublicadoPorFila()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var ordemServicoId = Guid.NewGuid();

        await CriarPublisher(loggerMock.Object).PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);

        var invocacoes = loggerMock.Invocations
            .Where(chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.EventoPublicado.Id)
            .ToArray();
        Assert.Equal(2, invocacoes.Length);
        Assert.All(invocacoes, invocacao => Assert.Equal(LogLevel.Information, (LogLevel)invocacao.Arguments[0]));
        Assert.Equal([FilaOsService, FilaManagement], invocacoes.Select(invocacao => ObterPropriedades(invocacao)["Fila"]));
        Assert.All(invocacoes, invocacao => Assert.Equal("OrcamentoRecusado", ObterPropriedades(invocacao)["Tipo"]));
        Assert.All(invocacoes, invocacao => Assert.Equal(ordemServicoId, ObterPropriedades(invocacao)["OrdemServicoId"]));
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioParaUmaFilaFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var falha = new InvalidOperationException("SQS indisponivel");
        var ordemServicoId = Guid.NewGuid();
        _sqsClientMock
            .Setup(c => c.SendAsync(UrlFilaManagement, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarPublisher(loggerMock.Object).PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(falha, exception);
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
        var propriedades = ObterPropriedades(invocacao);
        Assert.Equal(LogLevel.Error, (LogLevel)invocacao.Arguments[0]);
        Assert.Same(falha, invocacao.Arguments[3]);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("publicacao", propriedades["Operacao"]);
        Assert.Equal("OrcamentoRecusado", propriedades["Tipo"]);
        Assert.Equal(FilaManagement, propriedades["Fila"]);
        // A primeira fila recebeu o evento; a falha na segunda e registrada e relancada
        var publicado = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.EventoPublicado.Id);
        Assert.Equal(FilaOsService, ObterPropriedades(publicado)["Fila"]);
    }

    [Fact]
    public async Task PublicarAsync_DeveRelancarSemRegistrarFalha_QuandoCancelado()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _sqsClientMock
            .Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));
        var ordemServicoId = Guid.NewGuid();

        var action = async () => await CriarPublisher(loggerMock.Object).PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId, cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
    }
}
