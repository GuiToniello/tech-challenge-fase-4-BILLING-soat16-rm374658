using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

namespace TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;

public sealed class PagamentoGateway : IPagamentoGateway
{
    private readonly BillingDbContext _context;

    public PagamentoGateway(BillingDbContext context)
    {
        _context = context;
    }

    public async Task AdicionarAsync(Pagamento pagamento, CancellationToken cancellationToken = default)
    {
        await _context.Pagamentos.AddAsync(pagamento, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(Pagamento pagamento, CancellationToken cancellationToken = default)
    {
        _context.Pagamentos.Update(pagamento);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Pagamento?> ObterUltimoPorOrdemServicoIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        return await _context.Pagamentos
            .Where(pagamento => pagamento.OrdemServicoId == ordemServicoId)
            .OrderByDescending(pagamento => pagamento.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
