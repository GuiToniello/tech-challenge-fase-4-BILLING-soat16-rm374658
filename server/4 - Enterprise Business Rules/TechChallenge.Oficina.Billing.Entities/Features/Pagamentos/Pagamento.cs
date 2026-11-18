using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;

namespace TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;

// Cobranca do orcamento de uma OS no provedor de pagamento (uma preferencia do Checkout Pro do Mercado Pago).
// Uma OS pode ter varias cobrancas: depois de uma recusa, a nova aprovacao do orcamento cria outra. Fica como registro financeiro mesmo se a OS for excluida.
public sealed class Pagamento
{
    public const int TamanhoMaximoIdentificador = 100;
    public const int TamanhoMaximoLink = 500;

    public Guid Id { get; private set; }
    public Guid OrdemServicoId { get; private set; }
    public decimal Valor { get; private set; }
    public StatusPagamento Status { get; private set; }
    public ProvedorPagamento Provedor { get; private set; }
    public string PreferenciaId { get; private set; }
    public string? PagamentoExternoId { get; private set; }
    public string LinkPagamento { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    private Pagamento()
    {
        PreferenciaId = string.Empty;
        LinkPagamento = string.Empty;
    }

    private Pagamento(Guid ordemServicoId, decimal valor, ProvedorPagamento provedor, string preferenciaId, string linkPagamento, DateTime criadoEm)
    {
        Id = Guid.NewGuid();
        OrdemServicoId = ordemServicoId;
        Valor = decimal.Round(valor, 2, MidpointRounding.ToEven);
        Status = StatusPagamento.Pendente;
        Provedor = provedor;
        PreferenciaId = preferenciaId;
        LinkPagamento = linkPagamento;
        CriadoEm = criadoEm;
        AtualizadoEm = criadoEm;
    }

    public static Pagamento Criar(
        Guid ordemServicoId,
        decimal valor,
        ProvedorPagamento provedor,
        string preferenciaId,
        string linkPagamento,
        DateTime? criadoEm = null)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("A ordem de servico do pagamento e obrigatoria.");
        }

        if (valor <= 0)
        {
            throw new DomainException("O valor do pagamento deve ser maior que zero.");
        }

        if (!Enum.IsDefined(typeof(ProvedorPagamento), provedor))
        {
            throw new DomainException("O provedor do pagamento informado e invalido.");
        }

        if (string.IsNullOrWhiteSpace(preferenciaId) || preferenciaId.Trim().Length > TamanhoMaximoIdentificador)
        {
            throw new DomainException("O identificador da cobranca no provedor e obrigatorio e deve ter no maximo 100 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(linkPagamento) || linkPagamento.Trim().Length > TamanhoMaximoLink)
        {
            throw new DomainException("O link de pagamento e obrigatorio e deve ter no maximo 500 caracteres.");
        }

        return new Pagamento(ordemServicoId, valor, provedor, preferenciaId.Trim(), linkPagamento.Trim(), criadoEm ?? DateTime.UtcNow);
    }

    // Tambem a partir de Recusado: o checkout do Mercado Pago aceita nova tentativa na mesma cobranca depois de um cartao recusado
    public void Aprovar(string pagamentoExternoId, DateTime? dataAtualizacao = null)
    {
        if (Status == StatusPagamento.Aprovado)
        {
            throw new DomainException("O pagamento ja foi aprovado.");
        }

        PagamentoExternoId = ValidarPagamentoExternoId(pagamentoExternoId)
            ?? throw new DomainException("O identificador do pagamento no provedor e obrigatorio.");
        Status = StatusPagamento.Aprovado;
        AtualizadoEm = dataAtualizacao ?? DateTime.UtcNow;
    }

    public void Recusar(string? pagamentoExternoId, DateTime? dataAtualizacao = null)
    {
        if (Status != StatusPagamento.Pendente)
        {
            throw new DomainException("Somente pagamento pendente pode ser recusado.");
        }

        PagamentoExternoId = ValidarPagamentoExternoId(pagamentoExternoId) ?? PagamentoExternoId;
        Status = StatusPagamento.Recusado;
        AtualizadoEm = dataAtualizacao ?? DateTime.UtcNow;
    }

    private static string? ValidarPagamentoExternoId(string? pagamentoExternoId)
    {
        if (string.IsNullOrWhiteSpace(pagamentoExternoId))
        {
            return null;
        }

        var identificador = pagamentoExternoId!.Trim();

        if (identificador.Length > TamanhoMaximoIdentificador)
        {
            throw new DomainException("O identificador do pagamento no provedor deve ter no maximo 100 caracteres.");
        }

        return identificador;
    }
}
