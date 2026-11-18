namespace TechChallenge.Oficina.Billing.Pagamentos.Configuration;

public sealed class MercadoPagoSettings
{
    public const string SectionName = "MercadoPagoSettings";

    // Access token da conta (credencial de teste ou de producao). Vazio: o Billing usa o gateway simulado.
    public string AccessToken { get; set; } = string.Empty;

    // URL publica de POST /api/pagamentos/notificacoes enviada na preferencia. Vazia: sem webhook, o status e conferido em /verificar.
    public string NotificationUrl { get; set; } = string.Empty;
}
