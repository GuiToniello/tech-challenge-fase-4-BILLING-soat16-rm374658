namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// Porta de saida para o provedor de pagamento (Mercado Pago ou simulado, escolhido pela configuracao no Frameworks & Drivers).
public interface IGatewayPagamento
{
    // Cria a cobranca (preferencia do checkout) com a ordem de servico como referencia externa
    Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default);

    // Situacao do pagamento mais recente feito para a ordem de servico; null quando ainda nao houve pagamento
    Task<SituacaoPagamentoExterno?> ConsultarPorReferenciaAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);

    // Situacao de um pagamento pelo id no provedor (vindo da notificacao); null quando o provedor nao o conhece
    Task<SituacaoPagamentoExterno?> ConsultarPorIdAsync(string pagamentoExternoId, CancellationToken cancellationToken = default);
}
