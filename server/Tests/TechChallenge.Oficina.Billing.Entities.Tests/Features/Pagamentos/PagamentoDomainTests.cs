using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using Xunit;

namespace TechChallenge.Oficina.Billing.Entities.Tests.Features.Pagamentos;

public sealed class PagamentoDomainTests
{
    private static Pagamento CriarPagamento(DateTime? criadoEm = null)
        => Pagamento.Criar(Guid.NewGuid(), 150.5m, ProvedorPagamento.MercadoPago, "pref-123", "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=pref-123", criadoEm);

    [Fact]
    public void Criar_DeveIniciarPendenteComOsDadosDaCobranca()
    {
        var ordemServicoId = Guid.NewGuid();
        var criadoEm = new DateTime(2026, 11, 18, 10, 00, 00, DateTimeKind.Utc);

        var pagamento = Pagamento.Criar(ordemServicoId, 99.999m, ProvedorPagamento.Simulado, " SIM-1 ", " https://simulado.mercadopago.local/checkout/1 ", criadoEm);

        Assert.NotEqual(Guid.Empty, pagamento.Id);
        Assert.Equal(ordemServicoId, pagamento.OrdemServicoId);
        Assert.Equal(100.00m, pagamento.Valor);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        Assert.Equal(ProvedorPagamento.Simulado, pagamento.Provedor);
        Assert.Equal("SIM-1", pagamento.PreferenciaId);
        Assert.Equal("https://simulado.mercadopago.local/checkout/1", pagamento.LinkPagamento);
        Assert.Null(pagamento.PagamentoExternoId);
        Assert.Equal(criadoEm, pagamento.CriadoEm);
        Assert.Equal(criadoEm, pagamento.AtualizadoEm);
    }

    [Theory]
    [InlineData(true, 10, 1, "pref", "link", "A ordem de servico do pagamento e obrigatoria.")]
    [InlineData(false, 0, 1, "pref", "link", "O valor do pagamento deve ser maior que zero.")]
    [InlineData(false, -5, 1, "pref", "link", "O valor do pagamento deve ser maior que zero.")]
    [InlineData(false, 10, 99, "pref", "link", "O provedor do pagamento informado e invalido.")]
    [InlineData(false, 10, 1, " ", "link", "O identificador da cobranca no provedor e obrigatorio e deve ter no maximo 100 caracteres.")]
    [InlineData(false, 10, 1, "pref", "", "O link de pagamento e obrigatorio e deve ter no maximo 500 caracteres.")]
    public void Criar_DeveLancar_QuandoDadosInvalidos(bool ordemVazia, double valor, int provedor, string preferenciaId, string link, string mensagem)
    {
        var action = () => Pagamento.Criar(ordemVazia ? Guid.Empty : Guid.NewGuid(), (decimal)valor, (ProvedorPagamento)provedor, preferenciaId, link);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(mensagem, exception.Message);
    }

    [Fact]
    public void Aprovar_DeveAlterarParaAprovadoComIdDoPagamentoNoProvedor()
    {
        var pagamento = CriarPagamento(new DateTime(2026, 11, 18, 10, 00, 00, DateTimeKind.Utc));
        var aprovadoEm = new DateTime(2026, 11, 18, 10, 05, 00, DateTimeKind.Utc);

        pagamento.Aprovar(" 123456 ", aprovadoEm);

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal("123456", pagamento.PagamentoExternoId);
        Assert.Equal(aprovadoEm, pagamento.AtualizadoEm);
    }

    [Fact]
    public void Aprovar_DevePermitir_QuandoUmaTentativaAnteriorFoiRecusada()
    {
        var pagamento = CriarPagamento();
        pagamento.Recusar("111");

        pagamento.Aprovar("222");

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal("222", pagamento.PagamentoExternoId);
    }

    [Fact]
    public void Aprovar_DeveLancar_QuandoJaAprovado()
    {
        var pagamento = CriarPagamento();
        pagamento.Aprovar("123");

        var action = () => pagamento.Aprovar("456");

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O pagamento ja foi aprovado.", exception.Message);
        Assert.Equal("123", pagamento.PagamentoExternoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Aprovar_DeveLancarSemAlterar_QuandoSemIdDoPagamentoNoProvedor(string pagamentoExternoId)
    {
        var pagamento = CriarPagamento();

        var action = () => pagamento.Aprovar(pagamentoExternoId);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O identificador do pagamento no provedor e obrigatorio.", exception.Message);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
    }

    [Fact]
    public void Recusar_DeveAlterarParaRecusado()
    {
        var pagamento = CriarPagamento();

        pagamento.Recusar("999");

        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Equal("999", pagamento.PagamentoExternoId);
    }

    [Fact]
    public void Recusar_DeveAceitarSemIdDoPagamentoNoProvedor()
    {
        var pagamento = CriarPagamento();

        pagamento.Recusar(null);

        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        Assert.Null(pagamento.PagamentoExternoId);
    }

    [Fact]
    public void Recusar_DeveLancar_QuandoNaoEstaPendente()
    {
        var pagamento = CriarPagamento();
        pagamento.Aprovar("123");

        var action = () => pagamento.Recusar("456");

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente pagamento pendente pode ser recusado.", exception.Message);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente, "Pendente")]
    [InlineData(StatusPagamento.Aprovado, "Aprovado")]
    [InlineData(StatusPagamento.Recusado, "Recusado")]
    public void StatusPagamentoExtensions_ObterDescricao_DeveRetornarDescricaoEsperada(StatusPagamento status, string descricao)
    {
        Assert.Equal(descricao, status.ObterDescricao());
    }

    [Fact]
    public void StatusPagamentoExtensions_ObterDescricao_DeveLancar_QuandoStatusInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StatusPagamento)99).ObterDescricao());
    }
}
