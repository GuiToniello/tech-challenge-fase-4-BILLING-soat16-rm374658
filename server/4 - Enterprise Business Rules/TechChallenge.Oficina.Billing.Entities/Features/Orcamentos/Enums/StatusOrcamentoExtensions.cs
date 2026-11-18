namespace TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;

public static class StatusOrcamentoExtensions
{
    public static string ObterDescricao(this StatusOrcamento status)
    {
        return status switch
        {
            StatusOrcamento.Gerado => "Gerado",
            StatusOrcamento.AguardandoPagamento => "Aguardando pagamento",
            StatusOrcamento.Aprovado => "Aprovado",
            StatusOrcamento.Recusado => "Recusado",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status do orcamento invalido.")
        };
    }
}
