using System.Text.Json;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;

namespace TechChallenge.Oficina.Billing.Controllers.Features.Eventos
{
    public sealed class EventosController : IEventosController
    {
        // Os dados do envelope chegam em camelCase (contrato entre os servicos)
        private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

        private readonly IOrdemServicoEventosUseCases _ordemServicoEventosUseCases;

        public EventosController(IOrdemServicoEventosUseCases ordemServicoEventosUseCases)
        {
            _ordemServicoEventosUseCases = ordemServicoEventosUseCases;
        }

        // Tipo desconhecido (inclusive os eventos publicados pelo proprio Billing) e ignorado e a mensagem sai da fila.
        // Dados invalidos lancam JsonException: a mensagem fica na fila e, apos as retentativas, vai para a DLQ.
        public async Task<ResultadoProcessamentoEvento> ProcessarAsync(string tipo, string dadosJson, CancellationToken cancellationToken)
        {
            return tipo switch
            {
                nameof(OrdemServicoValidada) => await _ordemServicoEventosUseCases.ProcessarValidadaAsync(Desserializar<OrdemServicoValidada>(dadosJson), cancellationToken),
                nameof(OrdemServicoStatusAlterado) => await _ordemServicoEventosUseCases.ProcessarStatusAlteradoAsync(Desserializar<OrdemServicoStatusAlterado>(dadosJson), cancellationToken),
                nameof(OrdemServicoExcluida) => await _ordemServicoEventosUseCases.ProcessarExcluidaAsync(Desserializar<OrdemServicoExcluida>(dadosJson), cancellationToken),
                nameof(OrdemServicoRejeitada) => await _ordemServicoEventosUseCases.ProcessarRejeitadaAsync(Desserializar<OrdemServicoRejeitada>(dadosJson), cancellationToken),
                _ => ResultadoProcessamentoEvento.Ignorado
            };
        }

        private static TEvento Desserializar<TEvento>(string dadosJson) where TEvento : class
        {
            return JsonSerializer.Deserialize<TEvento>(dadosJson, OpcoesJson)
                ?? throw new JsonException($"Os dados do evento {typeof(TEvento).Name} nao foram informados.");
        }
    }
}
