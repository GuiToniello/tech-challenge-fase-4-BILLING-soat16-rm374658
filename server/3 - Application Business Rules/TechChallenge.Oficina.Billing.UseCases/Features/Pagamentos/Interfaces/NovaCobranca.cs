namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

public sealed record NovaCobranca(Guid OrdemServicoId, decimal Valor, string Descricao, string? EmailPagador);
