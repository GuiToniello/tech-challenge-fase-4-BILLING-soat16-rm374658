using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using MpPayment = MercadoPago.Resource.Payment.Payment;

namespace TechChallenge.Oficina.Billing.Pagamentos.Features;

// Recorte do SDK do Mercado Pago usado pelo Billing: isola o SDK e simplifica os testes do gateway.
internal interface IMercadoPagoClient
{
    // Cria a preferencia do Checkout Pro e devolve o id e o link de pagamento (init_point)
    Task<(string Id, string InitPoint)> CriarPreferenciaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default);

    // Pagamento mais recente feito com a referencia externa (id da ordem de servico); null quando nao ha pagamento
    Task<MpPayment?> BuscarUltimoPagamentoAsync(string referenciaExterna, CancellationToken cancellationToken = default);

    Task<MpPayment> ObterPagamentoAsync(long id, CancellationToken cancellationToken = default);
}
