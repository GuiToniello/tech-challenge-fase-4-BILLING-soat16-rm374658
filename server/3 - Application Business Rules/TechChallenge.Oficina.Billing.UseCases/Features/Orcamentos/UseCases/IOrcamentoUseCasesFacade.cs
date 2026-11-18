using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Interfaces;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoUseCasesFacade
{
    IOrcamentoEmailSender OrcamentoEmailSender { get; }
    IEventosPublisher EventosPublisher { get; }
    IGatewayPagamento GatewayPagamento { get; }
}
