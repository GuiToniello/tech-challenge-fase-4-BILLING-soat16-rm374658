using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;

namespace TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;

public sealed class OrcamentoServico
{
    public Guid ServicoId { get; private set; }
    public string NomeServico { get; private set; }
    public decimal ValorTotal { get; private set; }

    private OrcamentoServico()
    {
        NomeServico = string.Empty;
    }

    private OrcamentoServico(Guid servicoId, string nomeServico, decimal valorTotal)
    {
        ServicoId = servicoId;
        NomeServico = nomeServico;
        ValorTotal = decimal.Round(valorTotal, 2, MidpointRounding.ToEven);
    }

    // O valor vem do snapshot da OS (calculado pela Management com os insumos no momento da validacao)
    public static OrcamentoServico Criar(ServicoOrdemServico servico)
    {
        if (servico is null)
        {
            throw new DomainException("O servico do orcamento e obrigatorio.");
        }

        return new OrcamentoServico(servico.ServicoId, servico.Nome, servico.Valor);
    }
}
