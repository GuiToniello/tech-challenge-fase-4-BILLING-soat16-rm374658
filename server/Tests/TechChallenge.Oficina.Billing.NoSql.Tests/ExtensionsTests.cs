using Amazon.DynamoDBv2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.NoSql.Configuration;
using TechChallenge.Oficina.Billing.NoSql.DynamoDb;
using TechChallenge.Oficina.Billing.NoSql.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;
using Xunit;

namespace TechChallenge.Oficina.Billing.NoSql.Tests;

public sealed class ExtensionsTests
{
    private const string Tabela = "techchallenge-oficina-billing-pagamentos-historico";

    private readonly Mock<IDynamoDbClient> _dynamoDbClientMock = new();
    private readonly Mock<ILogger> _loggerMock = new();

    private static ServiceProvider CriarProvider(DynamoDbSettings settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfraNoSql(settings);
        return services.BuildServiceProvider();
    }

    // Provider so com o que a criacao da tabela local usa: settings, cliente do DynamoDB (mock) e logger (mock)
    private ServiceProvider CriarProviderTabelaLocal(string serviceUrl)
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_loggerMock.Object);

        var services = new ServiceCollection();
        services.AddSingleton(new DynamoDbSettings { TabelaHistoricoPagamentos = Tabela, ServiceUrl = serviceUrl });
        services.AddSingleton(_dynamoDbClientMock.Object);
        services.AddSingleton(loggerFactoryMock.Object);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddInfraNoSql_DeveRegistrarGatewayEClienteApontandoParaServiceUrl()
    {
        var settings = new DynamoDbSettings { TabelaHistoricoPagamentos = Tabela, ServiceUrl = "http://localhost:8000" };

        using var provider = CriarProvider(settings);

        Assert.IsType<HistoricoPagamentoGateway>(provider.GetRequiredService<IHistoricoPagamentoGateway>());
        Assert.IsType<DynamoDbClientAdapter>(provider.GetRequiredService<IDynamoDbClient>());
        Assert.Same(settings, provider.GetRequiredService<DynamoDbSettings>());
        Assert.StartsWith("http://localhost:8000", provider.GetRequiredService<IAmazonDynamoDB>().Config.ServiceURL);
    }

    [Fact]
    public void AddInfraNoSql_DeveUsarEndpointPadraoDaAws_QuandoServiceUrlVazio()
    {
        // Na AWS: sem ServiceURL, o endpoint sai da regiao do ambiente (AWS_REGION)
        using var provider = CriarProvider(new DynamoDbSettings { TabelaHistoricoPagamentos = Tabela });

        Assert.Null(provider.GetRequiredService<IAmazonDynamoDB>().Config.ServiceURL);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GarantirTabelasLocaisAsync_NaoDeveCriarTabela_QuandoServiceUrlVazio(string serviceUrl)
    {
        using var provider = CriarProviderTabelaLocal(serviceUrl);

        await provider.GarantirTabelasLocaisAsync();

        Assert.Empty(_dynamoDbClientMock.Invocations);
    }

    [Fact]
    public async Task GarantirTabelasLocaisAsync_DeveCriarTabelaComAsChavesDoContrato_QuandoServiceUrlConfigurado()
    {
        _dynamoDbClientMock
            .Setup(c => c.CriarTabelaSeNaoExisteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var provider = CriarProviderTabelaLocal("http://dynamodb-local:8000");

        await provider.GarantirTabelasLocaisAsync();

        _dynamoDbClientMock.Verify(c => c.CriarTabelaSeNaoExisteAsync(Tabela, "OrdemServicoId", "OcorridoEm", "ExpiraEm", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && (LogLevel)chamada.Arguments[0] == LogLevel.Information);
    }

    [Fact]
    public async Task GarantirTabelasLocaisAsync_DeveRegistrarWarningSemLancar_QuandoDynamoDbLocalIndisponivel()
    {
        var falha = new HttpRequestException("Connection refused");
        _dynamoDbClientMock
            .Setup(c => c.CriarTabelaSeNaoExisteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);
        using var provider = CriarProviderTabelaLocal("http://dynamodb-local:8000");

        await provider.GarantirTabelasLocaisAsync();

        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log));
        Assert.Equal(LogLevel.Warning, (LogLevel)invocacao.Arguments[0]);
        Assert.Same(falha, invocacao.Arguments[3]);
    }
}
