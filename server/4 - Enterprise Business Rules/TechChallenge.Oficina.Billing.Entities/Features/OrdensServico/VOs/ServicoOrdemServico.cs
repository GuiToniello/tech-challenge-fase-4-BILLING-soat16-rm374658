using TechChallenge.Oficina.Billing.Entities.Exceptions;

namespace TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;

// Servico da OS como chegou no OrdemServicoValidada (Management): nome e valor ficam congelados no snapshot, sem depender do cadastro de servicos.
public sealed class ServicoOrdemServico
{
    public const int TamanhoMaximoNome = 150;

    public Guid ServicoId { get; private set; }
    public string Nome { get; private set; }
    public decimal Valor { get; private set; }

    private ServicoOrdemServico()
    {
        Nome = string.Empty;
    }

    private ServicoOrdemServico(Guid servicoId, string nome, decimal valor)
    {
        ServicoId = servicoId;
        Nome = nome;
        Valor = valor;
    }

    public static ServicoOrdemServico Criar(Guid servicoId, string nome, decimal valor)
    {
        if (servicoId == Guid.Empty)
        {
            throw new DomainException("Todos os servicos informados devem possuir identificador valido.");
        }

        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new DomainException("O nome do servico e obrigatorio.");
        }

        var nomeNormalizado = nome.Trim();

        if (nomeNormalizado.Length > TamanhoMaximoNome)
        {
            throw new DomainException($"O nome do servico deve ter no maximo {TamanhoMaximoNome} caracteres.");
        }

        if (valor < 0)
        {
            throw new DomainException("O valor do servico nao pode ser negativo.");
        }

        return new ServicoOrdemServico(servicoId, nomeNormalizado, decimal.Round(valor, 2, MidpointRounding.ToEven));
    }
}
