using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.Billing.Pagamentos.Features.Gateways;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Features.Pagamentos;

// Fluxo de pagamento com o gateway simulado (sem access token do Mercado Pago): toda consulta devolve o pagamento aprovado
public sealed class PagamentosControllerIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<(Guid OrdemServicoId, AprovacaoOrcamentoViewModel Aprovacao)> AprovarOrcamentoAsync()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await _fixture.CriarOrcamentosEndpoints().Gerar(ordemServicoId, CancellationToken.None);
        var aprovacao = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().Aprovar(ordemServicoId, CancellationToken.None));
        return (ordemServicoId, aprovacao.Value!);
    }

    private void VerificarPagamentoAprovadoPublicado(Guid ordemServicoId, Times vezes) =>
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.Is<PagamentoAprovado>(evento => evento.OrdemServicoId == ordemServicoId && evento.Valor == 60m), ordemServicoId, It.IsAny<CancellationToken>()), vezes);

    [Fact]
    public async Task GetById_DeveRetornarACobrancaPendenteComOLink()
    {
        var (ordemServicoId, aprovacao) = await AprovarOrcamentoAsync();

        var ok = Assert.IsType<Ok<PagamentoViewModel>>(await _fixture.CriarPagamentosEndpoints().GetById(ordemServicoId, CancellationToken.None));

        Assert.Equal(aprovacao.Pagamento.Id, ok.Value!.Id);
        Assert.Equal("Pendente", ok.Value.StatusDescricao);
        Assert.Equal(aprovacao.Pagamento.LinkPagamento, ok.Value.LinkPagamento);
    }

    [Fact]
    public async Task GetById_SemCobranca_DeveRetornar404()
    {
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarPagamentosEndpoints().GetById(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Verificar_DeveAprovarPagamentoConfirmarOrcamentoEPublicarPagamentoAprovadoUmaVez()
    {
        var (ordemServicoId, aprovacao) = await AprovarOrcamentoAsync();

        var ok = Assert.IsType<Ok<PagamentoViewModel>>(await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None));
        var repetida = Assert.IsType<Ok<PagamentoViewModel>>(await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None));

        Assert.Equal((int)StatusPagamento.Aprovado, ok.Value!.Status);
        Assert.Equal(GatewayPagamentoSimulado.CriarPagamentoExternoId(ordemServicoId), ok.Value.PagamentoExternoId);
        Assert.Equal((int)StatusPagamento.Aprovado, repetida.Value!.Status);
        var orcamento = Assert.IsType<Ok<OrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().GetById(ordemServicoId, CancellationToken.None));
        Assert.Equal("Aprovado", orcamento.Value!.StatusDescricao);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new PagamentoAprovado(ordemServicoId, aprovacao.Pagamento.Id, 60m), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verificar_SemCobranca_DeveRetornar404()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Aprovar_AposPagamentoConfirmado_DeveRetornar400()
    {
        var (ordemServicoId, _) = await AprovarOrcamentoAsync();
        await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Aprovar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task ReceberNotificacao_ComPagamentoDoSimulado_DeveAprovarEPublicarUmaVez()
    {
        var (ordemServicoId, _) = await AprovarOrcamentoAsync();
        var pagamentoExternoId = GatewayPagamentoSimulado.CriarPagamentoExternoId(ordemServicoId);

        Assert.IsType<Ok>(await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", pagamentoExternoId, CancellationToken.None));
        Assert.IsType<Ok>(await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", pagamentoExternoId, CancellationToken.None));

        var pagamento = await _fixture.ObterContexto().Pagamentos.SingleAsync();
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        VerificarPagamentoAprovadoPublicado(ordemServicoId, Times.Once());
    }

    [Fact]
    public async Task GetHistorico_AposAprovarEVerificar_DeveRetornarCobrancaCriadaEPagamentoAprovadoEmOrdem()
    {
        var (ordemServicoId, aprovacao) = await AprovarOrcamentoAsync();
        await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None);
        // Ja aprovado: a nova verificacao nao acrescenta nada ao historico
        await _fixture.CriarPagamentosEndpoints().Verificar(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>>>(await _fixture.CriarPagamentosEndpoints().GetHistorico(ordemServicoId, CancellationToken.None));

        var historico = ok.Value!.ToArray();
        Assert.Equal(["CobrancaCriada", "PagamentoAprovado"], historico.Select(evento => evento.Tipo));
        Assert.Equal(["aprovacao", "verificacao"], historico.Select(evento => evento.Origem));
        Assert.All(historico, evento =>
        {
            Assert.Equal(ordemServicoId, evento.OrdemServicoId);
            Assert.Equal(aprovacao.Pagamento.Id, evento.PagamentoId);
            Assert.Equal("Simulado", evento.Provedor);
            Assert.Equal(60m, evento.Valor);
        });
        Assert.Equal(aprovacao.Pagamento.PreferenciaId, historico[0].PreferenciaId);
        Assert.Equal(GatewayPagamentoSimulado.CriarPagamentoExternoId(ordemServicoId), historico[1].PagamentoExternoId);
        Assert.True(historico[0].OcorridoEm <= historico[1].OcorridoEm);
    }

    [Fact]
    public async Task GetHistorico_AposNotificacoes_DeveRegistrarCadaNotificacaoEOResultadoUmaVez()
    {
        var (ordemServicoId, _) = await AprovarOrcamentoAsync();
        var pagamentoExternoId = GatewayPagamentoSimulado.CriarPagamentoExternoId(ordemServicoId);
        await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", pagamentoExternoId, CancellationToken.None);
        await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", pagamentoExternoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>>>(await _fixture.CriarPagamentosEndpoints().GetHistorico(ordemServicoId, CancellationToken.None));

        Assert.Equal(["CobrancaCriada", "NotificacaoRecebida", "PagamentoAprovado", "NotificacaoRecebida"], ok.Value!.Select(evento => evento.Tipo));
    }

    [Fact]
    public async Task GetHistorico_SemCobranca_DeveRetornarListaVazia()
    {
        var ok = Assert.IsType<Ok<IReadOnlyCollection<EventoHistoricoPagamentoViewModel>>>(await _fixture.CriarPagamentosEndpoints().GetHistorico(Guid.NewGuid(), CancellationToken.None));

        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task ReceberNotificacao_ComIdDesconhecido_DeveResponder200SemAlterar()
    {
        var (ordemServicoId, _) = await AprovarOrcamentoAsync();

        Assert.IsType<Ok>(await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", "123", CancellationToken.None));

        var pagamento = await _fixture.ObterContexto().Pagamentos.SingleAsync();
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        VerificarPagamentoAprovadoPublicado(ordemServicoId, Times.Never());
    }

    [Fact]
    public async Task ReceberNotificacao_DepoisDaRecusaDoOrcamento_DeveRegistrarOPagamentoSemLiberarAOrdem()
    {
        var (ordemServicoId, _) = await AprovarOrcamentoAsync();
        Assert.IsType<Ok<OrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().Recusar(ordemServicoId, CancellationToken.None));

        await _fixture.CriarPagamentosEndpoints().ReceberNotificacao("payment", GatewayPagamentoSimulado.CriarPagamentoExternoId(ordemServicoId), CancellationToken.None);

        var pagamento = await _fixture.ObterContexto().Pagamentos.SingleAsync();
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        VerificarPagamentoAprovadoPublicado(ordemServicoId, Times.Never());
    }
}
