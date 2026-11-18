using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;

// Resposta da aprovacao: o orcamento (aguardando pagamento) e a cobranca com o link de pagamento para o cliente.
public sealed class AprovacaoOrcamentoViewModel
{
    public OrcamentoViewModel Orcamento { get; set; } = new();
    public PagamentoViewModel Pagamento { get; set; } = new();
}
