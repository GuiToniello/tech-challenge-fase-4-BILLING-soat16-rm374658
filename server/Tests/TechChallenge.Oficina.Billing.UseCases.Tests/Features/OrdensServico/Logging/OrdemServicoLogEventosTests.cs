using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.Billing.UseCases.Tests.Features.OrdensServico.Logging;

public sealed class OrdemServicoLogEventosTests
{
    [Theory]
    [InlineData(StatusOrdemServico.Recebida, "recebida")]
    [InlineData(StatusOrdemServico.EmDiagnostico, "em_diagnostico")]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, "aguardando_aprovacao")]
    [InlineData(StatusOrdemServico.EmExecucao, "em_execucao")]
    [InlineData(StatusOrdemServico.Finalizada, "finalizada")]
    [InlineData(StatusOrdemServico.Entregue, "entregue")]
    [InlineData(StatusOrdemServico.Encerrada, "encerrada")]
    [InlineData(StatusOrdemServico.Cancelada, "cancelada")]
    public void StatusParaLog_DeveRetornarValorDoContrato(StatusOrdemServico status, string valorEsperado)
    {
        Assert.Equal(valorEsperado, OrdemServicoLogEventos.StatusParaLog(status));
    }

    [Fact]
    public void StatusParaLog_DeveLancar_QuandoStatusInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrdemServicoLogEventos.StatusParaLog((StatusOrdemServico)99));
    }

    [Fact]
    public void Eventos_DevemManterNomesEIdsDoContrato()
    {
        Assert.Equal((1004, "os.orcamento_gerado"), (OrdemServicoLogEventos.OsOrcamentoGerado.Id, OrdemServicoLogEventos.OsOrcamentoGerado.Name));
        Assert.Equal((1005, "os.orcamento_enviado"), (OrdemServicoLogEventos.OsOrcamentoEnviado.Id, OrdemServicoLogEventos.OsOrcamentoEnviado.Name));
        Assert.Equal((2001, "os.falha"), (OrdemServicoLogEventos.OsFalha.Id, OrdemServicoLogEventos.OsFalha.Name));
        Assert.Equal((3001, "integracao.falha"), (OrdemServicoLogEventos.IntegracaoFalha.Id, OrdemServicoLogEventos.IntegracaoFalha.Name));
        Assert.Equal((4001, "evento.publicado"), (OrdemServicoLogEventos.EventoPublicado.Id, OrdemServicoLogEventos.EventoPublicado.Name));
        Assert.Equal((4002, "evento.consumido"), (OrdemServicoLogEventos.EventoConsumido.Id, OrdemServicoLogEventos.EventoConsumido.Name));
        Assert.Equal((4003, "evento.ignorado"), (OrdemServicoLogEventos.EventoIgnorado.Id, OrdemServicoLogEventos.EventoIgnorado.Name));
        Assert.Equal((5001, "pagamento.criado"), (OrdemServicoLogEventos.PagamentoCriado.Id, OrdemServicoLogEventos.PagamentoCriado.Name));
        Assert.Equal((5002, "pagamento.aprovado"), (OrdemServicoLogEventos.PagamentoAprovado.Id, OrdemServicoLogEventos.PagamentoAprovado.Name));
        Assert.Equal((5003, "pagamento.recusado"), (OrdemServicoLogEventos.PagamentoRecusado.Id, OrdemServicoLogEventos.PagamentoRecusado.Name));
        Assert.Equal((5004, "pagamento.simulado"), (OrdemServicoLogEventos.PagamentoSimulado.Id, OrdemServicoLogEventos.PagamentoSimulado.Name));
    }

    [Fact]
    public void Etapas_IntegracoesEOperacoes_DevemManterValoresDoContrato()
    {
        Assert.Equal(
            ["orcamento_geracao", "orcamento_envio", "orcamento_aprovacao", "orcamento_recusa", "pagamento_verificacao", "pagamento_notificacao"],
            new[]
            {
                OrdemServicoLogEventos.Etapas.OrcamentoGeracao, OrdemServicoLogEventos.Etapas.OrcamentoEnvio, OrdemServicoLogEventos.Etapas.OrcamentoAprovacao,
                OrdemServicoLogEventos.Etapas.OrcamentoRecusa, OrdemServicoLogEventos.Etapas.PagamentoVerificacao, OrdemServicoLogEventos.Etapas.PagamentoNotificacao
            });
        Assert.Equal(
            ["resend", "mercadopago", "sqs", "postgres"],
            new[] { OrdemServicoLogEventos.Integracoes.Resend, OrdemServicoLogEventos.Integracoes.MercadoPago, OrdemServicoLogEventos.Integracoes.Sqs, OrdemServicoLogEventos.Integracoes.Postgres });
        Assert.Equal(
            ["envio_orcamento", "criacao_cobranca", "consulta_pagamento", "publicacao", "consumo"],
            new[]
            {
                OrdemServicoLogEventos.Operacoes.EnvioOrcamento, OrdemServicoLogEventos.Operacoes.CriacaoCobranca, OrdemServicoLogEventos.Operacoes.ConsultaPagamento,
                OrdemServicoLogEventos.Operacoes.Publicacao, OrdemServicoLogEventos.Operacoes.Consumo
            });
    }
}
