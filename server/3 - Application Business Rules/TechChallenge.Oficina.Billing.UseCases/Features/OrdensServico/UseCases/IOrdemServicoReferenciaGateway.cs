using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;

namespace TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoReferenciaGateway
{
    Task AdicionarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);
    Task AtualizarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);
    Task<OrdemServicoReferencia?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
    Task RemoverAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);
}
