using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos
{
    public class PagamentosController : IPagamentosController
    {
        // Tipo de notificacao do Mercado Pago que interessa ao Billing (type no webhook, topic na notificacao IPN legada)
        public const string TipoNotificacaoPagamento = "payment";

        private readonly IPagamentoUseCases _pagamentoUseCases;
        private readonly IPagamentosAdapter _pagamentosAdapter;
        private readonly ILogger<PagamentosController> _logger;

        public PagamentosController(
            IPagamentoUseCases pagamentoUseCases,
            IPagamentosAdapter pagamentosAdapter,
            ILogger<PagamentosController> logger)
        {
            _pagamentoUseCases = pagamentoUseCases;
            _pagamentosAdapter = pagamentosAdapter;
            _logger = logger;
        }

        public async Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ObterPagamentoPorOrdemServicoIdQuery { OrdemServicoId = ordemServicoId };
                var pagamento = await _pagamentoUseCases.ObterPorOrdemServicoIdAsync(query, cancellationToken);
                var result = PagamentosResult.From(pagamento);

                return _pagamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                var result = PagamentosResult.FromError<PagamentoViewModel>(exception);
                return _pagamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Verificar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var pagamento = await _pagamentoUseCases.VerificarAsync(new VerificarPagamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = PagamentosResult.From(pagamento);

                return _pagamentosAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.PagamentoVerificacao, ordemServicoId, exception);
                var result = PagamentosResult.FromError<PagamentoViewModel>(exception);
                return _pagamentosAdapter.Adapt(result);
            }
        }

        // Sempre responde 200: a situacao real e consultada no provedor e, se o processamento falhar, o pagamento ainda pode ser conferido em /verificar
        public async Task<object> ReceberNotificacao(string? tipo, string? recursoId, CancellationToken cancellationToken)
        {
            if (!string.Equals(tipo, TipoNotificacaoPagamento, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(recursoId))
            {
                _logger.LogInformation("Notificacao do provedor de pagamento ignorada (tipo {Tipo}, recurso {RecursoId}).", tipo, recursoId);
                return _pagamentosAdapter.Recebido();
            }

            try
            {
                await _pagamentoUseCases.ProcessarNotificacaoAsync(recursoId, cancellationToken);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                var tipoFalha = exception is KeyNotFoundException
                    ? OrdemServicoLogEventos.TiposFalha.NaoEncontrado
                    : OrdemServicoLogEventos.TiposFalha.RegraNegocio;

                _logger.LogWarning(
                    OrdemServicoLogEventos.OsFalha,
                    "Falha na etapa {Etapa} do pagamento externo {PagamentoExternoId} ({TipoFalha}): {Motivo}",
                    OrdemServicoLogEventos.Etapas.PagamentoNotificacao,
                    recursoId,
                    tipoFalha,
                    exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Falha do provedor ja sai como integracao.falha no gateway; aqui fica o erro do processamento da notificacao
                _logger.LogError(
                    exception,
                    "Falha na etapa {Etapa} do pagamento externo {PagamentoExternoId}; a notificacao foi confirmada e o pagamento pode ser conferido em /verificar.",
                    OrdemServicoLogEventos.Etapas.PagamentoNotificacao,
                    recursoId);
            }

            return _pagamentosAdapter.Recebido();
        }

        private void RegistrarFalha(string etapa, Guid ordemServicoId, Exception exception)
        {
            var tipoFalha = exception is KeyNotFoundException
                ? OrdemServicoLogEventos.TiposFalha.NaoEncontrado
                : OrdemServicoLogEventos.TiposFalha.RegraNegocio;

            // Rejeicao de regra de negocio nao e erro de aplicacao: registra so o motivo, sem stack trace
            _logger.LogWarning(
                OrdemServicoLogEventos.OsFalha,
                "Falha na etapa {Etapa} da ordem de servico {OrdemServicoId} ({TipoFalha}): {Motivo}",
                etapa,
                ordemServicoId,
                tipoFalha,
                exception.Message);
        }
    }
}
