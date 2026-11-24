namespace TechChallenge.Oficina.Billing.NoSql.Configuration;

public sealed class DynamoDbSettings
{
    public const string SectionName = "DynamoDbSettings";

    // Tabela do historico de pagamentos (criada pelo Terraform na AWS; no ambiente local, pela propria API)
    public string TabelaHistoricoPagamentos { get; set; } = string.Empty;

    // Endpoint do DynamoDB Local. Vazio na AWS: regiao e credenciais vem do ambiente.
    public string ServiceUrl { get; set; } = string.Empty;
}
