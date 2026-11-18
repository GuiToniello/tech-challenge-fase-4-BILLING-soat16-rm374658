namespace TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;

public static class StatusPagamentoExtensions
{
    public static string ObterDescricao(this StatusPagamento status)
    {
        return status switch
        {
            StatusPagamento.Pendente => "Pendente",
            StatusPagamento.Aprovado => "Aprovado",
            StatusPagamento.Recusado => "Recusado",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status do pagamento invalido.")
        };
    }
}
