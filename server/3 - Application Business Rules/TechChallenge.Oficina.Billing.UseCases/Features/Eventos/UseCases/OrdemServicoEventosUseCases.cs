using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;

// Mantem o snapshot local da OS a partir dos eventos da Management e do OS Service (saga coreografada). E idempotente: a fila entrega pelo menos uma vez,
// entao evento repetido ou de OS desconhecida resulta em Ignorado. Dados invalidos lancam excecao: a mensagem volta para a fila e, depois, vai para a DLQ.
public sealed class OrdemServicoEventosUseCases : IOrdemServicoEventosUseCases
{
    private readonly IOrdemServicoReferenciaGateway _referenciaGateway;
    private readonly IOrcamentoGateway _orcamentoGateway;
    private readonly ILogger<OrdemServicoEventosUseCases> _logger;

    public OrdemServicoEventosUseCases(
        IOrdemServicoReferenciaGateway referenciaGateway,
        IOrcamentoGateway orcamentoGateway,
        ILogger<OrdemServicoEventosUseCases> logger)
    {
        _referenciaGateway = referenciaGateway;
        _orcamentoGateway = orcamentoGateway;
        _logger = logger;
    }

    // Grava ou atualiza o snapshot; o status ja conhecido e mantido (pode ter chegado antes, pelo OS Service)
    public async Task<ResultadoProcessamentoEvento> ProcessarValidadaAsync(OrdemServicoValidada evento, CancellationToken cancellationToken = default)
    {
        var servicos = (evento.Servicos ?? [])
            .Select(servico => ServicoOrdemServico.Criar(servico.ServicoId, servico.Nome, servico.Valor))
            .ToArray();

        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null)
        {
            referencia = OrdemServicoReferencia.Criar(
                evento.OrdemServicoId,
                evento.ClienteId,
                evento.VeiculoId,
                evento.ClienteEmail,
                evento.EstoqueSuficiente,
                servicos,
                evento.ValorTotal);
            await _referenciaGateway.AdicionarAsync(referencia, cancellationToken);
        }
        else
        {
            referencia.AtualizarDados(evento.ClienteId, evento.VeiculoId, evento.ClienteEmail, evento.EstoqueSuficiente, servicos, evento.ValorTotal);
            await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    // Sem ordem entre os publicadores: se o status chegar antes do OrdemServicoValidada, cria uma referencia parcial (sem servicos)
    public async Task<ResultadoProcessamentoEvento> ProcessarStatusAlteradoAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default)
    {
        var statusNovo = (StatusOrdemServico)evento.StatusNovo;
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null)
        {
            referencia = OrdemServicoReferencia.CriarParcial(evento.OrdemServicoId, evento.ClienteId, statusNovo);
            await _referenciaGateway.AdicionarAsync(referencia, cancellationToken);
            return ResultadoProcessamentoEvento.Processado;
        }

        if (referencia.Status == statusNovo)
        {
            _logger.LogInformation(
                "Evento OrdemServicoStatusAlterado ignorado: a ordem de servico {OrdemServicoId} ja esta no status {StatusNovo}.",
                referencia.Id,
                OrdemServicoLogEventos.StatusParaLog(statusNovo));
            return ResultadoProcessamentoEvento.Ignorado;
        }

        referencia.AtualizarStatus(statusNovo);
        await _referenciaGateway.AtualizarAsync(referencia, cancellationToken);

        return ResultadoProcessamentoEvento.Processado;
    }

    // Remove a referencia e o orcamento; os pagamentos ficam como registro financeiro
    public async Task<ResultadoProcessamentoEvento> ProcessarExcluidaAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);
        var orcamento = await _orcamentoGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null && orcamento is null)
        {
            _logger.LogInformation("Evento OrdemServicoExcluida ignorado: ordem de servico {OrdemServicoId} desconhecida.", evento.OrdemServicoId);
            return ResultadoProcessamentoEvento.Ignorado;
        }

        if (orcamento is not null)
        {
            await _orcamentoGateway.RemoverAsync(orcamento, cancellationToken);
        }

        if (referencia is not null)
        {
            await _referenciaGateway.RemoverAsync(referencia, cancellationToken);
        }

        return ResultadoProcessamentoEvento.Processado;
    }

    public async Task<ResultadoProcessamentoEvento> ProcessarRejeitadaAsync(OrdemServicoRejeitada evento, CancellationToken cancellationToken = default)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(evento.OrdemServicoId, cancellationToken);

        if (referencia is null)
        {
            _logger.LogInformation("Evento OrdemServicoRejeitada ignorado: ordem de servico {OrdemServicoId} desconhecida.", evento.OrdemServicoId);
            return ResultadoProcessamentoEvento.Ignorado;
        }

        await _referenciaGateway.RemoverAsync(referencia, cancellationToken);

        _logger.LogInformation(
            "Referencia da ordem de servico {OrdemServicoId} removida: OS rejeitada pela Management ({Motivo}).",
            evento.OrdemServicoId,
            evento.Motivo);

        return ResultadoProcessamentoEvento.Processado;
    }
}
