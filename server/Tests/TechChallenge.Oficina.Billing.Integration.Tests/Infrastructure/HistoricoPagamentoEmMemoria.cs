using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;

/// <summary>
/// Historico de pagamentos em memoria no lugar do DynamoDB: uma lista com os eventos na ordem de registro.
/// </summary>
public sealed class HistoricoPagamentoEmMemoria : IHistoricoPagamentoGateway
{
    private readonly List<EventoHistoricoPagamento> _eventos = [];

    public IReadOnlyList<EventoHistoricoPagamento> Eventos => _eventos;

    public Task RegistrarAsync(EventoHistoricoPagamento evento, CancellationToken cancellationToken = default)
    {
        _eventos.Add(evento);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<EventoHistoricoPagamento>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<EventoHistoricoPagamento> eventos = _eventos
            .Where(evento => evento.OrdemServicoId == ordemServicoId)
            .OrderBy(evento => evento.OcorridoEm)
            .ToArray();

        return Task.FromResult(eventos);
    }
}
