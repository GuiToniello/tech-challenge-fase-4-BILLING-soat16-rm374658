using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;

namespace TechChallenge.Oficina.Billing.DB.Data.Features.OrdensServico;

// Snapshot local da OS (o dono e o OS Service, os dados vem da Management): sem chaves estrangeiras para clientes, veiculos e servicos de outros bancos.
public sealed class OrdemServicoReferenciaConfiguration : IEntityTypeConfiguration<OrdemServicoReferencia>
{
    private const string OrdemServicoIdColumnName = "ordem_servico_id";

    public void Configure(EntityTypeBuilder<OrdemServicoReferencia> builder)
    {
        builder.ToTable("ordens_servico_referencias");

        builder.HasKey(referencia => referencia.Id);

        builder.Property(referencia => referencia.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(referencia => referencia.ClienteId)
            .HasColumnName("cliente_id")
            .IsRequired();

        builder.Property(referencia => referencia.VeiculoId)
            .HasColumnName("veiculo_id")
            .IsRequired();

        builder.Property(referencia => referencia.ClienteEmail)
            .HasColumnName("cliente_email")
            .HasMaxLength(OrdemServicoReferencia.TamanhoMaximoEmail);

        builder.Property(referencia => referencia.EstoqueSuficiente)
            .HasColumnName("estoque_suficiente")
            .IsRequired();

        builder.Property(referencia => referencia.Status)
            .HasColumnName("status")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(referencia => referencia.ValorTotal)
            .HasColumnName("valor_total")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(referencia => referencia.PossuiDadosParaOrcamento)
            .HasColumnName("possui_dados")
            .IsRequired();

        builder.OwnsMany(referencia => referencia.Servicos, servicoBuilder =>
        {
            servicoBuilder.ToTable("ordens_servico_referencias_servicos");
            servicoBuilder.WithOwner().HasForeignKey(OrdemServicoIdColumnName);
            servicoBuilder.Property<int>("id").ValueGeneratedOnAdd();
            servicoBuilder.HasKey("id");

            servicoBuilder.Property(servico => servico.ServicoId)
                .HasColumnName("servico_id")
                .IsRequired();

            servicoBuilder.Property(servico => servico.Nome)
                .HasColumnName("nome")
                .HasMaxLength(ServicoOrdemServico.TamanhoMaximoNome)
                .IsRequired();

            servicoBuilder.Property(servico => servico.Valor)
                .HasColumnName("valor")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            servicoBuilder.HasIndex(OrdemServicoIdColumnName);
        });
    }
}
