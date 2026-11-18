using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.Eventos.UseCases;

public sealed class OrdemServicoEventosUseCasesTests
{
    private readonly Mock<IOrdemServicoReferenciaGateway> _referenciaGatewayMock = new();
    private readonly Mock<IOrcamentoGateway> _orcamentoGatewayMock = new();
    private readonly Guid _ordemServicoId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly Guid _veiculoId = Guid.NewGuid();

    private OrdemServicoEventosUseCases CriarUseCases() =>
        new(_referenciaGatewayMock.Object, _orcamentoGatewayMock.Object, NullLogger<OrdemServicoEventosUseCases>.Instance);

    private OrdemServicoValidada CriarValidada(params ServicoValidado[] servicos) =>
        new(_ordemServicoId, _clienteId, _veiculoId, "cliente@teste.com", true, servicos.Length > 0 ? servicos : [new ServicoValidado(Guid.NewGuid(), "Troca de oleo", 120m)], 120m);

    private OrdemServicoReferencia ConfigurarReferencia(OrdemServicoReferencia referencia)
    {
        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);
        return referencia;
    }

    private OrdemServicoReferencia CriarReferenciaCompleta(StatusOrdemServico status = StatusOrdemServico.Recebida)
    {
        var referencia = OrdemServicoReferencia.Criar(_ordemServicoId, _clienteId, _veiculoId, null, true, [ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca", 50m)], 50m);
        referencia.AtualizarStatus(status);
        return ConfigurarReferencia(referencia);
    }

    private void VerificarSemGravacao()
    {
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveCriarReferenciaComOSnapshot_QuandoAindaNaoExiste()
    {
        var servicoId = Guid.NewGuid();
        OrdemServicoReferencia? gravada = null;
        _referenciaGatewayMock
            .Setup(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()))
            .Callback<OrdemServicoReferencia, CancellationToken>((referencia, _) => gravada = referencia)
            .Returns(Task.CompletedTask);

        var resultado = await CriarUseCases().ProcessarValidadaAsync(CriarValidada(new ServicoValidado(servicoId, "Alinhamento", 89.9m)));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.NotNull(gravada);
        Assert.Equal(_ordemServicoId, gravada!.Id);
        Assert.Equal(_clienteId, gravada.ClienteId);
        Assert.Equal(_veiculoId, gravada.VeiculoId);
        Assert.Equal("cliente@teste.com", gravada.ClienteEmail);
        Assert.True(gravada.EstoqueSuficiente);
        Assert.True(gravada.PossuiDadosParaOrcamento);
        Assert.Equal(StatusOrdemServico.Recebida, gravada.Status);
        var servico = Assert.Single(gravada.Servicos);
        Assert.Equal(servicoId, servico.ServicoId);
        Assert.Equal("Alinhamento", servico.Nome);
        Assert.Equal(89.9m, servico.Valor);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveCompletarReferenciaParcialMantendoOStatus()
    {
        var parcial = ConfigurarReferencia(OrdemServicoReferencia.CriarParcial(_ordemServicoId, _clienteId, StatusOrdemServico.EmDiagnostico));

        var resultado = await CriarUseCases().ProcessarValidadaAsync(CriarValidada());

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, parcial.Status);
        Assert.True(parcial.PossuiDadosParaOrcamento);
        Assert.Equal(_veiculoId, parcial.VeiculoId);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(parcial, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveAtualizarSnapshot_QuandoOrdemFoiAtualizada()
    {
        var referencia = CriarReferenciaCompleta(StatusOrdemServico.Recebida);
        var novoServicoId = Guid.NewGuid();

        var resultado = await CriarUseCases().ProcessarValidadaAsync(CriarValidada(new ServicoValidado(novoServicoId, "Balanceamento", 70m)));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(novoServicoId, Assert.Single(referencia.Servicos).ServicoId);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveLancarSemGravar_QuandoServicoSemIdentificador()
    {
        var action = async () => await CriarUseCases().ProcessarValidadaAsync(CriarValidada(new ServicoValidado(Guid.Empty, "Troca", 10m)));

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarValidadaAsync_DeveLancarSemGravar_QuandoSemServicos()
    {
        var evento = new OrdemServicoValidada(_ordemServicoId, _clienteId, _veiculoId, null, true, null!, 0m);

        var action = async () => await CriarUseCases().ProcessarValidadaAsync(evento);

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico.", exception.Message);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveCriarReferenciaParcial_QuandoChegaAntesDaValidacao()
    {
        OrdemServicoReferencia? gravada = null;
        _referenciaGatewayMock
            .Setup(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()))
            .Callback<OrdemServicoReferencia, CancellationToken>((referencia, _) => gravada = referencia)
            .Returns(Task.CompletedTask);

        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 1, 2));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.NotNull(gravada);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, gravada!.Status);
        Assert.Equal(_clienteId, gravada.ClienteId);
        Assert.False(gravada.PossuiDadosParaOrcamento);
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveAtualizarStatus_QuandoReferenciaExiste()
    {
        var referencia = CriarReferenciaCompleta(StatusOrdemServico.AguardandoAprovacao);

        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 3, 4));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmExecucao, referencia.Status);
        Assert.True(referencia.PossuiDadosParaOrcamento);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveIgnorarSemGravar_QuandoStatusRepetido()
    {
        CriarReferenciaCompleta(StatusOrdemServico.EmDiagnostico);

        var resultado = await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 1, 2));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarStatusAlteradoAsync_DeveLancarSemGravar_QuandoStatusInvalido()
    {
        CriarReferenciaCompleta();

        var action = async () => await CriarUseCases().ProcessarStatusAlteradoAsync(new OrdemServicoStatusAlterado(_ordemServicoId, _clienteId, 1, 99));

        await Assert.ThrowsAsync<DomainException>(action);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveRemoverReferenciaEOrcamento()
    {
        var referencia = CriarReferenciaCompleta();
        var orcamento = Orcamento.Criar(_ordemServicoId, referencia.Servicos);
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(_ordemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);

        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveRemoverSoAReferencia_QuandoNaoHaOrcamento()
    {
        var referencia = CriarReferenciaCompleta();

        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarExcluidaAsync_DeveIgnorar_QuandoOrdemDesconhecida()
    {
        var resultado = await CriarUseCases().ProcessarExcluidaAsync(new OrdemServicoExcluida(_ordemServicoId));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        VerificarSemGravacao();
    }

    [Fact]
    public async Task ProcessarRejeitadaAsync_DeveRemoverReferencia()
    {
        var referencia = CriarReferenciaCompleta();

        var resultado = await CriarUseCases().ProcessarRejeitadaAsync(new OrdemServicoRejeitada(_ordemServicoId, "Cliente nao encontrado."));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarRejeitadaAsync_DeveIgnorar_QuandoOrdemDesconhecida()
    {
        var resultado = await CriarUseCases().ProcessarRejeitadaAsync(new OrdemServicoRejeitada(_ordemServicoId, "Cliente nao encontrado."));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        VerificarSemGravacao();
    }
}
