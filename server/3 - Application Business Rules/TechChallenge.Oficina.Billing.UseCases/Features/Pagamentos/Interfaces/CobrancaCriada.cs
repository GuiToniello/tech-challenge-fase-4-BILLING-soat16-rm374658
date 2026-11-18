using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

public sealed record CobrancaCriada(string PreferenciaId, string LinkPagamento, ProvedorPagamento Provedor);
