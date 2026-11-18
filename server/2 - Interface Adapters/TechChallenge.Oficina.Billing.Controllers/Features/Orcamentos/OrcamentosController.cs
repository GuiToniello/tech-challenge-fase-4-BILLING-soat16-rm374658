using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos
{
    public class OrcamentosController : IOrcamentosController
    {
        private readonly IOrcamentoUseCases _orcamentoUseCases;
        private readonly IOrcamentosAdapter _orcamentosAdapter;
        private readonly ILogger<OrcamentosController> _logger;

        public OrcamentosController(
            IOrcamentoUseCases orcamentoUseCases,
            IOrcamentosAdapter orcamentosAdapter,
            ILogger<OrcamentosController> logger)
        {
            _orcamentoUseCases = orcamentoUseCases;
            _orcamentosAdapter = orcamentosAdapter;
            _logger = logger;
        }

        public async Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var query = new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = ordemServicoId };
                var orcamento = await _orcamentoUseCases.ObterPorOrdemServicoIdAsync(query, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (KeyNotFoundException exception)
            {
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Gerar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var orcamento = await _orcamentoUseCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoGeracao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Enviar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                await _orcamentoUseCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);

                return _orcamentosAdapter.Empty();
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoEnvio, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<bool>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Aprovar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var aprovacao = await _orcamentoUseCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(aprovacao);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoAprovacao, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<AprovacaoOrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
        }

        public async Task<object> Recusar(Guid ordemServicoId, CancellationToken cancellationToken)
        {
            try
            {
                var orcamento = await _orcamentoUseCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = ordemServicoId }, cancellationToken);
                var result = OrcamentosResult.From(orcamento);

                return _orcamentosAdapter.Adapt(result);
            }
            catch (Exception exception) when (exception is DomainException or KeyNotFoundException)
            {
                RegistrarFalha(OrdemServicoLogEventos.Etapas.OrcamentoRecusa, ordemServicoId, exception);
                var result = OrcamentosResult.FromError<OrcamentoViewModel>(exception);
                return _orcamentosAdapter.Adapt(result);
            }
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
