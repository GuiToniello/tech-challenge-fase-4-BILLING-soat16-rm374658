using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

public interface IPagamentoUseCases
{
    Task<PagamentoViewModel> ObterPorOrdemServicoIdAsync(ObterPagamentoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default);
    Task<PagamentoViewModel> VerificarAsync(VerificarPagamentoCommand command, CancellationToken cancellationToken = default);
    Task ProcessarNotificacaoAsync(string pagamentoExternoId, CancellationToken cancellationToken = default);

    // Historico de pagamentos da OS (DynamoDB) em ordem cronologica; vazio quando nao ha registros
    Task<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>> ListarHistoricoAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
}
