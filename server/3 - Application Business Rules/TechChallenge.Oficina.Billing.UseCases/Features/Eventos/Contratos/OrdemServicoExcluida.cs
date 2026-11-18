namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Consumido: o OS Service excluiu a OS. Referencia e orcamento sao removidos; os pagamentos ficam como registro financeiro.
public sealed record OrdemServicoExcluida(Guid OrdemServicoId);
