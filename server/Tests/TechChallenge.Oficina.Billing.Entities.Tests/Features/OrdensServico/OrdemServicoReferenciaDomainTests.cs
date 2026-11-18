using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using Xunit;

namespace TechChallenge.Oficina.Billing.Entities.Tests.Features.OrdensServico;

public sealed class OrdemServicoReferenciaDomainTests
{
    private static ServicoOrdemServico CriarServico(decimal valor = 120m, Guid? servicoId = null)
        => ServicoOrdemServico.Criar(servicoId ?? Guid.NewGuid(), "Troca de oleo", valor);

    private static OrdemServicoReferencia CriarReferencia(bool estoqueSuficiente = true, StatusOrdemServico? status = null)
    {
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cliente@teste.com", estoqueSuficiente, [CriarServico()], 120m);

        if (status is not null)
        {
            referencia.AtualizarStatus(status.Value);
        }

        return referencia;
    }

    [Fact]
    public void Criar_DeveGuardarSnapshotComoRecebidaEComDadosParaOrcamento()
    {
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servico = CriarServico(89.9m);

        var referencia = OrdemServicoReferencia.Criar(ordemServicoId, clienteId, veiculoId, " cliente@teste.com ", true, [servico], 89.9m);

        Assert.Equal(ordemServicoId, referencia.Id);
        Assert.Equal(clienteId, referencia.ClienteId);
        Assert.Equal(veiculoId, referencia.VeiculoId);
        Assert.Equal("cliente@teste.com", referencia.ClienteEmail);
        Assert.True(referencia.EstoqueSuficiente);
        Assert.Equal(StatusOrdemServico.Recebida, referencia.Status);
        Assert.Equal(89.9m, referencia.ValorTotal);
        Assert.True(referencia.PossuiDadosParaOrcamento);
        Assert.Same(servico, Assert.Single(referencia.Servicos));
    }

    [Fact]
    public void Criar_DeveIgnorarServicosDuplicadosEGuardarEmailVazioComoNulo()
    {
        var servico = CriarServico();

        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", false, [servico, servico], 120m);

        Assert.Single(referencia.Servicos);
        Assert.Null(referencia.ClienteEmail);
        Assert.False(referencia.EstoqueSuficiente);
    }

    [Fact]
    public void Criar_DeveLancar_QuandoOrdemServicoIdVazio()
    {
        var action = () => OrdemServicoReferencia.Criar(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), null, true, [CriarServico()], 120m);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O identificador da ordem de servico e obrigatorio.", exception.Message);
    }

    [Theory]
    [InlineData(true, false, 1, 10, "O cliente da ordem de servico e obrigatorio.")]
    [InlineData(false, true, 1, 10, "O veiculo da ordem de servico e obrigatorio.")]
    [InlineData(false, false, 0, 10, "A ordem de servico deve possuir ao menos um servico.")]
    [InlineData(false, false, 1, -1, "O valor total da ordem de servico nao pode ser negativo.")]
    public void AtualizarDados_DeveLancarSemAlterar_QuandoDadosInvalidos(bool clienteVazio, bool veiculoVazio, int quantidadeServicos, decimal valorTotal, string mensagem)
    {
        var referencia = CriarReferencia();
        var clienteOriginal = referencia.ClienteId;
        var servicos = Enumerable.Range(0, quantidadeServicos).Select(_ => CriarServico()).ToArray();

        var action = () => referencia.AtualizarDados(
            clienteVazio ? Guid.Empty : Guid.NewGuid(),
            veiculoVazio ? Guid.Empty : Guid.NewGuid(),
            null,
            true,
            servicos,
            valorTotal);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(mensagem, exception.Message);
        Assert.Equal(clienteOriginal, referencia.ClienteId);
    }

    [Fact]
    public void CriarParcial_DeveGuardarStatusSemDadosParaOrcamento()
    {
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();

        var referencia = OrdemServicoReferencia.CriarParcial(ordemServicoId, clienteId, StatusOrdemServico.EmDiagnostico);

        Assert.Equal(ordemServicoId, referencia.Id);
        Assert.Equal(clienteId, referencia.ClienteId);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        Assert.False(referencia.PossuiDadosParaOrcamento);
        Assert.Empty(referencia.Servicos);
    }

    [Fact]
    public void AtualizarDados_DeveManterStatusConhecidoEHabilitarOrcamento()
    {
        var referencia = OrdemServicoReferencia.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico);
        var veiculoId = Guid.NewGuid();

        referencia.AtualizarDados(Guid.NewGuid(), veiculoId, "cliente@teste.com", true, [CriarServico(200m)], 200m);

        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        Assert.True(referencia.PossuiDadosParaOrcamento);
        Assert.Equal(veiculoId, referencia.VeiculoId);
        Assert.Equal(200m, referencia.ValorTotal);
    }

    [Fact]
    public void AtualizarStatus_DeveLancar_QuandoStatusInvalido()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.AtualizarStatus((StatusOrdemServico)99);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O status da ordem de servico informado e invalido.", exception.Message);
        Assert.Equal(StatusOrdemServico.Recebida, referencia.Status);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida)]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    public void ValidarGeracaoOrcamento_DevePermitir_QuandoStatusAceitaOrcamento(StatusOrdemServico status)
    {
        var referencia = CriarReferencia(status: status);

        referencia.ValidarGeracaoOrcamento();
        referencia.ValidarAprovacaoOrcamento();
        referencia.ValidarRecusaOrcamento();

        Assert.Equal(status, referencia.Status);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void ValidarGeracaoOrcamento_DeveLancar_QuandoStatusNaoAceitaOrcamento(StatusOrdemServico status)
    {
        var referencia = CriarReferencia(status: status);

        var exception = Assert.Throws<DomainException>(referencia.ValidarGeracaoOrcamento);

        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento gerado.", exception.Message);
    }

    [Fact]
    public void ValidarGeracaoOrcamento_DeveLancar_QuandoReferenciaParcial()
    {
        var referencia = OrdemServicoReferencia.CriarParcial(Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.Recebida);

        var exception = Assert.Throws<DomainException>(referencia.ValidarGeracaoOrcamento);

        Assert.Equal("A ordem de servico ainda nao foi validada pela Management. Tente novamente em instantes.", exception.Message);
    }

    [Fact]
    public void ValidarGeracaoOrcamento_DeveLancar_QuandoEstoqueInsuficiente()
    {
        var referencia = CriarReferencia(estoqueSuficiente: false);

        var exception = Assert.Throws<DomainException>(referencia.ValidarGeracaoOrcamento);

        Assert.Equal("Estoque insuficiente para gerar orcamento.", exception.Message);
    }

    [Fact]
    public void ValidarAprovacaoOrcamento_DeveLancar_QuandoOrdemEmExecucao()
    {
        var referencia = CriarReferencia(status: StatusOrdemServico.EmExecucao);

        var exception = Assert.Throws<DomainException>(referencia.ValidarAprovacaoOrcamento);

        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento aprovado.", exception.Message);
    }

    [Fact]
    public void ValidarRecusaOrcamento_DeveLancar_QuandoOrdemFinalizada()
    {
        var referencia = CriarReferencia(status: StatusOrdemServico.Finalizada);

        var exception = Assert.Throws<DomainException>(referencia.ValidarRecusaOrcamento);

        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento recusado.", exception.Message);
    }

    [Fact]
    public void ServicoOrdemServico_Criar_DeveNormalizarNomeEArredondarValor()
    {
        var servicoId = Guid.NewGuid();

        var servico = ServicoOrdemServico.Criar(servicoId, "  Alinhamento  ", 10.005m);

        Assert.Equal(servicoId, servico.ServicoId);
        Assert.Equal("Alinhamento", servico.Nome);
        Assert.Equal(10.00m, servico.Valor);
    }

    [Theory]
    [InlineData(true, "Troca", 10, "Todos os servicos informados devem possuir identificador valido.")]
    [InlineData(false, " ", 10, "O nome do servico e obrigatorio.")]
    [InlineData(false, "Troca", -0.01, "O valor do servico nao pode ser negativo.")]
    public void ServicoOrdemServico_Criar_DeveLancar_QuandoDadosInvalidos(bool idVazio, string nome, double valor, string mensagem)
    {
        var action = () => ServicoOrdemServico.Criar(idVazio ? Guid.Empty : Guid.NewGuid(), nome, (decimal)valor);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(mensagem, exception.Message);
    }

    [Fact]
    public void ServicoOrdemServico_Criar_DeveLancar_QuandoNomeMaiorQueOLimite()
    {
        var action = () => ServicoOrdemServico.Criar(Guid.NewGuid(), new string('a', ServicoOrdemServico.TamanhoMaximoNome + 1), 10m);

        Assert.Throws<DomainException>(action);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida, "Recebida")]
    [InlineData(StatusOrdemServico.EmDiagnostico, "Em diagnóstico")]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, "Aguardando aprovação")]
    [InlineData(StatusOrdemServico.EmExecucao, "Em execução")]
    [InlineData(StatusOrdemServico.Finalizada, "Finalizada")]
    [InlineData(StatusOrdemServico.Entregue, "Entregue")]
    [InlineData(StatusOrdemServico.Encerrada, "Encerrada")]
    [InlineData(StatusOrdemServico.Cancelada, "Cancelada")]
    public void StatusOrdemServicoExtensions_ObterDescricao_DeveRetornarDescricaoEsperada(StatusOrdemServico status, string descricao)
    {
        Assert.Equal(descricao, status.ObterDescricao());
    }

    [Fact]
    public void StatusOrdemServico_DeveManterValoresDoContratoDosEventos()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8],
            new[]
            {
                StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao,
                StatusOrdemServico.Finalizada, StatusOrdemServico.Entregue, StatusOrdemServico.Encerrada, StatusOrdemServico.Cancelada
            }.Select(status => (int)status));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((StatusOrdemServico)99).ObterDescricao());
    }
}
