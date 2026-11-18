using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

public interface IPagamentoGateway
{
    Task AdicionarAsync(Pagamento pagamento, CancellationToken cancellationToken = default);
    Task AtualizarAsync(Pagamento pagamento, CancellationToken cancellationToken = default);

    // A cobranca mais recente da OS (a ultima criada); null quando o orcamento ainda nao foi aprovado
    Task<Pagamento?> ObterUltimoPorOrdemServicoIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
}
