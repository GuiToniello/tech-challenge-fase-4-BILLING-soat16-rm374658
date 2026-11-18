namespace TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos
{
    public interface IPagamentosController
    {
        Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Verificar(Guid ordemServicoId, CancellationToken cancellationToken);

        // tipo e recursoId lidos da notificacao do Mercado Pago (type/topic e data.id/id)
        Task<object> ReceberNotificacao(string? tipo, string? recursoId, CancellationToken cancellationToken);
    }
}
