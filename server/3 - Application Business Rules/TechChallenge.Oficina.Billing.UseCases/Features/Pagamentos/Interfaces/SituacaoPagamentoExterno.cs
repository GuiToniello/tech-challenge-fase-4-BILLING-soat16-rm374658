using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// OrdemServicoId vem da referencia externa da cobranca; Valor e o valor pago informado pelo provedor (quando conhecido).
public sealed record SituacaoPagamentoExterno(string PagamentoExternoId, Guid OrdemServicoId, StatusPagamento Situacao, decimal? Valor);
