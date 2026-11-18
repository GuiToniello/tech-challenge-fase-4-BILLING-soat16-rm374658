using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.API.Features.Pagamentos
{
    public static class PagamentoEndpoints
    {
        public static RouteGroupBuilder MapPagamentoEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes
                .MapGroup("/api/pagamentos")
                .WithTags("Pagamentos");

            group.MapGet(
                "/{ordemServicoId:guid}",
                (
                    IPagamentosController pagamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => pagamentosController.GetById(ordemServicoId, cancellationToken))
                .WithName("GetPagamentoByOrdemServicoId")
                .Produces<PagamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            // Consulta a situacao no provedor e, se aprovado, confirma o orcamento e publica PagamentoAprovado
            group.MapPost(
                "/{ordemServicoId:guid}/verificar",
                (
                    IPagamentosController pagamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => pagamentosController.Verificar(ordemServicoId, cancellationToken))
                .Produces<PagamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            // Webhook do Mercado Pago: anonimo (o provedor nao envia o JWT) e sempre 200. A notificacao so informa o id;
            // a situacao do pagamento e consultada no proprio Mercado Pago com o access token, entao uma notificacao forjada nao aprova nada.
            group.MapPost(
                "/notificacoes",
                async (
                    IPagamentosController pagamentosController,
                    HttpRequest request,
                    CancellationToken cancellationToken
                ) =>
                {
                    var notificacao = await NotificacaoMercadoPago.LerAsync(request, cancellationToken);
                    return await pagamentosController.ReceberNotificacao(notificacao.Tipo, notificacao.RecursoId, cancellationToken);
                })
                .AllowAnonymous()
                .Produces(StatusCodes.Status200OK);

            return group;
        }
    }
}
