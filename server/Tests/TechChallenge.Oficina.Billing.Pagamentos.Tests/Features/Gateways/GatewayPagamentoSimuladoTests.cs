using Microsoft.Extensions.Logging;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Billing.Pagamentos.Tests.Features.Gateways;

public sealed class GatewayPagamentoSimuladoTests
{
    private readonly Mock<ILogger<GatewayPagamentoSimulado>> _loggerMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private GatewayPagamentoSimulado CriarGateway() => new(_loggerMock.Object);

    private IReadOnlyDictionary<string, object?> ObterRegistroSimulado()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.PagamentoSimulado.Id);
        Assert.Equal(LogLevel.Warning, (LogLevel)invocacao.Arguments[0]);

        return ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);
    }

    [Fact]
    public async Task CriarCobrancaAsync_DeveGerarCobrancaComLinkSimuladoERegistrarPagamentoSimulado()
    {
        var cobranca = await CriarGateway().CriarCobrancaAsync(new NovaCobranca(_ordemServicoId, 150m, "Orcamento", "cliente@teste.com"));

        Assert.StartsWith("SIM-", cobranca.PreferenciaId);
        Assert.True(Guid.TryParse(cobranca.PreferenciaId["SIM-".Length..], out _));
        Assert.Equal($"https://simulado.mercadopago.local/checkout/{_ordemServicoId}", cobranca.LinkPagamento);
        Assert.Equal(ProvedorPagamento.Simulado, cobranca.Provedor);
        var propriedades = ObterRegistroSimulado();
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.Equal(cobranca.PreferenciaId, propriedades["PreferenciaId"]);
    }

    [Fact]
    public async Task CriarCobrancaAsync_DeveGerarPreferenciaDiferenteACadaCobranca()
    {
        var gateway = CriarGateway();

        var primeira = await gateway.CriarCobrancaAsync(new NovaCobranca(_ordemServicoId, 150m, "Orcamento", null));
        var segunda = await gateway.CriarCobrancaAsync(new NovaCobranca(_ordemServicoId, 150m, "Orcamento", null));

        Assert.NotEqual(primeira.PreferenciaId, segunda.PreferenciaId);
    }

    [Fact]
    public async Task ConsultarPorReferenciaAsync_DeveConsiderarOPagamentoAprovado()
    {
        var situacao = await CriarGateway().ConsultarPorReferenciaAsync(_ordemServicoId);

        Assert.Equal(new SituacaoPagamentoExterno($"SIM-PAY-{_ordemServicoId:N}", _ordemServicoId, StatusPagamento.Aprovado, null), situacao);
        Assert.Equal($"SIM-PAY-{_ordemServicoId:N}", ObterRegistroSimulado()["PagamentoExternoId"]);
    }

    [Fact]
    public async Task ConsultarPorIdAsync_DeveReconhecerOIdGeradoPeloSimulado()
    {
        var situacao = await CriarGateway().ConsultarPorIdAsync(GatewayPagamentoSimulado.CriarPagamentoExternoId(_ordemServicoId));

        Assert.NotNull(situacao);
        Assert.Equal(_ordemServicoId, situacao!.OrdemServicoId);
        Assert.Equal(StatusPagamento.Aprovado, situacao.Situacao);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("SIM-PAY-nao-e-guid")]
    [InlineData("SIM-123")]
    [InlineData("")]
    public async Task ConsultarPorIdAsync_DeveRetornarNull_QuandoIdDesconhecido(string pagamentoExternoId)
    {
        var situacao = await CriarGateway().ConsultarPorIdAsync(pagamentoExternoId);

        Assert.Null(situacao);
        Assert.Empty(_loggerMock.Invocations);
    }
}
