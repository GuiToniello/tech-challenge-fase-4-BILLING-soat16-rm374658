using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;

namespace TechChallenge.Oficina.Billing.DB.Data.Context;

public sealed class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<OrdemServicoReferencia> OrdensServicoReferencias => Set<OrdemServicoReferencia>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
