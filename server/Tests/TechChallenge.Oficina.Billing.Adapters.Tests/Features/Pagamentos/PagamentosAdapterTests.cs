using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TechChallenge.Oficina.Billing.Adapters.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Adapters.Tests.Features.Pagamentos;

public sealed class PagamentosAdapterTests
{
    private readonly PagamentosAdapter _adapter = new();

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoPagamentoViewModelSucesso()
    {
        var pagamento = new PagamentoViewModel { Id = Guid.NewGuid(), Status = 1, LinkPagamento = "https://checkout/1" };

        var adaptado = _adapter.Adapt(new PagamentosResult<PagamentoViewModel, Exception>(pagamento));

        var ok = Assert.IsType<Ok<PagamentoViewModel>>(adaptado);
        Assert.Same(pagamento, ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFoundComMensagem_QuandoKeyNotFoundException()
    {
        var adaptado = _adapter.Adapt(new PagamentosResult<PagamentoViewModel, Exception>(new KeyNotFoundException("Pagamento nao encontrado.")));

        var notFound = Assert.IsType<NotFound<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("Pagamento nao encontrado.", notFound.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarBadRequestComMensagem_QuandoDomainException()
    {
        var adaptado = _adapter.Adapt(new PagamentosResult<PagamentoViewModel, Exception>(new DomainException("erro de regra")));

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("erro de regra", badRequest.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarProblem_QuandoExcecaoInesperada()
    {
        var adaptado = _adapter.Adapt(new PagamentosResult<PagamentoViewModel, Exception>(new InvalidOperationException("falha")));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode.StatusCode);
    }

    [Fact]
    public void Recebido_DeveRetornarOkSemCorpo()
    {
        var adaptado = _adapter.Recebido();

        var ok = Assert.IsType<Ok>(adaptado);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
    }
}
