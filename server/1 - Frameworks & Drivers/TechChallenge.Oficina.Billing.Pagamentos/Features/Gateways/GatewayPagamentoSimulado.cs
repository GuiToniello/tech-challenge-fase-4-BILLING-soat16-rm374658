using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;

// Usado quando MercadoPagoSettings:AccessToken nao esta configurado (ambiente local e demonstracao): a cobranca recebe um link ficticio
// e qualquer consulta devolve o pagamento como aprovado, para a saga seguir sem conta no Mercado Pago. Cada operacao registra pagamento.simulado.
public sealed class GatewayPagamentoSimulado : IGatewayPagamento
{
    public const string PrefixoPreferencia = "SIM-";
    public const string PrefixoPagamento = "SIM-PAY-";
    public const string UrlCheckout = "https://simulado.mercadopago.local/checkout/";

    private readonly ILogger<GatewayPagamentoSimulado> _logger;

    public GatewayPagamentoSimulado(ILogger<GatewayPagamentoSimulado> logger)
    {
        _logger = logger;
    }

    public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default)
    {
        var cobrancaCriada = new CobrancaCriada(
            $"{PrefixoPreferencia}{Guid.NewGuid()}",
            $"{UrlCheckout}{cobranca.OrdemServicoId}",
            ProvedorPagamento.Simulado);

        _logger.LogWarning(
            OrdemServicoLogEventos.PagamentoSimulado,
            "Cobranca simulada {PreferenciaId} criada para a ordem de servico {OrdemServicoId} com valor {Valor}: Mercado Pago nao configurado.",
            cobrancaCriada.PreferenciaId,
            cobranca.OrdemServicoId,
            cobranca.Valor);

        return Task.FromResult(cobrancaCriada);
    }

    public Task<SituacaoPagamentoExterno?> ConsultarPorReferenciaAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<SituacaoPagamentoExterno?>(CriarSituacaoAprovada(ordemServicoId));
    }

    // So reconhece os ids gerados pelo proprio simulado (SIM-PAY-{ordemServicoId:N}); qualquer outro e desconhecido
    public Task<SituacaoPagamentoExterno?> ConsultarPorIdAsync(string pagamentoExternoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pagamentoExternoId) ||
            !pagamentoExternoId.StartsWith(PrefixoPagamento, StringComparison.Ordinal) ||
            !Guid.TryParseExact(pagamentoExternoId[PrefixoPagamento.Length..], "N", out var ordemServicoId))
        {
            return Task.FromResult<SituacaoPagamentoExterno?>(null);
        }

        return Task.FromResult<SituacaoPagamentoExterno?>(CriarSituacaoAprovada(ordemServicoId));
    }

    public static string CriarPagamentoExternoId(Guid ordemServicoId)
    {
        return $"{PrefixoPagamento}{ordemServicoId:N}";
    }

    private SituacaoPagamentoExterno CriarSituacaoAprovada(Guid ordemServicoId)
    {
        var situacao = new SituacaoPagamentoExterno(CriarPagamentoExternoId(ordemServicoId), ordemServicoId, StatusPagamento.Aprovado, null);

        _logger.LogWarning(
            OrdemServicoLogEventos.PagamentoSimulado,
            "Pagamento simulado {PagamentoExternoId} da ordem de servico {OrdemServicoId} considerado aprovado: Mercado Pago nao configurado.",
            situacao.PagamentoExternoId,
            ordemServicoId);

        return situacao;
    }
}
