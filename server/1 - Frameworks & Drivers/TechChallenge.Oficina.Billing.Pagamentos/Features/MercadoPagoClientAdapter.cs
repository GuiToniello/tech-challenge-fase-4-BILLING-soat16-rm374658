using MercadoPago.Client;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Http;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using MpPayment = MercadoPago.Resource.Payment.Payment;

namespace TechChallenge.Oficina.Billing.Pagamentos.Features;

internal sealed class MercadoPagoClientAdapter : IMercadoPagoClient
{
    public const string Moeda = "BRL";

    private readonly MercadoPagoSettings _settings;
    private readonly PreferenceClient _preferenceClient;
    private readonly PaymentClient _paymentClient;

    // O IHttpClient do SDK e injetado para os testes trocarem o HttpMessageHandler (sem chamadas reais ao Mercado Pago)
    public MercadoPagoClientAdapter(MercadoPagoSettings settings, IHttpClient httpClient)
    {
        _settings = settings;
        _preferenceClient = new PreferenceClient(httpClient);
        _paymentClient = new PaymentClient(httpClient);
    }

    public async Task<(string Id, string InitPoint)> CriarPreferenciaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default)
    {
        var preferencia = await _preferenceClient.CreateAsync(CriarPreferenceRequest(cobranca, _settings), CriarRequestOptions(), cancellationToken);
        return (preferencia.Id, preferencia.InitPoint);
    }

    public async Task<MpPayment?> BuscarUltimoPagamentoAsync(string referenciaExterna, CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest
        {
            Filters = new Dictionary<string, object>
            {
                ["external_reference"] = referenciaExterna,
                ["sort"] = "date_created",
                ["criteria"] = "desc"
            }
        };

        var pagina = await _paymentClient.SearchAsync(request, CriarRequestOptions(), cancellationToken);
        return pagina?.Results?.FirstOrDefault();
    }

    public Task<MpPayment> ObterPagamentoAsync(long id, CancellationToken cancellationToken = default)
    {
        return _paymentClient.GetAsync(id, CriarRequestOptions(), cancellationToken);
    }

    // Um item com o valor total do orcamento; a ordem de servico vai na referencia externa para achar o pagamento depois
    internal static PreferenceRequest CriarPreferenceRequest(NovaCobranca cobranca, MercadoPagoSettings settings)
    {
        var request = new PreferenceRequest
        {
            Items =
            [
                new PreferenceItemRequest
                {
                    Title = cobranca.Descricao,
                    Quantity = 1,
                    CurrencyId = Moeda,
                    UnitPrice = cobranca.Valor
                }
            ],
            ExternalReference = cobranca.OrdemServicoId.ToString()
        };

        if (!string.IsNullOrWhiteSpace(settings.NotificationUrl))
        {
            request.NotificationUrl = settings.NotificationUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(cobranca.EmailPagador))
        {
            request.Payer = new PreferencePayerRequest { Email = cobranca.EmailPagador };
        }

        return request;
    }

    // Um RequestOptions novo por chamada: o access token vai so nesta requisicao, sem o estado global do MercadoPagoConfig
    private RequestOptions CriarRequestOptions()
    {
        return new RequestOptions { AccessToken = _settings.AccessToken.Trim() };
    }
}
