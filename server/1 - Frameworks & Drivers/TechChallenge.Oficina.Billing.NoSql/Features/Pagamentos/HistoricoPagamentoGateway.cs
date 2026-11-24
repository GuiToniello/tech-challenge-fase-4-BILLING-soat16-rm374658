using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.NoSql.Configuration;
using TechChallenge.Oficina.Billing.NoSql.DynamoDb;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.NoSql.Features.Pagamentos;

// Historico de pagamentos no DynamoDB: uma particao por OS (OrdemServicoId) e a chave de ordenacao "OcorridoEm#sufixo"
// (data UTC com milissegundos + id curto), em ordem cronologica e sem colisao entre eventos do mesmo instante. Cada item expira em 180 dias (TTL em ExpiraEm).
// O registro e best-effort: a falha sai como integracao.falha (Warning) e o fluxo de pagamento segue. A consulta registra a falha e relanca.
internal sealed class HistoricoPagamentoGateway : IHistoricoPagamentoGateway
{
    public const string ChaveParticao = "OrdemServicoId";
    public const string ChaveOrdenacao = "OcorridoEm";
    public const string AtributoExpiracao = "ExpiraEm";
    public const string AtributoTipo = "Tipo";
    public const string AtributoOrigem = "Origem";
    public const string AtributoPagamentoId = "PagamentoId";
    public const string AtributoProvedor = "Provedor";
    public const string AtributoValor = "Valor";
    public const string AtributoPagamentoExternoId = "PagamentoExternoId";
    public const string AtributoPreferenciaId = "PreferenciaId";

    public static readonly TimeSpan Retencao = TimeSpan.FromDays(180);

    private const string FormatoData = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const char SeparadorChaveOrdenacao = '#';
    private const int TamanhoSufixo = 8;

    private readonly IDynamoDbClient _dynamoDbClient;
    private readonly DynamoDbSettings _settings;
    private readonly ILogger<HistoricoPagamentoGateway> _logger;

    public HistoricoPagamentoGateway(IDynamoDbClient dynamoDbClient, DynamoDbSettings settings, ILogger<HistoricoPagamentoGateway> logger)
    {
        _dynamoDbClient = dynamoDbClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task RegistrarAsync(EventoHistoricoPagamento evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        try
        {
            await _dynamoDbClient.PutItemAsync(_settings.TabelaHistoricoPagamentos, CriarItem(evento), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Historico complementar: o pagamento ja esta gravado no PostgreSQL, entao a falha nao interrompe o fluxo
            _logger.LogWarning(
                OrdemServicoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do evento {Tipo} da ordem de servico {OrdemServicoId}; o historico de pagamentos fica sem este registro.",
                OrdemServicoLogEventos.Integracoes.DynamoDb,
                OrdemServicoLogEventos.Operacoes.RegistroHistorico,
                evento.Tipo,
                evento.OrdemServicoId);
        }
    }

    public async Task<IReadOnlyCollection<EventoHistoricoPagamento>> ListarAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var itens = await _dynamoDbClient.QueryAsync(_settings.TabelaHistoricoPagamentos, ChaveParticao, ordemServicoId.ToString("D"), cancellationToken);
            return itens.Select(Mapear).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da ordem de servico {OrdemServicoId}.",
                OrdemServicoLogEventos.Integracoes.DynamoDb,
                OrdemServicoLogEventos.Operacoes.ConsultaHistorico,
                ordemServicoId);
            throw;
        }
    }

    // Sem dados pessoais: so identificadores, tipo, origem, provedor e valor
    internal static Dictionary<string, AttributeValue> CriarItem(EventoHistoricoPagamento evento)
    {
        var ocorridoEm = ParaUtc(evento.OcorridoEm);

        var item = new Dictionary<string, AttributeValue>
        {
            [ChaveParticao] = new AttributeValue { S = evento.OrdemServicoId.ToString("D") },
            [ChaveOrdenacao] = new AttributeValue { S = CriarChaveOrdenacao(ocorridoEm) },
            [AtributoExpiracao] = new AttributeValue { N = new DateTimeOffset(ocorridoEm).Add(Retencao).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            [AtributoTipo] = new AttributeValue { S = evento.Tipo },
            [AtributoOrigem] = new AttributeValue { S = evento.Origem },
            [AtributoPagamentoId] = new AttributeValue { S = evento.PagamentoId.ToString("D") },
            [AtributoProvedor] = new AttributeValue { S = evento.Provedor },
            [AtributoValor] = new AttributeValue { N = evento.Valor.ToString(CultureInfo.InvariantCulture) }
        };

        // Atributos opcionais so entram quando preenchidos
        if (!string.IsNullOrWhiteSpace(evento.PagamentoExternoId))
        {
            item[AtributoPagamentoExternoId] = new AttributeValue { S = evento.PagamentoExternoId };
        }

        if (!string.IsNullOrWhiteSpace(evento.PreferenciaId))
        {
            item[AtributoPreferenciaId] = new AttributeValue { S = evento.PreferenciaId };
        }

        return item;
    }

    internal static string CriarChaveOrdenacao(DateTime ocorridoEmUtc)
    {
        var sufixo = Guid.NewGuid().ToString("N")[..TamanhoSufixo];
        return $"{ocorridoEmUtc.ToString(FormatoData, CultureInfo.InvariantCulture)}{SeparadorChaveOrdenacao}{sufixo}";
    }

    internal static EventoHistoricoPagamento Mapear(Dictionary<string, AttributeValue> item)
    {
        var data = item[ChaveOrdenacao].S.Split(SeparadorChaveOrdenacao)[0];

        return new EventoHistoricoPagamento(
            Guid.Parse(item[ChaveParticao].S),
            DateTime.ParseExact(data, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            item[AtributoTipo].S,
            item[AtributoOrigem].S,
            Guid.Parse(item[AtributoPagamentoId].S),
            item[AtributoProvedor].S,
            decimal.Parse(item[AtributoValor].N, NumberStyles.Float, CultureInfo.InvariantCulture),
            item.TryGetValue(AtributoPagamentoExternoId, out var pagamentoExternoId) ? pagamentoExternoId.S : null,
            item.TryGetValue(AtributoPreferenciaId, out var preferenciaId) ? preferenciaId.S : null);
    }

    // Data sem fuso (Unspecified) e tratada como UTC, como o restante do servico (DateTime.UtcNow)
    private static DateTime ParaUtc(DateTime data)
    {
        return data.Kind == DateTimeKind.Local ? data.ToUniversalTime() : DateTime.SpecifyKind(data, DateTimeKind.Utc);
    }
}
