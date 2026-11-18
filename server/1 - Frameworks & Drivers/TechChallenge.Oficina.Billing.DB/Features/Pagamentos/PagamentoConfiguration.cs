using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;

namespace TechChallenge.Oficina.Billing.DB.Data.Features.Pagamentos;

public sealed class PagamentoConfiguration : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> builder)
    {
        builder.ToTable("pagamentos");

        builder.HasKey(pagamento => pagamento.Id);

        builder.Property(pagamento => pagamento.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // Sem chave estrangeira: o pagamento fica como registro financeiro mesmo depois da exclusao da OS e do orcamento
        builder.Property(pagamento => pagamento.OrdemServicoId)
            .HasColumnName("ordem_servico_id")
            .IsRequired();

        builder.Property(pagamento => pagamento.Valor)
            .HasColumnName("valor")
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // Token de concorrencia: duas notificacoes simultaneas do mesmo pagamento nao aprovam (nem publicam o PagamentoAprovado) duas vezes
        builder.Property(pagamento => pagamento.Status)
            .HasColumnName("status")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(pagamento => pagamento.Provedor)
            .HasColumnName("provedor")
            .HasColumnType("integer")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(pagamento => pagamento.PreferenciaId)
            .HasColumnName("preferencia_id")
            .HasMaxLength(Pagamento.TamanhoMaximoIdentificador)
            .IsRequired();

        builder.Property(pagamento => pagamento.PagamentoExternoId)
            .HasColumnName("pagamento_externo_id")
            .HasMaxLength(Pagamento.TamanhoMaximoIdentificador);

        builder.Property(pagamento => pagamento.LinkPagamento)
            .HasColumnName("link_pagamento")
            .HasMaxLength(Pagamento.TamanhoMaximoLink)
            .IsRequired();

        builder.Property(pagamento => pagamento.CriadoEm)
            .HasColumnName("criado_em")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(pagamento => pagamento.AtualizadoEm)
            .HasColumnName("atualizado_em")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(pagamento => pagamento.OrdemServicoId);
    }
}
