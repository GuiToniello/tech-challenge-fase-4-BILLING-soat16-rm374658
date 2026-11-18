namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Publicado pelo Billing ao gerar (ou regerar) o orcamento: o OS Service passa a OS para aguardando aprovacao.
public sealed record OrcamentoGerado(Guid OrdemServicoId, decimal ValorTotal);
