using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;

public interface IOrdemServicoEventosUseCases
{
    Task<ResultadoProcessamentoEvento> ProcessarValidadaAsync(OrdemServicoValidada evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> ProcessarStatusAlteradoAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> ProcessarExcluidaAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> ProcessarRejeitadaAsync(OrdemServicoRejeitada evento, CancellationToken cancellationToken = default);
}
