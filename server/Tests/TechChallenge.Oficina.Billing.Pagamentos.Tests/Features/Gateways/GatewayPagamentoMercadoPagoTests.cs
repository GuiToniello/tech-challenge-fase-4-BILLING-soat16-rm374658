using System.Net;
using MercadoPago.Error;
using MercadoPago.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.Pagamentos.Features;
using TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;
using TechChallenge.Oficina.Billing.Pagamentos.Tests.Infrastructure;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;
using MpPayment = MercadoPago.Resource.Payment.Payment;

namespace TechChallenge.Oficina.Billing.Pagamentos.Tests.Features.Gateways;

public sealed class GatewayPagamentoMercadoPagoTests
{
    private readonly Mock<IMercadoPagoClient> _clientMock = new();
    private readonly Mock<ILogger<GatewayPagamentoMercadoPago>> _loggerMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private GatewayPagamentoMercadoPago CriarGateway() => new(_clientMock.Object, _loggerMock.Object);

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeFalha()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Theory]
    [InlineData("approved", StatusPagamento.Aprovado)]
    [InlineData("authorized", StatusPagamento.Aprovado)]
    [InlineData("pending", StatusPagamento.Pendente)]
    [InlineData("in_process", StatusPagamento.Pendente)]
    [InlineData("in_mediation", StatusPagamento.Pendente)]
    [InlineData("rejected", StatusPagamento.Recusado)]
    [InlineData("cancelled", StatusPagamento.Recusado)]
    [InlineData("refunded", StatusPagamento.Recusado)]
    [InlineData("charged_back", StatusPagamento.Recusado)]
    [InlineData("APPROVED", StatusPagamento.Aprovado)]
    [InlineData("status_novo", StatusPagamento.Pendente)]
    [InlineData(null, StatusPagamento.Pendente)]
    public void MapearSituacao_DeveConverterStatusDoMercadoPago(string? status, StatusPagamento esperado)
    {
        Assert.Equal(esperado, GatewayPagamentoMercadoPago.MapearSituacao(status));
    }

    [Fact]
    public async Task CriarCobrancaAsync_DeveDevolverAPreferenciaComoCobrancaDoMercadoPago()
    {
        var cobranca = new NovaCobranca(_ordemServicoId, 150m, "Orcamento", "cliente@teste.com");
        _clientMock.Setup(c => c.CriarPreferenciaAsync(cobranca, It.IsAny<CancellationToken>())).ReturnsAsync(("pref-1", "https://checkout/pref-1"));

        var criada = await CriarGateway().CriarCobrancaAsync(cobranca);

        Assert.Equal(new CobrancaCriada("pref-1", "https://checkout/pref-1", ProvedorPagamento.MercadoPago), criada);
    }

    [Fact]
    public async Task CriarCobrancaAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoMercadoPagoFalhar()
    {
        var falha = new MercadoPagoException("An unexpected error has occurred.");
        _clientMock.Setup(c => c.CriarPreferenciaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await CriarGateway().CriarCobrancaAsync(new NovaCobranca(_ordemServicoId, 150m, "Orcamento", null));

        Assert.Same(falha, await Assert.ThrowsAsync<MercadoPagoException>(action));
        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("mercadopago", propriedades["Integracao"]);
        Assert.Equal("criacao_cobranca", propriedades["Operacao"]);
        Assert.Equal(_ordemServicoId, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task ConsultarPorReferenciaAsync_DeveMapearOPagamentoMaisRecente()
    {
        _clientMock
            .Setup(c => c.BuscarUltimoPagamentoAsync(_ordemServicoId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MpPayment { Id = 123456, Status = "approved", ExternalReference = _ordemServicoId.ToString(), TransactionAmount = 150m });

        var situacao = await CriarGateway().ConsultarPorReferenciaAsync(_ordemServicoId);

        Assert.Equal(new SituacaoPagamentoExterno("123456", _ordemServicoId, StatusPagamento.Aprovado, 150m), situacao);
    }

    [Fact]
    public async Task ConsultarPorReferenciaAsync_DeveRetornarNull_QuandoAindaNaoHaPagamento()
    {
        _clientMock.Setup(c => c.BuscarUltimoPagamentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((MpPayment?)null);

        var situacao = await CriarGateway().ConsultarPorReferenciaAsync(_ordemServicoId);

        Assert.Null(situacao);
    }

    [Fact]
    public async Task ConsultarPorReferenciaAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoMercadoPagoFalhar()
    {
        var falha = new HttpRequestException("Timeout");
        _clientMock.Setup(c => c.BuscarUltimoPagamentoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await CriarGateway().ConsultarPorReferenciaAsync(_ordemServicoId);

        Assert.Same(falha, await Assert.ThrowsAsync<HttpRequestException>(action));
        var (_, propriedades, _) = ObterRegistroDeFalha();
        Assert.Equal("mercadopago", propriedades["Integracao"]);
        Assert.Equal("consulta_pagamento", propriedades["Operacao"]);
    }

    [Fact]
    public async Task ConsultarPorIdAsync_DeveMapearPagamentoDaOrdemDeServico()
    {
        _clientMock
            .Setup(c => c.ObterPagamentoAsync(987, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MpPayment { Id = 987, Status = "rejected", ExternalReference = _ordemServicoId.ToString(), TransactionAmount = 80m });

        var situacao = await CriarGateway().ConsultarPorIdAsync("987");

        Assert.Equal(new SituacaoPagamentoExterno("987", _ordemServicoId, StatusPagamento.Recusado, 80m), situacao);
    }

    [Theory]
    [InlineData("SIM-PAY-123")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("")]
    public async Task ConsultarPorIdAsync_DeveRetornarNullSemConsultar_QuandoIdNaoENumerico(string pagamentoExternoId)
    {
        var situacao = await CriarGateway().ConsultarPorIdAsync(pagamentoExternoId);

        Assert.Null(situacao);
        Assert.Empty(_clientMock.Invocations);
    }

    [Fact]
    public async Task ConsultarPorIdAsync_DeveRetornarNull_QuandoReferenciaExternaNaoEUmaOrdemDeServico()
    {
        _clientMock
            .Setup(c => c.ObterPagamentoAsync(987, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MpPayment { Id = 987, Status = "approved", ExternalReference = "pedido-loja-42" });

        var situacao = await CriarGateway().ConsultarPorIdAsync("987");

        Assert.Null(situacao);
    }

    [Fact]
    public async Task ConsultarPorIdAsync_DeveRetornarNullSemRegistrarFalha_QuandoMercadoPagoNaoConheceOPagamento()
    {
        // SDK real com HTTP falso: o 404 da API vira MPNotFoundException
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.NotFound, """{"message":"Payment not found","error":"not_found","status":404,"cause":[]}""");
        var client = new MercadoPagoClientAdapter(new MercadoPagoSettings { AccessToken = "TEST-token" }, new DefaultHttpClient(new HttpClient(handler)));
        var gateway = new GatewayPagamentoMercadoPago(client, _loggerMock.Object);

        var situacao = await gateway.ConsultarPorIdAsync("123");

        Assert.Null(situacao);
        Assert.Single(handler.Requisicoes);
        Assert.DoesNotContain(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log));
    }

    [Fact]
    public async Task ConsultarPorIdAsync_DeveRegistrarFalhaComIdExternoERelancar_QuandoMercadoPagoRecusaACredencial()
    {
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.Unauthorized, """{"message":"invalid access token","error":"unauthorized","status":401,"cause":[]}""");
        var client = new MercadoPagoClientAdapter(new MercadoPagoSettings { AccessToken = "TEST-token" }, new DefaultHttpClient(new HttpClient(handler)));
        var gateway = new GatewayPagamentoMercadoPago(client, _loggerMock.Object);

        var action = async () => await gateway.ConsultarPorIdAsync("123");

        await Assert.ThrowsAnyAsync<MercadoPagoApiException>(action);
        var (nivel, propriedades, _) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Equal("mercadopago", propriedades["Integracao"]);
        Assert.Equal("consulta_pagamento", propriedades["Operacao"]);
        Assert.Equal("123", propriedades["PagamentoExternoId"]);
    }

    [Fact]
    public async Task CriarCobrancaAsync_DeveRelancarSemRegistrarFalha_QuandoRequisicaoCancelada()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _clientMock
            .Setup(c => c.CriarPreferenciaAsync(It.IsAny<NovaCobranca>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await new GatewayPagamentoMercadoPago(_clientMock.Object, NullLogger<GatewayPagamentoMercadoPago>.Instance)
            .CriarCobrancaAsync(new NovaCobranca(_ordemServicoId, 10m, "Orcamento", null), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
    }
}
