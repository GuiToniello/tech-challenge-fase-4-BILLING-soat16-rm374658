using AutoMapper;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

// Acompanha a cobranca no provedor: pela verificacao manual (/verificar) ou pela notificacao do Mercado Pago (webhook).
// A situacao sempre e consultada no provedor (a notificacao so informa o id), e o PagamentoAprovado e publicado uma unica vez.
// Cada verificacao e notificacao processada vai para o historico de pagamentos (DynamoDB), sempre depois da gravacao e da publicacao.
public sealed class PagamentoUseCases : IPagamentoUseCases
{
    private readonly IMapper _mapper;
    private readonly IPagamentoGateway _pagamentoGateway;
    private readonly IOrcamentoGateway _orcamentoGateway;
    private readonly IGatewayPagamento _gatewayPagamento;
    private readonly IEventosPublisher _eventosPublisher;
    private readonly IHistoricoPagamentoGateway _historicoPagamentoGateway;
    private readonly ILogger<PagamentoUseCases> _logger;

    public PagamentoUseCases(
        IMapper mapper,
        IPagamentoGateway pagamentoGateway,
        IOrcamentoGateway orcamentoGateway,
        IGatewayPagamento gatewayPagamento,
        IEventosPublisher eventosPublisher,
        IHistoricoPagamentoGateway historicoPagamentoGateway,
        ILogger<PagamentoUseCases> logger)
    {
        _mapper = mapper;
        _pagamentoGateway = pagamentoGateway;
        _orcamentoGateway = orcamentoGateway;
        _gatewayPagamento = gatewayPagamento;
        _eventosPublisher = eventosPublisher;
        _historicoPagamentoGateway = historicoPagamentoGateway;
        _logger = logger;
    }

    public async Task<PagamentoViewModel> ObterPorOrdemServicoIdAsync(ObterPagamentoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default)
    {
        var pagamento = await ObterUltimoPagamentoExistenteAsync(query.OrdemServicoId, cancellationToken);
        return _mapper.Map<PagamentoViewModel>(pagamento);
    }

    public async Task<PagamentoViewModel> VerificarAsync(VerificarPagamentoCommand command, CancellationToken cancellationToken = default)
    {
        var pagamento = await ObterUltimoPagamentoExistenteAsync(command.OrdemServicoId, cancellationToken);

        // Ja aprovado: nada a consultar, publicar ou registrar de novo
        if (pagamento.Status != StatusPagamento.Aprovado)
        {
            var situacao = await _gatewayPagamento.ConsultarPorReferenciaAsync(pagamento.OrdemServicoId, cancellationToken);

            if (situacao is not null)
            {
                await AplicarSituacaoAsync(pagamento, situacao, cancellationToken);
            }

            // Sem pagamento no provedor (situacao nula) a cobranca segue pendente
            await RegistrarResultadoAsync(pagamento, OrigensHistoricoPagamento.Verificacao, situacao?.PagamentoExternoId);
        }

        return _mapper.Map<PagamentoViewModel>(pagamento);
    }

    public async Task ProcessarNotificacaoAsync(string pagamentoExternoId, CancellationToken cancellationToken = default)
    {
        var recebidaEm = DateTime.UtcNow;
        var situacao = await _gatewayPagamento.ConsultarPorIdAsync(pagamentoExternoId, cancellationToken);

        if (situacao is null)
        {
            _logger.LogInformation("Notificacao do pagamento externo {PagamentoExternoId} ignorada: pagamento desconhecido no provedor.", pagamentoExternoId);
            return;
        }

        var pagamento = await _pagamentoGateway.ObterUltimoPorOrdemServicoIdAsync(situacao.OrdemServicoId, cancellationToken);

        if (pagamento is null || pagamento.Status == StatusPagamento.Aprovado)
        {
            _logger.LogInformation(
                "Notificacao do pagamento externo {PagamentoExternoId} ignorada: a ordem de servico {OrdemServicoId} nao tem cobranca aguardando confirmacao.",
                pagamentoExternoId,
                situacao.OrdemServicoId);

            // Notificacao repetida de pagamento ja aprovado: fica no historico, sem novo resultado
            if (pagamento is not null)
            {
                await RegistrarHistoricoAsync(pagamento, TiposHistoricoPagamento.NotificacaoRecebida, OrigensHistoricoPagamento.Notificacao, situacao.PagamentoExternoId, recebidaEm);
            }

            return;
        }

        await AplicarSituacaoAsync(pagamento, situacao, cancellationToken);

        // A notificacao fica com o horario em que chegou; o resultado e registrado depois dela
        await RegistrarHistoricoAsync(pagamento, TiposHistoricoPagamento.NotificacaoRecebida, OrigensHistoricoPagamento.Notificacao, situacao.PagamentoExternoId, recebidaEm);
        await RegistrarResultadoAsync(pagamento, OrigensHistoricoPagamento.Notificacao, situacao.PagamentoExternoId);
    }

    public async Task<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>> ListarHistoricoAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        var historico = await _historicoPagamentoGateway.ListarAsync(ordemServicoId, cancellationToken);

        return historico
            .OrderBy(evento => evento.OcorridoEm)
            .Select(evento => _mapper.Map<EventoHistoricoPagamentoViewModel>(evento))
            .ToArray();
    }

    private async Task AplicarSituacaoAsync(Pagamento pagamento, SituacaoPagamentoExterno situacao, CancellationToken cancellationToken)
    {
        switch (situacao.Situacao)
        {
            case StatusPagamento.Aprovado:
                await AprovarAsync(pagamento, situacao, cancellationToken);
                break;

            case StatusPagamento.Recusado when pagamento.Status == StatusPagamento.Pendente:
                pagamento.Recusar(situacao.PagamentoExternoId);
                await _pagamentoGateway.AtualizarAsync(pagamento, cancellationToken);

                // O orcamento continua aguardando pagamento: uma nova aprovacao cria outra cobranca
                _logger.LogWarning(
                    OrdemServicoLogEventos.PagamentoRecusado,
                    "Pagamento {PagamentoId} da ordem de servico {OrdemServicoId} recusado no provedor {Provedor} (pagamento externo {PagamentoExternoId}).",
                    pagamento.Id,
                    pagamento.OrdemServicoId,
                    pagamento.Provedor,
                    situacao.PagamentoExternoId);
                break;

            // Pendente no provedor (ou recusa ja registrada): nada muda
        }
    }

    private async Task AprovarAsync(Pagamento pagamento, SituacaoPagamentoExterno situacao, CancellationToken cancellationToken)
    {
        pagamento.Aprovar(situacao.PagamentoExternoId);

        var orcamento = await _orcamentoGateway.ObterPorIdAsync(pagamento.OrdemServicoId, cancellationToken);
        var orcamentoConfirmado = orcamento is { Status: StatusOrcamento.AguardandoPagamento };

        if (orcamentoConfirmado)
        {
            orcamento!.ConfirmarPagamento();
        }

        // O status do pagamento e token de concorrencia: duas notificacoes simultaneas nao aprovam (nem publicam) duas vezes
        await _pagamentoGateway.AtualizarAsync(pagamento, cancellationToken);

        if (orcamentoConfirmado)
        {
            await _orcamentoGateway.AtualizarAsync(orcamento!, cancellationToken);
        }

        _logger.LogInformation(
            OrdemServicoLogEventos.PagamentoAprovado,
            "Pagamento {PagamentoId} da ordem de servico {OrdemServicoId} aprovado no provedor {Provedor} (pagamento externo {PagamentoExternoId}) com valor {Valor}.",
            pagamento.Id,
            pagamento.OrdemServicoId,
            pagamento.Provedor,
            pagamento.PagamentoExternoId,
            situacao.Valor ?? pagamento.Valor);

        if (!orcamentoConfirmado)
        {
            // Ex.: orcamento recusado ou removido depois da cobranca: o pagamento fica registrado, mas a OS nao segue para execucao
            _logger.LogWarning(
                "Pagamento {PagamentoId} aprovado sem orcamento aguardando pagamento na ordem de servico {OrdemServicoId} (status do orcamento: {StatusOrcamento}); PagamentoAprovado nao publicado.",
                pagamento.Id,
                pagamento.OrdemServicoId,
                orcamento?.Status.ToString() ?? "inexistente");
            return;
        }

        // Ja gravado: a publicacao nao e cancelada junto com a requisicao, senao o evento se perderia (a nova verificacao veria o pagamento ja aprovado)
        await _eventosPublisher.PublicarAsync(
            new PagamentoAprovado(pagamento.OrdemServicoId, pagamento.Id, pagamento.Valor),
            pagamento.OrdemServicoId,
            CancellationToken.None);
    }

    // Resultado da verificacao ou da notificacao: o status da cobranca depois de aplicada a situacao do provedor
    private Task RegistrarResultadoAsync(Pagamento pagamento, string origem, string? pagamentoExternoId)
    {
        var tipo = pagamento.Status switch
        {
            StatusPagamento.Aprovado => TiposHistoricoPagamento.PagamentoAprovado,
            StatusPagamento.Recusado => TiposHistoricoPagamento.PagamentoRecusado,
            _ => TiposHistoricoPagamento.PagamentoPendente
        };

        return RegistrarHistoricoAsync(pagamento, tipo, origem, pagamentoExternoId ?? pagamento.PagamentoExternoId);
    }

    // Best-effort (o gateway nao lanca) e sem o token da requisicao: chamado so depois da gravacao e da publicacao do evento
    private Task RegistrarHistoricoAsync(Pagamento pagamento, string tipo, string origem, string? pagamentoExternoId, DateTime? ocorridoEm = null)
    {
        return _historicoPagamentoGateway.RegistrarAsync(
            new EventoHistoricoPagamento(
                pagamento.OrdemServicoId,
                ocorridoEm ?? DateTime.UtcNow,
                tipo,
                origem,
                pagamento.Id,
                pagamento.Provedor.ToString(),
                pagamento.Valor,
                pagamentoExternoId,
                null),
            CancellationToken.None);
    }

    private async Task<Pagamento> ObterUltimoPagamentoExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var pagamento = await _pagamentoGateway.ObterUltimoPorOrdemServicoIdAsync(ordemServicoId, cancellationToken);

        if (pagamento is null)
        {
            throw new KeyNotFoundException("Pagamento nao encontrado.");
        }

        return pagamento;
    }
}
