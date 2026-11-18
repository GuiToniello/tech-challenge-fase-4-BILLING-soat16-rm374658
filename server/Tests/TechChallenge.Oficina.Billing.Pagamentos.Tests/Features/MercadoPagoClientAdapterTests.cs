using System.Net;
using System.Text.Json;
using DefaultHttpClient = MercadoPago.Http.DefaultHttpClient;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.Pagamentos.Features;
using TechChallenge.Oficina.Billing.Pagamentos.Tests.Infrastructure;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Billing.Pagamentos.Tests.Features;

public sealed class MercadoPagoClientAdapterTests
{
    private const string AccessToken = "TEST-token-de-teste";

    private static MercadoPagoClientAdapter CriarAdapter(HttpMessageHandlerFalso handler, string notificationUrl = "https://billing.exemplo.com/api/pagamentos/notificacoes") =>
        new(new MercadoPagoSettings { AccessToken = AccessToken, NotificationUrl = notificationUrl }, new DefaultHttpClient(new HttpClient(handler)));

    [Fact]
    public async Task CriarPreferenciaAsync_DeveEnviarItemReferenciaNotificacaoEPagadorComOAccessToken()
    {
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.Created, """{"id":"123-pref","init_point":"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-pref"}""");
        var ordemServicoId = Guid.NewGuid();

        var (id, link) = await CriarAdapter(handler).CriarPreferenciaAsync(new NovaCobranca(ordemServicoId, 150.5m, "Orçamento da ordem de serviço ABC", "cliente@teste.com"));

        Assert.Equal("123-pref", id);
        Assert.Equal("https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-pref", link);
        var requisicao = Assert.Single(handler.Requisicoes);
        Assert.Equal(HttpMethod.Post, requisicao.Metodo);
        Assert.Equal("https://api.mercadopago.com/checkout/preferences", requisicao.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {AccessToken}", requisicao.Autorizacao);

        using var corpo = JsonDocument.Parse(requisicao.Corpo);
        var item = Assert.Single(corpo.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Orçamento da ordem de serviço ABC", item.GetProperty("title").GetString());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
        Assert.Equal("BRL", item.GetProperty("currency_id").GetString());
        Assert.Equal(150.5m, item.GetProperty("unit_price").GetDecimal());
        Assert.Equal(ordemServicoId.ToString(), corpo.RootElement.GetProperty("external_reference").GetString());
        Assert.Equal("https://billing.exemplo.com/api/pagamentos/notificacoes", corpo.RootElement.GetProperty("notification_url").GetString());
        Assert.Equal("cliente@teste.com", corpo.RootElement.GetProperty("payer").GetProperty("email").GetString());
    }

    [Fact]
    public async Task CriarPreferenciaAsync_NaoDeveEnviarNotificacaoNemPagador_QuandoNaoInformados()
    {
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.Created, """{"id":"123-pref","init_point":"https://checkout"}""");

        await CriarAdapter(handler, notificationUrl: " ").CriarPreferenciaAsync(new NovaCobranca(Guid.NewGuid(), 10m, "Orcamento", null));

        using var corpo = JsonDocument.Parse(Assert.Single(handler.Requisicoes).Corpo);
        Assert.False(corpo.RootElement.TryGetProperty("notification_url", out _));
        Assert.False(corpo.RootElement.TryGetProperty("payer", out _));
    }

    [Fact]
    public async Task BuscarUltimoPagamentoAsync_DeveFiltrarPelaReferenciaOrdenandoDoMaisRecente()
    {
        var ordemServicoId = Guid.NewGuid();
        var handler = new HttpMessageHandlerFalso(
            HttpStatusCode.OK,
            $$"""{"paging":{"total":2,"limit":30,"offset":0},"results":[{"id":222,"status":"approved","external_reference":"{{ordemServicoId}}","transaction_amount":150.5},{"id":111,"status":"rejected","external_reference":"{{ordemServicoId}}","transaction_amount":150.5}]}""");

        var pagamento = await CriarAdapter(handler).BuscarUltimoPagamentoAsync(ordemServicoId.ToString());

        Assert.NotNull(pagamento);
        Assert.Equal(222, pagamento!.Id);
        Assert.Equal("approved", pagamento.Status);
        var requisicao = Assert.Single(handler.Requisicoes);
        Assert.Equal(HttpMethod.Get, requisicao.Metodo);
        Assert.Equal("/v1/payments/search", requisicao.Uri.AbsolutePath);
        Assert.Contains($"external_reference={ordemServicoId}", requisicao.Uri.Query);
        Assert.Contains("sort=date_created", requisicao.Uri.Query);
        Assert.Contains("criteria=desc", requisicao.Uri.Query);
        Assert.Equal($"Bearer {AccessToken}", requisicao.Autorizacao);
    }

    [Fact]
    public async Task BuscarUltimoPagamentoAsync_DeveRetornarNull_QuandoNaoHaPagamentos()
    {
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.OK, """{"paging":{"total":0,"limit":30,"offset":0},"results":[]}""");

        var pagamento = await CriarAdapter(handler).BuscarUltimoPagamentoAsync(Guid.NewGuid().ToString());

        Assert.Null(pagamento);
    }

    [Fact]
    public async Task ObterPagamentoAsync_DeveConsultarPeloIdComOAccessToken()
    {
        var handler = new HttpMessageHandlerFalso(HttpStatusCode.OK, """{"id":123456,"status":"pending","external_reference":"abc","transaction_amount":99.9}""");

        var pagamento = await CriarAdapter(handler).ObterPagamentoAsync(123456);

        Assert.Equal(123456, pagamento.Id);
        Assert.Equal("pending", pagamento.Status);
        Assert.Equal(99.9m, pagamento.TransactionAmount);
        var requisicao = Assert.Single(handler.Requisicoes);
        Assert.Equal("https://api.mercadopago.com/v1/payments/123456", requisicao.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {AccessToken}", requisicao.Autorizacao);
    }
}
