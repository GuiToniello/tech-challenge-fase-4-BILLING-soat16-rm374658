using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;

// Este facade agrupa as integracoes de saida do fluxo de orcamento (email, eventos SQS, provedor de pagamento e historico de pagamentos) para reduzir a quantidade de dependencias injetadas no OrcamentoUseCases e atender a regra S107 do SonarQube sem alterar o padrao arquitetural atual.
public sealed class OrcamentoUseCasesFacade : IOrcamentoUseCasesFacade
{
    public OrcamentoUseCasesFacade(
        IOrcamentoEmailSender orcamentoEmailSender,
        IEventosPublisher eventosPublisher,
        IGatewayPagamento gatewayPagamento,
        IHistoricoPagamentoGateway historicoPagamentoGateway)
    {
        OrcamentoEmailSender = orcamentoEmailSender;
        EventosPublisher = eventosPublisher;
        GatewayPagamento = gatewayPagamento;
        HistoricoPagamentoGateway = historicoPagamentoGateway;
    }

    public IOrcamentoEmailSender OrcamentoEmailSender { get; }

    public IEventosPublisher EventosPublisher { get; }

    public IGatewayPagamento GatewayPagamento { get; }

    public IHistoricoPagamentoGateway HistoricoPagamentoGateway { get; }
}
