using System.Globalization;
using MercadoPago.Error;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using MpPayment = MercadoPago.Resource.Payment.Payment;

namespace TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;

// Checkout Pro do Mercado Pago: a cobranca e uma preferencia com a ordem de servico na referencia externa.
// Falhas do provedor (MercadoPagoApiException, MercadoPagoException, timeout) sao registradas como integracao.falha e relancadas.
internal sealed class GatewayPagamentoMercadoPago : IGatewayPagamento
{
    private readonly IMercadoPagoClient _mercadoPagoClient;
    private readonly ILogger<GatewayPagamentoMercadoPago> _logger;

    public GatewayPagamentoMercadoPago(IMercadoPagoClient mercadoPagoClient, ILogger<GatewayPagamentoMercadoPago> logger)
    {
        _mercadoPagoClient = mercadoPagoClient;
        _logger = logger;
    }

    public async Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default)
    {
        try
        {
            var (preferenciaId, linkPagamento) = await _mercadoPagoClient.CriarPreferenciaAsync(cobranca, cancellationToken);
            return new CobrancaCriada(preferenciaId, linkPagamento, ProvedorPagamento.MercadoPago);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RegistrarFalha(exception, OrdemServicoLogEventos.Operacoes.CriacaoCobranca, cobranca.OrdemServicoId);
            throw;
        }
    }

    public async Task<SituacaoPagamentoExterno?> ConsultarPorReferenciaAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var pagamento = await _mercadoPagoClient.BuscarUltimoPagamentoAsync(ordemServicoId.ToString(), cancellationToken);
            return pagamento is null ? null : Mapear(pagamento);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RegistrarFalha(exception, OrdemServicoLogEventos.Operacoes.ConsultaPagamento, ordemServicoId);
            throw;
        }
    }

    public async Task<SituacaoPagamentoExterno?> ConsultarPorIdAsync(string pagamentoExternoId, CancellationToken cancellationToken = default)
    {
        // Os ids de pagamento do Mercado Pago sao numericos: outro formato nao e um pagamento conhecido
        if (!long.TryParse(pagamentoExternoId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        try
        {
            var pagamento = await _mercadoPagoClient.ObterPagamentoAsync(id, cancellationToken);
            return Mapear(pagamento);
        }
        catch (MPNotFoundException)
        {
            // Id que o Mercado Pago nao conhece (ex.: notificacao de teste com id ficticio)
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do pagamento externo {PagamentoExternoId}.",
                OrdemServicoLogEventos.Integracoes.MercadoPago,
                OrdemServicoLogEventos.Operacoes.ConsultaPagamento,
                pagamentoExternoId);
            throw;
        }
    }

    // Pagamento sem id ou com referencia externa que nao e uma OS (ex.: outra integracao da mesma conta) e desconhecido para o Billing
    internal static SituacaoPagamentoExterno? Mapear(MpPayment pagamento)
    {
        if (pagamento.Id is null || !Guid.TryParse(pagamento.ExternalReference, out var ordemServicoId))
        {
            return null;
        }

        return new SituacaoPagamentoExterno(
            pagamento.Id.Value.ToString(CultureInfo.InvariantCulture),
            ordemServicoId,
            MapearSituacao(pagamento.Status),
            pagamento.TransactionAmount);
    }

    // Status do Mercado Pago: aprovado, recusado (inclui cancelado, estornado e chargeback) ou ainda pendente (inclui status desconhecido)
    internal static StatusPagamento MapearSituacao(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "approved" or "authorized" => StatusPagamento.Aprovado,
            "rejected" or "cancelled" or "refunded" or "charged_back" => StatusPagamento.Recusado,
            _ => StatusPagamento.Pendente
        };
    }

    private void RegistrarFalha(Exception exception, string operacao, Guid ordemServicoId)
    {
        _logger.LogError(
            OrdemServicoLogEventos.IntegracaoFalha,
            exception,
            "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId}.",
            OrdemServicoLogEventos.Integracoes.MercadoPago,
            operacao,
            ordemServicoId);
    }
}
