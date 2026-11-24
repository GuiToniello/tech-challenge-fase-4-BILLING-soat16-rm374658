using System.Text.RegularExpressions;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Moq;
using TechChallenge.Oficina.Billing.NoSql.Configuration;
using TechChallenge.Oficina.Billing.NoSql.DynamoDb;
using TechChallenge.Oficina.Billing.NoSql.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Billing.NoSql.Tests.Features.Pagamentos;

public sealed class HistoricoPagamentoGatewayTests
{
    private const string Tabela = "techchallenge-oficina-billing-pagamentos-historico";

    private static readonly DateTime OcorridoEm = new(2026, 11, 24, 10, 15, 30, 123, DateTimeKind.Utc);

    private readonly Mock<IDynamoDbClient> _dynamoDbClientMock = new();
    private readonly Mock<ILogger<HistoricoPagamentoGateway>> _loggerMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _pagamentoId = Guid.NewGuid();

    private HistoricoPagamentoGateway CriarGateway() =>
        new(_dynamoDbClientMock.Object, new DynamoDbSettings { TabelaHistoricoPagamentos = Tabela }, _loggerMock.Object);

    private EventoHistoricoPagamento CriarEvento(string? pagamentoExternoId = "1234567890", string? preferenciaId = "pref-1") => new(
        _ordemServicoId,
        OcorridoEm,
        TiposHistoricoPagamento.PagamentoAprovado,
        OrigensHistoricoPagamento.Notificacao,
        _pagamentoId,
        "MercadoPago",
        120.50m,
        pagamentoExternoId,
        preferenciaId);

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeFalha()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveGravarItemComChavesTtlEAtributosDoContrato()
    {
        Dictionary<string, AttributeValue>? gravado = null;
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(Tabela, It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, AttributeValue>, CancellationToken>((_, item, _) => gravado = item)
            .Returns(Task.CompletedTask);

        await CriarGateway().RegistrarAsync(CriarEvento());

        Assert.NotNull(gravado);
        Assert.Equal(_ordemServicoId.ToString("D"), gravado["OrdemServicoId"].S);
        Assert.Matches(new Regex("^2026-11-24T10:15:30\\.123Z#[0-9a-f]{8}$"), gravado["OcorridoEm"].S);
        Assert.Equal(new DateTimeOffset(OcorridoEm).AddDays(180).ToUnixTimeSeconds().ToString(), gravado["ExpiraEm"].N);
        Assert.Equal("PagamentoAprovado", gravado["Tipo"].S);
        Assert.Equal("notificacao", gravado["Origem"].S);
        Assert.Equal(_pagamentoId.ToString("D"), gravado["PagamentoId"].S);
        Assert.Equal("MercadoPago", gravado["Provedor"].S);
        Assert.Equal("120.50", gravado["Valor"].N);
        Assert.Equal("1234567890", gravado["PagamentoExternoId"].S);
        Assert.Equal("pref-1", gravado["PreferenciaId"].S);
        Assert.Equal(10, gravado.Count);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public void CriarItem_NaoDeveIncluirAtributosOpcionais_QuandoNaoInformados()
    {
        var item = HistoricoPagamentoGateway.CriarItem(CriarEvento(pagamentoExternoId: null, preferenciaId: " "));

        Assert.DoesNotContain("PagamentoExternoId", item.Keys);
        Assert.DoesNotContain("PreferenciaId", item.Keys);
        Assert.Equal(8, item.Count);
    }

    [Fact]
    public void CriarChaveOrdenacao_DeveSerUnicaNoMesmoInstanteEOrdenarCronologicamente()
    {
        var primeira = HistoricoPagamentoGateway.CriarChaveOrdenacao(OcorridoEm);
        var mesmoInstante = HistoricoPagamentoGateway.CriarChaveOrdenacao(OcorridoEm);
        var seguinte = HistoricoPagamentoGateway.CriarChaveOrdenacao(OcorridoEm.AddMilliseconds(1));

        Assert.NotEqual(primeira, mesmoInstante);
        Assert.True(string.CompareOrdinal(primeira, seguinte) < 0);
        Assert.True(string.CompareOrdinal(mesmoInstante, seguinte) < 0);
    }

    [Fact]
    public void Mapear_DeveRecuperarOEventoGravado()
    {
        var evento = CriarEvento();

        var mapeado = HistoricoPagamentoGateway.Mapear(HistoricoPagamentoGateway.CriarItem(evento));

        Assert.Equal(evento, mapeado);
        Assert.Equal(DateTimeKind.Utc, mapeado.OcorridoEm.Kind);
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarWarningSemLancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        await CriarGateway().RegistrarAsync(CriarEvento());

        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("registro_historico", propriedades["Operacao"]);
        Assert.Equal("PagamentoAprovado", propriedades["Tipo"]);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task RegistrarAsync_DevePropagarCancelamentoDaRequisicao()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await CriarGateway().RegistrarAsync(CriarEvento(), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public async Task ListarAsync_DeveConsultarAParticaoDaOrdemEMapearOsItens()
    {
        var cobranca = CriarEvento(pagamentoExternoId: null) with { Tipo = TiposHistoricoPagamento.CobrancaCriada, Origem = OrigensHistoricoPagamento.Aprovacao };
        var aprovacao = CriarEvento(preferenciaId: null) with { OcorridoEm = OcorridoEm.AddMinutes(2) };
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(Tabela, "OrdemServicoId", _ordemServicoId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([HistoricoPagamentoGateway.CriarItem(cobranca), HistoricoPagamentoGateway.CriarItem(aprovacao)]);

        var historico = await CriarGateway().ListarAsync(_ordemServicoId);

        Assert.Equal([cobranca, aprovacao], historico);
    }

    [Fact]
    public async Task ListarAsync_DeveRegistrarErroERelancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarGateway().ListarAsync(_ordemServicoId);

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("consulta_historico", propriedades["Operacao"]);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
    }
}
