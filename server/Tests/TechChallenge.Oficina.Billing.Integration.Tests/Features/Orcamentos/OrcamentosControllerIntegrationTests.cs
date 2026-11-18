using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Features.Orcamentos;

public sealed class OrcamentosControllerIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private Task<object> GerarAsync(Guid ordemServicoId) => _fixture.CriarOrcamentosEndpoints().Gerar(ordemServicoId, CancellationToken.None);

    private Task<object> AprovarAsync(Guid ordemServicoId) => _fixture.CriarOrcamentosEndpoints().Aprovar(ordemServicoId, CancellationToken.None);

    [Fact]
    public async Task Gerar_OrdemValidada_DeveRetornar200ComOrcamentoEPublicarEvento()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await GerarAsync(ordemServicoId));

        Assert.Equal(ordemServicoId, ok.Value!.OrdemServicoId);
        Assert.Equal(60m, ok.Value.ValorTotal);
        Assert.Equal(1, ok.Value.Status);
        Assert.Equal("Troca de Vela", Assert.Single(ok.Value.Servicos).NomeServico);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(ordemServicoId, 60m), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_DuasVezes_DeveManterUmUnicoOrcamento()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        await GerarAsync(ordemServicoId);
        Assert.IsType<Ok<OrcamentoViewModel>>(await GerarAsync(ordemServicoId));

        Assert.Single(await _fixture.ObterContexto().Set<Orcamento>().ToListAsync());
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), ordemServicoId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Gerar_OrdemInexistente_DeveRetornar404()
    {
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await GerarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Gerar_EstoqueInsuficiente_DeveRetornar400SemCriarOrcamento()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, estoqueSuficiente: false);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(await GerarAsync(ordemServicoId));

        Assert.Equal("Estoque insuficiente para gerar orcamento.", badRequest.Value!["message"]);
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().GetById(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Gerar_OrdemEmExecucao_DeveRetornar400()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, (int)StatusOrdemServico.Recebida, (int)StatusOrdemServico.EmExecucao);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await GerarAsync(ordemServicoId));
    }

    [Fact]
    public async Task Gerar_OrdemAindaNaoValidadaPelaManagement_DeveRetornar400()
    {
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, (int)StatusOrdemServico.Recebida, (int)StatusOrdemServico.EmDiagnostico);

        var badRequest = Assert.IsType<BadRequest<Dictionary<string, string?>>>(await GerarAsync(ordemServicoId));

        Assert.Equal("A ordem de servico ainda nao foi validada pela Management. Tente novamente em instantes.", badRequest.Value!["message"]);
    }

    [Fact]
    public async Task GetById_OrcamentoGerado_DeveRetornar200()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().GetById(ordemServicoId, CancellationToken.None));

        Assert.Equal("Gerado", ok.Value!.StatusDescricao);
    }

    [Fact]
    public async Task GetById_OrcamentoInexistente_DeveRetornar404()
    {
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().GetById(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Enviar_OrcamentoGerado_DeveRetornar204EEnviarEmailAoCliente()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);

        Assert.IsType<NoContent>(await _fixture.CriarOrcamentosEndpoints().Enviar(ordemServicoId, CancellationToken.None));

        _fixture.OrcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(
            It.Is<Orcamento>(o => o.OrdemServicoId == ordemServicoId),
            CenarioOrdemServico.EmailCliente,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enviar_OrcamentoAprovado_DeveIncluirOLinkDaCobrancaPendente()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);
        var aprovacao = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(await AprovarAsync(ordemServicoId)).Value!;

        Assert.IsType<NoContent>(await _fixture.CriarOrcamentosEndpoints().Enviar(ordemServicoId, CancellationToken.None));

        _fixture.OrcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(
            It.IsAny<Orcamento>(),
            CenarioOrdemServico.EmailCliente,
            aprovacao.Pagamento.LinkPagamento,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enviar_ClienteSemEmail_DeveRetornar400()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, email: null);
        await GerarAsync(ordemServicoId);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Enviar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Enviar_OrcamentoInexistente_DeveRetornar404()
    {
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Enviar(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Aprovar_OrcamentoGerado_DeveRetornar200ComCobrancaSimuladaSemLiberarAOrdem()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);

        var ok = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(await AprovarAsync(ordemServicoId));

        Assert.Equal(2, ok.Value!.Orcamento.Status);
        Assert.Equal("Aguardando pagamento", ok.Value.Orcamento.StatusDescricao);
        Assert.Equal(1, ok.Value.Pagamento.Status);
        Assert.Equal("Simulado", ok.Value.Pagamento.Provedor);
        Assert.Equal(60m, ok.Value.Pagamento.Valor);
        Assert.Equal($"https://simulado.mercadopago.local/checkout/{ordemServicoId}", ok.Value.Pagamento.LinkPagamento);
        // So o OrcamentoGerado: a OS segue para execucao apenas com o PagamentoAprovado
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<PagamentoAprovado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Aprovar_DuasVezes_DeveDevolverAMesmaCobranca()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);
        var primeira = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(await AprovarAsync(ordemServicoId)).Value!;

        var segunda = Assert.IsType<Ok<AprovacaoOrcamentoViewModel>>(await AprovarAsync(ordemServicoId)).Value!;

        Assert.Equal(primeira.Pagamento.Id, segunda.Pagamento.Id);
        Assert.Equal(primeira.Pagamento.PreferenciaId, segunda.Pagamento.PreferenciaId);
        Assert.Single(await _fixture.ObterContexto().Pagamentos.ToListAsync());
    }

    [Fact]
    public async Task Aprovar_SemOrcamento_DeveRetornar404()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await AprovarAsync(ordemServicoId));
    }

    [Fact]
    public async Task Recusar_OrcamentoGerado_DeveRetornar200ComStatusRecusadoEPublicarEvento()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().Recusar(ordemServicoId, CancellationToken.None));

        Assert.Equal(4, ok.Value!.Status);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recusar_SemOrcamento_DeveRetornar404()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Recusar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Recusar_DuasVezes_DeveRetornar400()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);
        await _fixture.CriarOrcamentosEndpoints().Recusar(ordemServicoId, CancellationToken.None);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Recusar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Gerar_AposAprovacao_DeveRetornar400()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await GerarAsync(ordemServicoId);
        await AprovarAsync(ordemServicoId);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await GerarAsync(ordemServicoId));
    }
}
