using System.Text.Json;

namespace TechChallenge.Oficina.Billing.API.Features.Pagamentos
{
    // Tipo e id do recurso de uma notificacao do Mercado Pago, nos formatos aceitos:
    // - webhook: query ?type=payment&data.id=123 e/ou corpo JSON {"type":"payment","data":{"id":"123"}};
    // - IPN legado: query ?topic=payment&id=123.
    public sealed record NotificacaoMercadoPago(string? Tipo, string? RecursoId)
    {
        public static async Task<NotificacaoMercadoPago> LerAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            var query = request.Query;
            var (tipoCorpo, idCorpo) = await LerCorpoAsync(request, cancellationToken);

            var tipo = Primeiro(query["type"], tipoCorpo, query["topic"]);
            var recursoId = Primeiro(query["data.id"], idCorpo, query["id"]);

            return new NotificacaoMercadoPago(tipo, recursoId);
        }

        private static async Task<(string? Tipo, string? Id)> LerCorpoAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (request.ContentLength == 0)
            {
                return (null, null);
            }

            try
            {
                using var documento = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
                var raiz = documento.RootElement;

                if (raiz.ValueKind != JsonValueKind.Object)
                {
                    return (null, null);
                }

                var tipo = LerTexto(raiz, "type") ?? LerTexto(raiz, "topic");
                var id = raiz.TryGetProperty("data", out var dados) && dados.ValueKind == JsonValueKind.Object
                    ? LerTexto(dados, "id")
                    : null;

                return (tipo, id);
            }
            catch (JsonException)
            {
                // Corpo vazio ou que nao e JSON: vale o que veio na query
                return (null, null);
            }
        }

        // O id pode vir como texto ("123") ou numero (123)
        private static string? LerTexto(JsonElement elemento, string propriedade)
        {
            if (!elemento.TryGetProperty(propriedade, out var valor))
            {
                return null;
            }

            return valor.ValueKind switch
            {
                JsonValueKind.String => valor.GetString(),
                JsonValueKind.Number => valor.GetRawText(),
                _ => null
            };
        }

        private static string? Primeiro(params string?[] valores)
        {
            return valores.FirstOrDefault(valor => !string.IsNullOrWhiteSpace(valor))?.Trim();
        }
    }
}
