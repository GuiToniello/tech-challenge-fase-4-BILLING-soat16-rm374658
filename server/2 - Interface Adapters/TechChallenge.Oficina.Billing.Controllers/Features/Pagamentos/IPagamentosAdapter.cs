using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos
{
    public interface IPagamentosAdapter
    {
        object Adapt(PagamentosResult<PagamentoViewModel, Exception> result);

        // Confirmacao da notificacao do provedor: sempre 200, para o Mercado Pago nao reenviar
        object Recebido();
    }
}
