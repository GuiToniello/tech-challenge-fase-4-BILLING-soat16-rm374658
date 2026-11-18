using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using TechChallenge.Oficina.Billing.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Adapters.Tests.Features.Orcamentos;

public sealed class OrcamentosAdapterTests
{
    private readonly OrcamentosAdapter _adapter = new();

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoOrcamentoViewModelSucesso()
    {
        var orcamento = new OrcamentoViewModel { OrdemServicoId = Guid.NewGuid(), ValorTotal = 50m };
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(orcamento);

        var adaptado = _adapter.Adapt(result);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(adaptado);
        Assert.Equal(orcamento, ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarBadRequestComMensagem_QuandoDomainException()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new DomainException("erro de regra"));

        var adaptado = _adapter.Adapt(result);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(adaptado);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Equal("erro de regra", badRequest.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFoundComMensagem_QuandoKeyNotFoundException()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new KeyNotFoundException("Orcamento nao encontrado."));

        var adaptado = _adapter.Adapt(result);

        var notFound = Assert.IsType<NotFound<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("Orcamento nao encontrado.", notFound.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarProblem_QuandoExcecaoInesperada()
    {
        var result = new OrcamentosResult<OrcamentoViewModel, Exception>(new InvalidOperationException("falha"));

        var adaptado = _adapter.Adapt(result);

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode.StatusCode);
    }

    [Fact]
    public void Adapt_DeveRetornarOkComOrcamentoECobranca_QuandoAprovacaoSucesso()
    {
        var aprovacao = new AprovacaoOrcamentoViewModel
        {
            Orcamento = new OrcamentoViewModel { OrdemServicoId = Guid.NewGuid(), Status = 2 },
            Pagamento = new PagamentoViewModel { LinkPagamento = "https://checkout/1" }
        };

        var adaptado = _adapter.Adapt(new OrcamentosResult<AprovacaoOrcamentoViewModel, Exception>(aprovacao));

        var ok = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(adaptado);
        Assert.Equal("https://checkout/1", ok.Value!.Pagamento.LinkPagamento);
    }

    [Fact]
    public void Adapt_DeveRetornarBadRequest_QuandoAprovacaoComDomainException()
    {
        var adaptado = _adapter.Adapt(new OrcamentosResult<AprovacaoOrcamentoViewModel, Exception>(new DomainException("Somente orcamento gerado ou aguardando pagamento pode ser aprovado.")));

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(adaptado);
        Assert.Equal("Somente orcamento gerado ou aguardando pagamento pode ser aprovado.", badRequest.Value!["message"]);
    }

    [Fact]
    public void Adapt_DeveRetornarOk_QuandoBoolSucesso()
    {
        var adaptado = _adapter.Adapt(new OrcamentosResult<bool, Exception>(true));

        var ok = Assert.IsType<Ok<bool>>(adaptado);
        Assert.True(ok.Value);
    }

    [Fact]
    public void Adapt_DeveRetornarNotFound_QuandoBoolComKeyNotFoundException()
    {
        var adaptado = _adapter.Adapt(new OrcamentosResult<bool, Exception>(new KeyNotFoundException("nao encontrado")));

        var statusCode = Assert.IsAssignableFrom<IStatusCodeHttpResult>(adaptado);
        Assert.Equal(StatusCodes.Status404NotFound, statusCode.StatusCode);
    }

    [Fact]
    public void Empty_DeveRetornarNoContent()
    {
        Assert.IsType<NoContent>(_adapter.Empty());
    }
}
