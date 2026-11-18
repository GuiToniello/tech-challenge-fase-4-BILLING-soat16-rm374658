using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using Xunit;

namespace TechChallenge.Oficina.Billing.DB.Data.Tests.Features.Pagamentos;

public sealed class PagamentoGatewayTests
{
    private static readonly DateTime Inicio = new(2026, 11, 18, 10, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task AdicionarAsync_DevePersistirCobrancaPendente()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var pagamento = CriarPagamento(Guid.NewGuid(), Inicio);

        await using (var context = CriarContexto(nomeBanco))
        {
            await new PagamentoGateway(context).AdicionarAsync(pagamento);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new PagamentoGateway(novoContexto).ObterUltimoPorOrdemServicoIdAsync(pagamento.OrdemServicoId);

        Assert.NotNull(salvo);
        Assert.Equal(pagamento.Id, salvo!.Id);
        Assert.Equal(StatusPagamento.Pendente, salvo.Status);
        Assert.Equal(ProvedorPagamento.MercadoPago, salvo.Provedor);
        Assert.Equal("pref-1", salvo.PreferenciaId);
        Assert.Equal("https://checkout/pref-1", salvo.LinkPagamento);
        Assert.Equal(120m, salvo.Valor);
    }

    [Fact]
    public async Task ObterUltimoPorOrdemServicoIdAsync_DeveRetornarACobrancaMaisRecenteDaOrdem()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var ordemServicoId = Guid.NewGuid();
        var antiga = CriarPagamento(ordemServicoId, Inicio);
        antiga.Recusar("111");
        var recente = CriarPagamento(ordemServicoId, Inicio.AddMinutes(10));
        var deOutraOrdem = CriarPagamento(Guid.NewGuid(), Inicio.AddMinutes(20));

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new PagamentoGateway(context);
            await gateway.AdicionarAsync(antiga);
            await gateway.AdicionarAsync(recente);
            await gateway.AdicionarAsync(deOutraOrdem);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var ultimo = await new PagamentoGateway(novoContexto).ObterUltimoPorOrdemServicoIdAsync(ordemServicoId);

        Assert.Equal(recente.Id, ultimo!.Id);
    }

    [Fact]
    public async Task ObterUltimoPorOrdemServicoIdAsync_DeveRetornarNull_QuandoOrdemSemCobranca()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var ultimo = await new PagamentoGateway(context).ObterUltimoPorOrdemServicoIdAsync(Guid.NewGuid());

        Assert.Null(ultimo);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirAprovacaoComIdExterno()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var pagamento = CriarPagamento(Guid.NewGuid(), Inicio);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new PagamentoGateway(context);
            await gateway.AdicionarAsync(pagamento);
            var carregado = await gateway.ObterUltimoPorOrdemServicoIdAsync(pagamento.OrdemServicoId);
            carregado!.Aprovar("123456", Inicio.AddMinutes(5));
            await gateway.AtualizarAsync(carregado);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new PagamentoGateway(novoContexto).ObterUltimoPorOrdemServicoIdAsync(pagamento.OrdemServicoId);

        Assert.Equal(StatusPagamento.Aprovado, salvo!.Status);
        Assert.Equal("123456", salvo.PagamentoExternoId);
        Assert.Equal(Inicio.AddMinutes(5), salvo.AtualizadoEm);
    }

    [Fact]
    public async Task AtualizarAsync_DeveLancarConcorrencia_QuandoOutraNotificacaoJaAprovouOPagamento()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var pagamento = CriarPagamento(Guid.NewGuid(), Inicio);

        await using (var context = CriarContexto(nomeBanco))
        {
            await new PagamentoGateway(context).AdicionarAsync(pagamento);
        }

        await using var primeiroContexto = CriarContexto(nomeBanco);
        await using var segundoContexto = CriarContexto(nomeBanco);
        var primeiro = await new PagamentoGateway(primeiroContexto).ObterUltimoPorOrdemServicoIdAsync(pagamento.OrdemServicoId);
        var segundo = await new PagamentoGateway(segundoContexto).ObterUltimoPorOrdemServicoIdAsync(pagamento.OrdemServicoId);
        primeiro!.Aprovar("123");
        await new PagamentoGateway(primeiroContexto).AtualizarAsync(primeiro);
        segundo!.Aprovar("123");

        var action = async () => await new PagamentoGateway(segundoContexto).AtualizarAsync(segundo);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(action);
    }

    private static Pagamento CriarPagamento(Guid ordemServicoId, DateTime criadoEm)
        => Pagamento.Criar(ordemServicoId, 120m, ProvedorPagamento.MercadoPago, "pref-1", "https://checkout/pref-1", criadoEm);

    private static BillingDbContext CriarContexto(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .Options;

        return new BillingDbContext(options);
    }
}
