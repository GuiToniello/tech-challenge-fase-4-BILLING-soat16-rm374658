namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// Porta de saida para o historico de pagamentos (Amazon DynamoDB no Frameworks & Drivers).
// O historico e complementar e descartavel: a fonte da verdade continua no PostgreSQL e nenhuma regra le o historico para decidir algo.
public interface IHistoricoPagamentoGateway
{
    // Best-effort: falha de integracao nao lanca excecao (a implementacao registra integracao.falha e segue)
    Task RegistrarAsync(EventoHistoricoPagamento evento, CancellationToken cancellationToken = default);

    // Eventos da ordem de servico em ordem cronologica; vazio quando nao ha historico
    Task<IReadOnlyCollection<EventoHistoricoPagamento>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
}
