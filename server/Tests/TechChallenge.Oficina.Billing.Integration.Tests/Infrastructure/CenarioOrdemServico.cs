using System.Text.Json;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;

/// <summary>
/// Simula os eventos da Management e do OS Service como chegam da fila (dados JSON em camelCase, como no envelope SQS).
/// </summary>
internal static class CenarioOrdemServico
{
    public const string EmailCliente = "cliente@email.com";
    public const decimal ValorServico = 60m;

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    // OrdemServicoValidada da Management: um servico de 60,00 e estoque suficiente por padrao
    public static async Task<Guid> ValidarOrdemServicoAsync(
        IntegrationTestFixture fixture,
        Guid? ordemServicoId = null,
        bool estoqueSuficiente = true,
        string? email = EmailCliente)
    {
        var id = ordemServicoId ?? Guid.NewGuid();
        var evento = new OrdemServicoValidada(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            email,
            estoqueSuficiente,
            [new ServicoValidado(Guid.NewGuid(), "Troca de Vela", ValorServico)],
            ValorServico);

        await ProcessarAsync(fixture, evento);
        return id;
    }

    public static Task<ResultadoProcessamentoEvento> AlterarStatusAsync(IntegrationTestFixture fixture, Guid ordemServicoId, int statusAnterior, int statusNovo)
        => ProcessarAsync(fixture, new OrdemServicoStatusAlterado(ordemServicoId, Guid.NewGuid(), statusAnterior, statusNovo));

    public static Task<ResultadoProcessamentoEvento> ExcluirOrdemServicoAsync(IntegrationTestFixture fixture, Guid ordemServicoId)
        => ProcessarAsync(fixture, new OrdemServicoExcluida(ordemServicoId));

    public static Task<ResultadoProcessamentoEvento> RejeitarOrdemServicoAsync(IntegrationTestFixture fixture, Guid ordemServicoId)
        => ProcessarAsync(fixture, new OrdemServicoRejeitada(ordemServicoId, "Veiculo nao encontrado."));

    public static Task<ResultadoProcessamentoEvento> ProcessarAsync<TEvento>(IntegrationTestFixture fixture, TEvento evento)
        => fixture.CriarEventosController().ProcessarAsync(typeof(TEvento).Name, JsonSerializer.Serialize(evento, OpcoesJson), CancellationToken.None);
}
