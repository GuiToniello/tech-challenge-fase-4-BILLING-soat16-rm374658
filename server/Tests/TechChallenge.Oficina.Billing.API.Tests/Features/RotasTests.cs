using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechChallenge.Oficina.Billing.API.Features.Orcamentos;
using TechChallenge.Oficina.Billing.API.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using Xunit;

namespace TechChallenge.Oficina.Billing.API.Tests.Features;

public sealed class RotasTests
{
    private static List<RouteEndpoint> MapearRotas()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => Mock.Of<IOrcamentosController>());
        builder.Services.AddScoped(_ => Mock.Of<IPagamentosController>());
        var app = builder.Build();

        app.MapOrcamentoEndpoints();
        app.MapPagamentoEndpoints();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(fonte => fonte.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static string Descrever(RouteEndpoint endpoint) =>
        $"{endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single()} {endpoint.RoutePattern.RawText}";

    [Fact]
    public void Endpoints_DevemMapearAsRotasDeOrcamentosEPagamentos()
    {
        var rotas = MapearRotas().Select(Descrever).OrderBy(rota => rota).ToArray();

        Assert.Equal(
            new[]
            {
                "GET /api/orcamentos/{ordemServicoId:guid}",
                "GET /api/pagamentos/{ordemServicoId:guid}",
                "POST /api/orcamentos/{ordemServicoId:guid}/aprovar",
                "POST /api/orcamentos/{ordemServicoId:guid}/enviar",
                "POST /api/orcamentos/{ordemServicoId:guid}/gerar",
                "POST /api/orcamentos/{ordemServicoId:guid}/recusar",
                "POST /api/pagamentos/notificacoes",
                "POST /api/pagamentos/{ordemServicoId:guid}/verificar"
            }.OrderBy(rota => rota),
            rotas);
    }

    [Fact]
    public void Notificacoes_DeveSerAUnicaRotaAnonima()
    {
        // O Mercado Pago nao envia o JWT: so o webhook dispensa autenticacao; as demais rotas seguem a politica padrao (usuario autenticado)
        var anonimas = MapearRotas()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Descrever);

        Assert.Equal(["POST /api/pagamentos/notificacoes"], anonimas);
    }

    [Fact]
    public void GetById_DeveTerAsRotasNomeadas()
    {
        var nomes = MapearRotas()
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(nome => nome is not null)
            .OrderBy(nome => nome);

        Assert.Equal(["GetOrcamentoByOrdemServicoId", "GetPagamentoByOrdemServicoId"], nomes);
    }
}
