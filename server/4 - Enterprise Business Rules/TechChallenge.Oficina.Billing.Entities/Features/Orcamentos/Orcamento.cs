using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;

namespace TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;

// Raiz de agregado do orcamento: o Id e o mesmo da ordem de servico (OS Service), ja que cada OS possui no maximo um orcamento.
// Ciclo: Gerado -> AguardandoPagamento (aprovado pelo cliente, cobranca criada) -> Aprovado (pagamento confirmado); Recusado a partir de Gerado ou AguardandoPagamento.
public sealed class Orcamento
{
    private readonly List<OrcamentoServico> _servicos = [];

    public Guid Id { get; private set; }
    public Guid OrdemServicoId => Id;
    public DateTime DataGeracao { get; private set; }
    public decimal ValorTotal { get; private set; }
    public StatusOrcamento Status { get; private set; }
    public IReadOnlyCollection<OrcamentoServico> Servicos => _servicos.AsReadOnly();

    private Orcamento()
    {
    }

    private Orcamento(Guid ordemServicoId, IReadOnlyCollection<ServicoOrdemServico> servicos, DateTime dataGeracao)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("A ordem de servico do orcamento e obrigatoria.");
        }

        Id = ordemServicoId;
        Status = StatusOrcamento.Gerado;

        DefinirServicos(servicos, dataGeracao);
    }

    public static Orcamento Criar(Guid ordemServicoId, IReadOnlyCollection<ServicoOrdemServico> servicos, DateTime? dataGeracao = null)
    {
        return new Orcamento(ordemServicoId, servicos, dataGeracao ?? DateTime.UtcNow);
    }

    public void ValidarRegeracao()
    {
        if (Status != StatusOrcamento.Gerado)
        {
            throw new DomainException("Somente orcamento gerado pode ser gerado novamente.");
        }
    }

    public void Regerar(IReadOnlyCollection<ServicoOrdemServico> servicos, DateTime? dataGeracao = null)
    {
        ValidarRegeracao();
        DefinirServicos(servicos, dataGeracao ?? DateTime.UtcNow);
    }

    // Aguardando pagamento tambem e aceito: a aprovacao repetida devolve a cobranca pendente ou cria outra depois de uma recusa
    public void ValidarAprovacao()
    {
        if (Status != StatusOrcamento.Gerado && Status != StatusOrcamento.AguardandoPagamento)
        {
            throw new DomainException("Somente orcamento gerado ou aguardando pagamento pode ser aprovado.");
        }

        if (ValorTotal <= 0)
        {
            throw new DomainException("O orcamento precisa ter valor maior que zero para ser aprovado.");
        }
    }

    public void AprovarParaPagamento()
    {
        if (Status != StatusOrcamento.Gerado)
        {
            throw new DomainException("Somente orcamento gerado pode ser aprovado para pagamento.");
        }

        Status = StatusOrcamento.AguardandoPagamento;
    }

    public void ConfirmarPagamento()
    {
        if (Status != StatusOrcamento.AguardandoPagamento)
        {
            throw new DomainException("Somente orcamento aguardando pagamento pode ter o pagamento confirmado.");
        }

        Status = StatusOrcamento.Aprovado;
    }

    public void ValidarRecusa()
    {
        if (Status != StatusOrcamento.Gerado && Status != StatusOrcamento.AguardandoPagamento)
        {
            throw new DomainException("Somente orcamento gerado ou aguardando pagamento pode ser recusado.");
        }
    }

    public void Recusar()
    {
        ValidarRecusa();
        Status = StatusOrcamento.Recusado;
    }

    private void DefinirServicos(IReadOnlyCollection<ServicoOrdemServico> servicos, DateTime dataGeracao)
    {
        if (dataGeracao == default)
        {
            throw new DomainException("A data de geracao do orcamento e obrigatoria.");
        }

        if (servicos is null || servicos.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico para gerar orcamento.");
        }

        // Monta os itens antes de alterar o estado: um servico invalido nao deixa o orcamento pela metade
        var servicosIdsAdicionados = new HashSet<Guid>();
        var itens = servicos
            .Select(OrcamentoServico.Criar)
            .Where(item => servicosIdsAdicionados.Add(item.ServicoId))
            .ToList();

        DataGeracao = dataGeracao;
        _servicos.Clear();
        _servicos.AddRange(itens);

        ValorTotal = decimal.Round(_servicos.Sum(item => item.ValorTotal), 2, MidpointRounding.ToEven);
    }
}
