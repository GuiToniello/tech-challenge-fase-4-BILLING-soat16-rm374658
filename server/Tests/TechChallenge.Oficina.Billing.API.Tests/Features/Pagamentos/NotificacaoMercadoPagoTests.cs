using System.Text;
using Microsoft.AspNetCore.Http;
using TechChallenge.Oficina.Billing.API.Features.Pagamentos;
using Xunit;

namespace TechChallenge.Oficina.Billing.API.Tests.Features.Pagamentos;

public sealed class NotificacaoMercadoPagoTests
{
    private static HttpRequest CriarRequisicao(string query, string? corpo = null)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = HttpMethods.Post;
        contexto.Request.QueryString = new QueryString(query);

        if (corpo is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(corpo);
            contexto.Request.Body = new MemoryStream(bytes);
            contexto.Request.ContentLength = bytes.Length;
            contexto.Request.ContentType = "application/json";
        }

        return contexto.Request;
    }

    [Fact]
    public async Task LerAsync_DeveLerTipoEIdDaQueryDoWebhook()
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao("?type=payment&data.id=123"), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago("payment", "123"), notificacao);
    }

    [Fact]
    public async Task LerAsync_DeveLerTipoEIdDoCorpoDoWebhook()
    {
        var corpo = """{"action":"payment.updated","api_version":"v1","data":{"id":"98765"},"id":12345,"live_mode":false,"type":"payment"}""";

        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao(string.Empty, corpo), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago("payment", "98765"), notificacao);
    }

    [Fact]
    public async Task LerAsync_DeveAceitarIdNumericoNoCorpo()
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao(string.Empty, """{"type":"payment","data":{"id":98765}}"""), CancellationToken.None);

        Assert.Equal("98765", notificacao.RecursoId);
    }

    [Fact]
    public async Task LerAsync_DeveLerANotificacaoIpnLegada()
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao("?id=555&topic=payment"), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago("payment", "555"), notificacao);
    }

    [Fact]
    public async Task LerAsync_DevePriorizarAQuery_QuandoCorpoTambemInformado()
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao("?type=payment&data.id=111", """{"type":"merchant_order","data":{"id":"222"}}"""), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago("payment", "111"), notificacao);
    }

    [Theory]
    [InlineData("nao e json")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public async Task LerAsync_DeveIgnorarCorpoInvalido(string corpo)
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao("?topic=merchant_order&id=10", corpo), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago("merchant_order", "10"), notificacao);
    }

    [Fact]
    public async Task LerAsync_DeveRetornarVazio_QuandoSemDados()
    {
        var notificacao = await NotificacaoMercadoPago.LerAsync(CriarRequisicao(string.Empty), CancellationToken.None);

        Assert.Equal(new NotificacaoMercadoPago(null, null), notificacao);
    }
}
