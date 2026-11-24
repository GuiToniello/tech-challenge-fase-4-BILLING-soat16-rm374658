namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

// Tipos de evento do historico de pagamentos (atributo Tipo do item no DynamoDB)
public static class TiposHistoricoPagamento
{
    public const string CobrancaCriada = "CobrancaCriada";
    public const string NotificacaoRecebida = "NotificacaoRecebida";
    public const string PagamentoPendente = "PagamentoPendente";
    public const string PagamentoAprovado = "PagamentoAprovado";
    public const string PagamentoRecusado = "PagamentoRecusado";
}
