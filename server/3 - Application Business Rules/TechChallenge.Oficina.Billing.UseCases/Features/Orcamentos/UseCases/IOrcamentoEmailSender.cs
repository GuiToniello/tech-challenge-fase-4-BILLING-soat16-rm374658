using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoEmailSender
{
    // linkPagamento: incluido no email quando ja existe uma cobranca pendente para o orcamento
    Task EnviarOrcamentoAsync(Orcamento orcamento, string emailDestino, string? linkPagamento = null, CancellationToken cancellationToken = default);
}
