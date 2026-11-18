namespace TechChallenge.Oficina.Billing.Email.Configuration;

public sealed class ResendSettings
{
    public const string SectionName = "ResendSettings";

    // Opcional: sem a chave, o envio do orcamento responde 400 (servico de email nao configurado)
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}
