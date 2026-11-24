using AutoMapper;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;

// O orcamento e do Billing e e montado com o snapshot da OS recebido da Management (OrdemServicoValidada).
// A OS e do OS Service, informado das decisoes por eventos (OrcamentoGerado, OrcamentoRecusado e, depois do pagamento, PagamentoAprovado).
public sealed class OrcamentoUseCases : IOrcamentoUseCases
{
    private readonly IMapper _mapper;
    private readonly IOrcamentoGateway _orcamentoGateway;
    private readonly IOrdemServicoReferenciaGateway _referenciaGateway;
    private readonly IPagamentoGateway _pagamentoGateway;
    private readonly IOrcamentoUseCasesFacade _facade;
    private readonly ILogger<OrcamentoUseCases> _logger;

    public OrcamentoUseCases(
        IMapper mapper,
        IOrcamentoGateway orcamentoGateway,
        IOrdemServicoReferenciaGateway referenciaGateway,
        IPagamentoGateway pagamentoGateway,
        IOrcamentoUseCasesFacade facade,
        ILogger<OrcamentoUseCases> logger)
    {
        _mapper = mapper;
        _orcamentoGateway = orcamentoGateway;
        _referenciaGateway = referenciaGateway;
        _pagamentoGateway = pagamentoGateway;
        _facade = facade;
        _logger = logger;
    }

    public async Task<OrcamentoViewModel> ObterPorOrdemServicoIdAsync(ObterOrcamentoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(query.OrdemServicoId, cancellationToken);
        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    public async Task<OrcamentoViewModel> GerarAsync(GerarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var referencia = await ObterReferenciaExistenteAsync(command.OrdemServicoId, cancellationToken);
        referencia.ValidarGeracaoOrcamento();

        var orcamento = await _orcamentoGateway.ObterPorIdAsync(referencia.Id, cancellationToken);
        orcamento?.ValidarRegeracao();

        var regerado = orcamento is not null;

        if (orcamento is null)
        {
            orcamento = Orcamento.Criar(referencia.Id, referencia.Servicos);
            await _orcamentoGateway.AdicionarAsync(orcamento, cancellationToken);
        }
        else
        {
            orcamento.Regerar(referencia.Servicos);
            await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);
        }

        await _facade.EventosPublisher.PublicarAsync(new OrcamentoGerado(orcamento.OrdemServicoId, orcamento.ValorTotal), orcamento.OrdemServicoId, cancellationToken);

        _logger.LogInformation(
            OrdemServicoLogEventos.OsOrcamentoGerado,
            "Orcamento da ordem de servico {OrdemServicoId} gerado com valor total {ValorTotal} (regerado: {Regerado}).",
            orcamento.OrdemServicoId,
            orcamento.ValorTotal,
            regerado);

        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    public async Task EnviarPorEmailAsync(EnviarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);

        var emailDestino = referencia.ClienteEmail;

        if (string.IsNullOrWhiteSpace(emailDestino))
        {
            throw new DomainException("O cliente da ordem de servico nao possui email cadastrado.");
        }

        // Com cobranca pendente (orcamento ja aprovado pelo cliente), o email leva o link de pagamento
        var ultimoPagamento = await _pagamentoGateway.ObterUltimoPorOrdemServicoIdAsync(orcamento.OrdemServicoId, cancellationToken);
        var linkPagamento = ultimoPagamento is { Status: StatusPagamento.Pendente } ? ultimoPagamento.LinkPagamento : null;

        // O timeout do HttpClient também é OperationCanceledException; só o cancelamento da requisição sai sem registro.
        try
        {
            await _facade.OrcamentoEmailSender.EnviarOrcamentoAsync(orcamento, emailDestino, linkPagamento, cancellationToken);
        }
        catch (Exception ex) when (ex is not DomainException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId}.",
                OrdemServicoLogEventos.Integracoes.Resend,
                OrdemServicoLogEventos.Operacoes.EnvioOrcamento,
                orcamento.OrdemServicoId);
            throw;
        }

        _logger.LogInformation(
            OrdemServicoLogEventos.OsOrcamentoEnviado,
            "Orcamento da ordem de servico {OrdemServicoId} enviado por email com valor total {ValorTotal}.",
            orcamento.OrdemServicoId,
            orcamento.ValorTotal);
    }

    // O cliente aprova o orcamento: cria a cobranca no provedor e o orcamento passa a aguardar o pagamento.
    // A OS so segue para execucao quando o pagamento for confirmado (PagamentoAprovado, publicado pelo PagamentoUseCases).
    public async Task<AprovacaoOrcamentoViewModel> AprovarAsync(AprovarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        orcamento.ValidarAprovacao();

        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);
        referencia.ValidarAprovacaoOrcamento();

        // Idempotente: com uma cobranca pendente, devolve o mesmo link em vez de criar outra. Depois de uma recusa, cria uma nova cobranca.
        var ultimoPagamento = await _pagamentoGateway.ObterUltimoPorOrdemServicoIdAsync(orcamento.OrdemServicoId, cancellationToken);

        if (ultimoPagamento is { Status: StatusPagamento.Pendente })
        {
            return CriarAprovacaoViewModel(orcamento, ultimoPagamento);
        }

        // Falha no provedor ja sai como integracao.falha no gateway; nada foi gravado ainda
        var cobranca = await _facade.GatewayPagamento.CriarCobrancaAsync(
            new NovaCobranca(orcamento.OrdemServicoId, orcamento.ValorTotal, CriarDescricaoCobranca(orcamento), referencia.ClienteEmail),
            cancellationToken);

        if (orcamento.Status == StatusOrcamento.Gerado)
        {
            orcamento.AprovarParaPagamento();
        }

        var pagamento = Pagamento.Criar(orcamento.OrdemServicoId, orcamento.ValorTotal, cobranca.Provedor, cobranca.PreferenciaId, cobranca.LinkPagamento);

        // Mesmo DbContext por requisicao: a primeira gravacao ja leva a cobranca nova e o status do orcamento juntos
        await _pagamentoGateway.AdicionarAsync(pagamento, cancellationToken);
        await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);

        _logger.LogInformation(
            OrdemServicoLogEventos.PagamentoCriado,
            "Cobranca {PagamentoId} criada no provedor {Provedor} para a ordem de servico {OrdemServicoId} com valor {Valor}.",
            pagamento.Id,
            pagamento.Provedor,
            pagamento.OrdemServicoId,
            pagamento.Valor);

        // Historico complementar (best-effort, nao lanca): so depois da gravacao, e sem o token da requisicao para nao perder o registro
        await _facade.HistoricoPagamentoGateway.RegistrarAsync(
            new EventoHistoricoPagamento(
                pagamento.OrdemServicoId,
                DateTime.UtcNow,
                TiposHistoricoPagamento.CobrancaCriada,
                OrigensHistoricoPagamento.Aprovacao,
                pagamento.Id,
                pagamento.Provedor.ToString(),
                pagamento.Valor,
                null,
                pagamento.PreferenciaId),
            CancellationToken.None);

        return CriarAprovacaoViewModel(orcamento, pagamento);
    }

    public async Task<OrcamentoViewModel> RecusarAsync(RecusarOrcamentoCommand command, CancellationToken cancellationToken = default)
    {
        var orcamento = await ObterOrcamentoExistenteAsync(command.OrdemServicoId, cancellationToken);
        orcamento.ValidarRecusa();

        var referencia = await ObterReferenciaExistenteAsync(orcamento.OrdemServicoId, cancellationToken);
        referencia.ValidarRecusaOrcamento();

        orcamento.Recusar();
        await _orcamentoGateway.AtualizarAsync(orcamento, cancellationToken);
        await _facade.EventosPublisher.PublicarAsync(new OrcamentoRecusado(orcamento.OrdemServicoId), orcamento.OrdemServicoId, cancellationToken);

        return _mapper.Map<OrcamentoViewModel>(orcamento);
    }

    private AprovacaoOrcamentoViewModel CriarAprovacaoViewModel(Orcamento orcamento, Pagamento pagamento)
    {
        return new AprovacaoOrcamentoViewModel
        {
            Orcamento = _mapper.Map<OrcamentoViewModel>(orcamento),
            Pagamento = _mapper.Map<PagamentoViewModel>(pagamento)
        };
    }

    private static string CriarDescricaoCobranca(Orcamento orcamento)
    {
        var prefixo = orcamento.OrdemServicoId.ToString()[..8].ToUpperInvariant();
        return $"Orçamento da ordem de serviço {prefixo}";
    }

    private async Task<Orcamento> ObterOrcamentoExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var orcamento = await _orcamentoGateway.ObterPorIdAsync(ordemServicoId, cancellationToken);

        if (orcamento is null)
        {
            throw new KeyNotFoundException("Orcamento nao encontrado.");
        }

        return orcamento;
    }

    private async Task<OrdemServicoReferencia> ObterReferenciaExistenteAsync(Guid ordemServicoId, CancellationToken cancellationToken)
    {
        var referencia = await _referenciaGateway.ObterPorIdAsync(ordemServicoId, cancellationToken);

        if (referencia is null)
        {
            throw new KeyNotFoundException("Ordem de servico nao encontrada.");
        }

        return referencia;
    }
}
