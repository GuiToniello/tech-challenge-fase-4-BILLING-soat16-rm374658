using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos
{
    public interface IOrcamentosAdapter
    {
        object Adapt(OrcamentosResult<OrcamentoViewModel, Exception> result);

        object Adapt(OrcamentosResult<AprovacaoOrcamentoViewModel, Exception> result);

        object Adapt(OrcamentosResult<bool, Exception> result);

        object Empty();
    }
}
