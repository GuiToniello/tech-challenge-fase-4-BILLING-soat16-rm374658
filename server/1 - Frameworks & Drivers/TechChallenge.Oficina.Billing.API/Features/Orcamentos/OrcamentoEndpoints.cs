using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.API.Features.Orcamentos
{
    public static class OrcamentoEndpoints
    {
        public static RouteGroupBuilder MapOrcamentoEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes
                .MapGroup("/api/orcamentos")
                .WithTags("Orcamentos");

            group.MapGet(
                "/{ordemServicoId:guid}",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.GetById(ordemServicoId, cancellationToken))
                .WithName("GetOrcamentoByOrdemServicoId")
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/gerar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Gerar(ordemServicoId, cancellationToken))
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/enviar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Enviar(ordemServicoId, cancellationToken))
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            // Aprovacao do cliente: cria a cobranca (Mercado Pago ou simulada) e devolve o link de pagamento
            group.MapPost(
                "/{ordemServicoId:guid}/aprovar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Aprovar(ordemServicoId, cancellationToken))
                .Produces<AprovacaoOrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/recusar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Recusar(ordemServicoId, cancellationToken))
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            return group;
        }
    }
}
