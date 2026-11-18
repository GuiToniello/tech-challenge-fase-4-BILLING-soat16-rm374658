using System.Net;
using System.Text;

namespace TechChallenge.Oficina.Billing.Pagamentos.Tests.Infrastructure;

// Substitui a rede nas chamadas do SDK do Mercado Pago: registra cada requisicao e devolve a resposta configurada.
public sealed class HttpMessageHandlerFalso : HttpMessageHandler
{
    public sealed record RequisicaoRegistrada(HttpMethod Metodo, Uri Uri, string? Autorizacao, string Corpo);

    private readonly HttpStatusCode _status;
    private readonly string _resposta;

    public HttpMessageHandlerFalso(HttpStatusCode status, string resposta)
    {
        _status = status;
        _resposta = resposta;
    }

    public List<RequisicaoRegistrada> Requisicoes { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requisicoes.Add(new RequisicaoRegistrada(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), corpo));

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_resposta, Encoding.UTF8, "application/json")
        };
    }
}
