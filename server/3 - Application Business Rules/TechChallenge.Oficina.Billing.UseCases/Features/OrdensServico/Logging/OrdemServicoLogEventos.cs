using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.Logging;

// Catálogo dos eventos de log de negócio do Billing. Nomes e valores são o contrato com as métricas e monitores do Datadog: não renomear sem atualizar os dois lados.
// Os eventos do orçamento (os.orcamento_*) mantêm os nomes de quando o orçamento era da Management; os de pagamento (pagamento.*) são do Billing.
public static class OrdemServicoLogEventos
{
    public static readonly EventId OsOrcamentoGerado = new(1004, "os.orcamento_gerado");
    public static readonly EventId OsOrcamentoEnviado = new(1005, "os.orcamento_enviado");
    public static readonly EventId OsFalha = new(2001, "os.falha");
    public static readonly EventId IntegracaoFalha = new(3001, "integracao.falha");
    public static readonly EventId EventoPublicado = new(4001, "evento.publicado");
    public static readonly EventId EventoConsumido = new(4002, "evento.consumido");
    public static readonly EventId EventoIgnorado = new(4003, "evento.ignorado");
    public static readonly EventId PagamentoCriado = new(5001, "pagamento.criado");
    public static readonly EventId PagamentoAprovado = new(5002, "pagamento.aprovado");
    public static readonly EventId PagamentoRecusado = new(5003, "pagamento.recusado");
    public static readonly EventId PagamentoSimulado = new(5004, "pagamento.simulado");

    public static class Etapas
    {
        public const string OrcamentoGeracao = "orcamento_geracao";
        public const string OrcamentoEnvio = "orcamento_envio";
        public const string OrcamentoAprovacao = "orcamento_aprovacao";
        public const string OrcamentoRecusa = "orcamento_recusa";
        public const string PagamentoVerificacao = "pagamento_verificacao";
        public const string PagamentoNotificacao = "pagamento_notificacao";
    }

    public static class TiposFalha
    {
        public const string RegraNegocio = "regra_negocio";
        public const string NaoEncontrado = "nao_encontrado";
    }

    public static class Integracoes
    {
        public const string Resend = "resend";
        public const string MercadoPago = "mercadopago";
        public const string Sqs = "sqs";
        public const string Postgres = "postgres";
        public const string DynamoDb = "dynamodb";
    }

    public static class Operacoes
    {
        public const string EnvioOrcamento = "envio_orcamento";
        public const string CriacaoCobranca = "criacao_cobranca";
        public const string ConsultaPagamento = "consulta_pagamento";
        public const string Publicacao = "publicacao";
        public const string Consumo = "consumo";
        public const string RegistroHistorico = "registro_historico";
        public const string ConsultaHistorico = "consulta_historico";
    }

    public static string StatusParaLog(StatusOrdemServico status)
    {
        return status switch
        {
            StatusOrdemServico.Recebida => "recebida",
            StatusOrdemServico.EmDiagnostico => "em_diagnostico",
            StatusOrdemServico.AguardandoAprovacao => "aguardando_aprovacao",
            StatusOrdemServico.EmExecucao => "em_execucao",
            StatusOrdemServico.Finalizada => "finalizada",
            StatusOrdemServico.Entregue => "entregue",
            StatusOrdemServico.Encerrada => "encerrada",
            StatusOrdemServico.Cancelada => "cancelada",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status da ordem de servico invalido.")
        };
    }
}
