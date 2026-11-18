namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Publicado pelo Billing uma unica vez, quando o provedor confirma o pagamento do orcamento: a OS segue para execucao.
public sealed record PagamentoAprovado(Guid OrdemServicoId, Guid PagamentoId, decimal Valor);
