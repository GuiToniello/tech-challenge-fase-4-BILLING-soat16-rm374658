namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Publicado pelo Billing quando o cliente recusa o orcamento: o OS Service finaliza a OS sem execucao.
public sealed record OrcamentoRecusado(Guid OrdemServicoId);
