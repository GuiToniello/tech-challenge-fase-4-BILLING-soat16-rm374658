using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Billing.Integration.Tests.Features.Eventos;

// Saga do lado do Billing: eventos da Management e do OS Service (JSON como chegam da fila) aplicados pela EventosController
public sealed class EventosIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task StatusAntesDaValidacao_DeveCriarReferenciaParcialECompletarMantendoOStatus()
    {
        var ordemServicoId = Guid.NewGuid();

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2));
        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId);

        var referencia = await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync();
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        Assert.True(referencia.PossuiDadosParaOrcamento);
        Assert.Equal("Troca de Vela", Assert.Single(referencia.Servicos).Nome);
        Assert.IsType<Ok<OrcamentoViewModel>>(await _fixture.CriarOrcamentosEndpoints().Gerar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task StatusDepoisDaValidacao_DeveAtualizarSoOStatus()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 3));

        var referencia = await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync();
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, referencia.Status);
        Assert.Equal(60m, referencia.ValorTotal);
    }

    [Fact]
    public async Task StatusRepetido_DeveSerIgnorado()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, 1, 2));
    }

    [Fact]
    public async Task ValidacaoRepetida_DeveAtualizarOSnapshotSemDuplicar()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture, ordemServicoId, estoqueSuficiente: false);

        var referencia = await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync();
        Assert.False(referencia.EstoqueSuficiente);
        Assert.Single(referencia.Servicos);
    }

    [Fact]
    public async Task OrdemServicoExcluida_DeveRemoverReferenciaEOrcamentoMantendoOsPagamentos()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);
        await _fixture.CriarOrcamentosEndpoints().Gerar(ordemServicoId, CancellationToken.None);
        await _fixture.CriarOrcamentosEndpoints().Aprovar(ordemServicoId, CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId));

        var contexto = _fixture.ObterContexto();
        Assert.Empty(await contexto.OrdensServicoReferencias.ToListAsync());
        Assert.Empty(await contexto.Orcamentos.ToListAsync());
        Assert.Single(await contexto.Pagamentos.ToListAsync());
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId));
    }

    [Fact]
    public async Task OrdemServicoRejeitada_DeveRemoverAReferencia()
    {
        var ordemServicoId = await CenarioOrdemServico.ValidarOrdemServicoAsync(_fixture);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.RejeitarOrdemServicoAsync(_fixture, ordemServicoId));

        Assert.Empty(await _fixture.ObterContexto().OrdensServicoReferencias.ToListAsync());
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await _fixture.CriarOrcamentosEndpoints().Gerar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task EventoPublicadoPeloProprioBilling_DeveSerIgnorado()
    {
        var resultado = await CenarioOrdemServico.ProcessarAsync(_fixture, new OrcamentoGerado(Guid.NewGuid(), 10m));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
    }
}
