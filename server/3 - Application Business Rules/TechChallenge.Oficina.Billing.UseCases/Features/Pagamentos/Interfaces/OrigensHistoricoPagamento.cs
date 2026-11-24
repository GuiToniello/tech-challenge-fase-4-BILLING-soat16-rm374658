namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// Fluxo que gerou o evento do historico (atributo Origem do item no DynamoDB)
public static class OrigensHistoricoPagamento
{
    public const string Aprovacao = "aprovacao";
    public const string Verificacao = "verificacao";
    public const string Notificacao = "notificacao";
}
