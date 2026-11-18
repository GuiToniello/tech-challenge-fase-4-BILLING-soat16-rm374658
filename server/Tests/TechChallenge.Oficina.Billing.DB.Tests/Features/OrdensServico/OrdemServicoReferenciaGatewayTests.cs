using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using Xunit;

namespace TechChallenge.Oficina.Billing.DB.Data.Tests.Features.OrdensServico;

public sealed class OrdemServicoReferenciaGatewayTests
{
    [Fact]
    public async Task AdicionarAsync_DevePersistirSnapshotComServicos()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servicos = new[] { CriarServico("Troca de oleo", 50m), CriarServico("Alinhamento", 30m) };
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cliente@teste.com", true, servicos, 80m);

        await using (var context = CriarContexto(nomeBanco))
        {
            await new OrdemServicoReferenciaGateway(context).AdicionarAsync(referencia);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id);

        Assert.NotNull(salva);
        Assert.Equal(referencia.ClienteId, salva!.ClienteId);
        Assert.Equal(referencia.VeiculoId, salva.VeiculoId);
        Assert.Equal("cliente@teste.com", salva.ClienteEmail);
        Assert.True(salva.EstoqueSuficiente);
        Assert.True(salva.PossuiDadosParaOrcamento);
        Assert.Equal(80m, salva.ValorTotal);
        Assert.Equal(StatusOrdemServico.Recebida, salva.Status);
        Assert.Equal(servicos.Select(servico => servico.ServicoId).Order(), salva.Servicos.Select(servico => servico.ServicoId).Order());
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirNovoSnapshotEStatus_QuandoReferenciaParcialECompletada()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var referencia = OrdemServicoReferencia.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico);
        var novoServico = CriarServico("Balanceamento", 70m);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrdemServicoReferenciaGateway(context);
            await gateway.AdicionarAsync(referencia);

            var carregada = await gateway.ObterPorIdAsync(referencia.Id);
            carregada!.AtualizarDados(carregada.ClienteId, Guid.NewGuid(), null, false, [novoServico], 70m);
            carregada.AtualizarStatus(StatusOrdemServico.AguardandoAprovacao);
            await gateway.AtualizarAsync(carregada);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id);

        Assert.Equal(novoServico.ServicoId, Assert.Single(salva!.Servicos).ServicoId);
        Assert.Equal(StatusOrdemServico.AguardandoAprovacao, salva.Status);
        Assert.True(salva.PossuiDadosParaOrcamento);
        Assert.False(salva.EstoqueSuficiente);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarNull_QuandoNaoExiste()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var encontrada = await new OrdemServicoReferenciaGateway(context).ObterPorIdAsync(Guid.NewGuid());

        Assert.Null(encontrada);
    }

    [Fact]
    public async Task RemoverAsync_DeveExcluirReferencia()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());
        var gateway = new OrdemServicoReferenciaGateway(context);
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, true, [CriarServico("Troca", 50m)], 50m);
        await gateway.AdicionarAsync(referencia);

        await gateway.RemoverAsync(referencia);

        Assert.Empty(await context.OrdensServicoReferencias.ToListAsync());
    }

    private static ServicoOrdemServico CriarServico(string nome, decimal valor)
        => ServicoOrdemServico.Criar(Guid.NewGuid(), nome, valor);

    private static BillingDbContext CriarContexto(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .Options;

        return new BillingDbContext(options);
    }
}
