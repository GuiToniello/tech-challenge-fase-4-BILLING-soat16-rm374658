using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.DB.Data.Context;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.UseCases.Features.OrdensServico.UseCases;

namespace TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;

public sealed class OrdemServicoReferenciaGateway : IOrdemServicoReferenciaGateway
{
    private readonly BillingDbContext _context;

    public OrdemServicoReferenciaGateway(BillingDbContext context)
    {
        _context = context;
    }

    public async Task AdicionarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        await _context.OrdensServicoReferencias.AddAsync(referencia, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        _context.OrdensServicoReferencias.Update(referencia);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrdemServicoReferencia?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default)
    {
        // Os servicos do snapshot sao entidades owned: o EF Core ja os carrega junto com a referencia
        return await _context.OrdensServicoReferencias
            .FirstOrDefaultAsync(referencia => referencia.Id == ordemServicoId, cancellationToken);
    }

    public async Task RemoverAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default)
    {
        _context.OrdensServicoReferencias.Remove(referencia);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
