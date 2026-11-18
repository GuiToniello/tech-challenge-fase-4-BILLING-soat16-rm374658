using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using Xunit;

namespace TechChallenge.Oficina.Billing.DB.Data.Tests.Features.Orcamentos;

public sealed class OrcamentoGatewayTests
{
    [Fact]
    public async Task AdicionarAsync_DevePersistirOrcamentoComServicos()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servico = CriarServico("Troca de oleo", 50m);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));

        await using (var context = CriarContexto(nomeBanco))
        {
            await new OrcamentoGateway(context).AdicionarAsync(orcamento);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.NotNull(salvo);
        Assert.Equal(50m, salvo!.ValorTotal);
        Assert.Equal(StatusOrcamento.Gerado, salvo.Status);
        var item = Assert.Single(salvo.Servicos);
        Assert.Equal(servico.ServicoId, item.ServicoId);
        Assert.Equal("Troca de oleo", item.NomeServico);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirSomenteUltimoResultado_QuandoOrcamentoRegerado()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servico = CriarServico("Troca", 50m);
        var outroServico = CriarServico("Alinhamento", 30m);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrcamentoGateway(context);
            await gateway.AdicionarAsync(orcamento);

            var carregado = await gateway.ObterPorIdAsync(orcamento.OrdemServicoId);
            carregado!.Regerar([servico, outroServico], new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc));
            await gateway.AtualizarAsync(carregado);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.Equal(80m, salvo!.ValorTotal);
        Assert.Equal(2, salvo.Servicos.Count);
        Assert.Equal(new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc), salvo.DataGeracao);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirStatusAguardandoPagamento()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 50m)]);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrcamentoGateway(context);
            await gateway.AdicionarAsync(orcamento);
            orcamento.AprovarParaPagamento();
            await gateway.AtualizarAsync(orcamento);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.Equal(StatusOrcamento.AguardandoPagamento, salvo!.Status);
    }

    [Fact]
    public async Task AtualizarAsync_DeveLancarConcorrencia_QuandoOutraRequisicaoJaAlterouOStatus()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 50m)]);

        await using (var context = CriarContexto(nomeBanco))
        {
            await new OrcamentoGateway(context).AdicionarAsync(orcamento);
        }

        await using var primeiroContexto = CriarContexto(nomeBanco);
        await using var segundoContexto = CriarContexto(nomeBanco);
        var primeiro = await new OrcamentoGateway(primeiroContexto).ObterPorIdAsync(orcamento.OrdemServicoId);
        var segundo = await new OrcamentoGateway(segundoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);
        primeiro!.AprovarParaPagamento();
        await new OrcamentoGateway(primeiroContexto).AtualizarAsync(primeiro);
        segundo!.Recusar();

        var action = async () => await new OrcamentoGateway(segundoContexto).AtualizarAsync(segundo);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(action);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarNull_QuandoNaoExiste()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var encontrado = await new OrcamentoGateway(context).ObterPorIdAsync(Guid.NewGuid());

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task RemoverAsync_DeveExcluirOrcamento()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());
        var gateway = new OrcamentoGateway(context);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 50m)]);
        await gateway.AdicionarAsync(orcamento);

        await gateway.RemoverAsync(orcamento);

        Assert.Empty(await context.Orcamentos.ToListAsync());
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
