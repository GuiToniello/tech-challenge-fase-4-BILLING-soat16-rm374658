namespace TechChallenge.Oficina.Billing.Messaging.Configuration;

public sealed class SqsSettings
{
    public const string SectionName = "SqsSettings";

    // Fila FIFO de onde o Billing consome os eventos da Management e do OS Service. Vazia: o consumidor nao e iniciado.
    public string FilaConsumo { get; set; } = string.Empty;

    // Filas FIFO do OS Service e da Management: o mesmo envelope de cada evento do Billing vai para todas.
    // Por variavel de ambiente: SqsSettings__FilasPublicacao__0, SqsSettings__FilasPublicacao__1...
    public List<string> FilasPublicacao { get; set; } = [];

    // Endpoint do emulador local (ElasticMQ). Vazio na AWS: regiao e credenciais vem do ambiente.
    public string ServiceUrl { get; set; } = string.Empty;
}
