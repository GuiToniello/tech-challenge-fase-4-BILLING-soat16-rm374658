namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Consumido: a Management rejeitou a OS (cliente, veiculo ou servico inexistente) e o OS Service a cancela. A referencia local e removida.
public sealed record OrdemServicoRejeitada(Guid OrdemServicoId, string? Motivo);
