using Microsoft.AspNetCore.Http;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.Adapters.Features.Pagamentos
{
    public class PagamentosAdapter : IPagamentosAdapter
    {
        public object Adapt(PagamentosResult<PagamentoViewModel, Exception> result)
        {
            if (result.Value != null)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object Recebido()
        {
            return TypedResults.Ok();
        }

        public object CriaErro(Exception ex)
        {
            if (ex is DomainException)
            {
                return TypedResults.BadRequest(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            if (ex is KeyNotFoundException)
            {
                return TypedResults.NotFound(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            return TypedResults.Problem(ex?.Message);
        }
    }
}
