namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// Um registro do historico de pagamentos da OS. Tipo e Origem usam os valores de TiposHistoricoPagamento e OrigensHistoricoPagamento.
// Sem dados pessoais (nunca o email do cliente).
public sealed record EventoHistoricoPagamento(
    Guid OrdemServicoId,
    DateTime OcorridoEm,
    string Tipo,
    string Origem,
    Guid PagamentoId,
    string Provedor,
    decimal Valor,
    string? PagamentoExternoId,
    string? PreferenciaId);
