using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using Xunit;

namespace TechChallenge.Oficina.Billing.Entities.Tests.Features.Orcamentos;

public sealed class OrcamentoDomainTests
{
    private static ServicoOrdemServico CriarServico(string nome, decimal valor, Guid? servicoId = null)
        => ServicoOrdemServico.Criar(servicoId ?? Guid.NewGuid(), nome, valor);

    private static Orcamento CriarOrcamento(decimal valor = 50m)
        => Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", valor)]);

    [Fact]
    public void Orcamento_Criar_DeveCalcularValoresTotaisEIniciarComoGerado()
    {
        var ordemServicoId = Guid.NewGuid();
        var servicoA = CriarServico("Troca de oleo", 41m);
        var servicoB = CriarServico("Alinhamento", 30m);
        var dataGeracao = new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc);

        var orcamento = Orcamento.Criar(ordemServicoId, [servicoA, servicoB], dataGeracao);

        Assert.Equal(ordemServicoId, orcamento.Id);
        Assert.Equal(ordemServicoId, orcamento.OrdemServicoId);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        Assert.Equal(dataGeracao, orcamento.DataGeracao);
        Assert.Equal(71m, orcamento.ValorTotal);
        Assert.Equal(41m, orcamento.Servicos.Single(s => s.ServicoId == servicoA.ServicoId).ValorTotal);
        Assert.Equal("Alinhamento", orcamento.Servicos.Single(s => s.ServicoId == servicoB.ServicoId).NomeServico);
    }

    [Fact]
    public void Orcamento_Criar_DeveIgnorarServicosDuplicados()
    {
        var servico = CriarServico("Troca", 50m);

        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico, servico]);

        Assert.Single(orcamento.Servicos);
        Assert.Equal(50m, orcamento.ValorTotal);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoOrdemServicoIdForInvalido()
    {
        var action = () => Orcamento.Criar(Guid.Empty, [CriarServico("Troca", 10m)]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico do orcamento e obrigatoria.", exception.Message);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoDataGeracaoForInvalida()
    {
        var action = () => Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 10m)], default(DateTime));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A data de geracao do orcamento e obrigatoria.", exception.Message);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoNaoPossuirServicos()
    {
        var action = () => Orcamento.Criar(Guid.NewGuid(), []);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico para gerar orcamento.", exception.Message);
    }

    [Fact]
    public void OrcamentoServico_Criar_DeveLancarQuandoServicoForNulo()
    {
        var action = () => OrcamentoServico.Criar(null!);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O servico do orcamento e obrigatorio.", exception.Message);
    }

    [Fact]
    public void Orcamento_Regerar_DeveManterSomenteUltimoResultado()
    {
        var servicoId = Guid.NewGuid();
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 50m, servicoId)], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));
        var novaData = new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc);

        // Snapshot atualizado pela Management (OrdemServicoValidada) com o novo valor do servico
        orcamento.Regerar([CriarServico("Troca", 80m, servicoId)], novaData);

        Assert.Equal(80m, orcamento.ValorTotal);
        Assert.Equal(novaData, orcamento.DataGeracao);
        Assert.Single(orcamento.Servicos);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Regerar_DeveLancarSemAlterarOrcamento_QuandoJaFoiAprovadoParaPagamento()
    {
        var servico = CriarServico("Troca", 50m);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico]);
        orcamento.AprovarParaPagamento();

        var action = () => orcamento.Regerar([CriarServico("Alinhamento", 30m)]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser gerado novamente.", exception.Message);
        Assert.Equal(50m, orcamento.ValorTotal);
        Assert.Equal(servico.ServicoId, orcamento.Servicos.Single().ServicoId);
    }

    [Fact]
    public void Orcamento_Regerar_DeveLancarSemAlterarOrcamento_QuandoNaoPossuirServicos()
    {
        var orcamento = CriarOrcamento();

        var action = () => orcamento.Regerar([]);

        Assert.Throws<DomainException>(action);
        Assert.Single(orcamento.Servicos);
        Assert.Equal(50m, orcamento.ValorTotal);
    }

    [Fact]
    public void Orcamento_Regerar_DeveLancarSemAlterarOrcamento_QuandoServicoNulo()
    {
        var orcamento = CriarOrcamento();

        var action = () => orcamento.Regerar([CriarServico("Alinhamento", 30m), null!]);

        Assert.Throws<DomainException>(action);
        Assert.Equal("Troca", orcamento.Servicos.Single().NomeServico);
        Assert.Equal(50m, orcamento.ValorTotal);
    }

    [Fact]
    public void Orcamento_AprovarParaPagamento_DeveAlterarStatusParaAguardandoPagamento()
    {
        var orcamento = CriarOrcamento();

        orcamento.AprovarParaPagamento();

        Assert.Equal(StatusOrcamento.AguardandoPagamento, orcamento.Status);
    }

    [Fact]
    public void Orcamento_AprovarParaPagamento_DeveLancar_QuandoJaAguardaPagamento()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();

        var action = () => orcamento.AprovarParaPagamento();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser aprovado para pagamento.", exception.Message);
    }

    [Fact]
    public void Orcamento_ValidarAprovacao_DevePermitir_QuandoGeradoOuAguardandoPagamento()
    {
        var orcamento = CriarOrcamento();
        orcamento.ValidarAprovacao();

        orcamento.AprovarParaPagamento();
        orcamento.ValidarAprovacao();

        Assert.Equal(StatusOrcamento.AguardandoPagamento, orcamento.Status);
    }

    [Fact]
    public void Orcamento_ValidarAprovacao_DeveLancar_QuandoJaFoiRecusado()
    {
        var orcamento = CriarOrcamento();
        orcamento.Recusar();

        var action = () => orcamento.ValidarAprovacao();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado ou aguardando pagamento pode ser aprovado.", exception.Message);
        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_ValidarAprovacao_DeveLancar_QuandoPagamentoJaConfirmado()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();
        orcamento.ConfirmarPagamento();

        var action = () => orcamento.ValidarAprovacao();

        Assert.Throws<DomainException>(action);
    }

    [Fact]
    public void Orcamento_ValidarAprovacao_DeveLancar_QuandoValorTotalZero()
    {
        var orcamento = CriarOrcamento(valor: 0m);

        var action = () => orcamento.ValidarAprovacao();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O orcamento precisa ter valor maior que zero para ser aprovado.", exception.Message);
    }

    [Fact]
    public void Orcamento_ConfirmarPagamento_DeveAlterarStatusParaAprovado()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();

        orcamento.ConfirmarPagamento();

        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_ConfirmarPagamento_DeveLancar_QuandoNaoAguardaPagamento()
    {
        var orcamento = CriarOrcamento();

        var action = () => orcamento.ConfirmarPagamento();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento aguardando pagamento pode ter o pagamento confirmado.", exception.Message);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DeveAlterarStatusParaRecusado()
    {
        var orcamento = CriarOrcamento();

        orcamento.Recusar();

        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DevePermitir_QuandoAguardandoPagamento()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();

        orcamento.Recusar();

        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DeveLancar_QuandoPagamentoJaConfirmado()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();
        orcamento.ConfirmarPagamento();

        var action = () => orcamento.Recusar();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado ou aguardando pagamento pode ser recusado.", exception.Message);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DeveLancar_QuandoJaFoiRecusado()
    {
        var orcamento = CriarOrcamento();
        orcamento.Recusar();

        var action = () => orcamento.Recusar();

        Assert.Throws<DomainException>(action);
    }

    [Theory]
    [InlineData(StatusOrcamento.Gerado, "Gerado")]
    [InlineData(StatusOrcamento.AguardandoPagamento, "Aguardando pagamento")]
    [InlineData(StatusOrcamento.Aprovado, "Aprovado")]
    [InlineData(StatusOrcamento.Recusado, "Recusado")]
    public void StatusOrcamentoExtensions_ObterDescricao_DeveRetornarDescricaoEsperada(StatusOrcamento status, string descricaoEsperada)
    {
        Assert.Equal(descricaoEsperada, status.ObterDescricao());
    }

    [Fact]
    public void StatusOrcamentoExtensions_ObterDescricao_DeveLancar_QuandoStatusInvalido()
    {
        var action = () => ((StatusOrcamento)99).ObterDescricao();

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }
}
