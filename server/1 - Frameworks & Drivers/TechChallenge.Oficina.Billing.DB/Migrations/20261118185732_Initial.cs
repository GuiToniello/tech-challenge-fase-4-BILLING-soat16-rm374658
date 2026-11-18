using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TechChallenge.Oficina.Billing.DB.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orcamentos",
                columns: table => new
                {
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_geracao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orcamentos", x => x.ordem_servico_id);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_referencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    veiculo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    estoque_suficiente = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    possui_dados = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_referencias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pagamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provedor = table.Column<int>(type: "integer", nullable: false),
                    preferencia_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pagamento_externo_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    link_pagamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagamentos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orcamentos_servicos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_servico = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orcamentos_servicos", x => x.id);
                    table.ForeignKey(
                        name: "FK_orcamentos_servicos_orcamentos_ordem_servico_id",
                        column: x => x.ordem_servico_id,
                        principalTable: "orcamentos",
                        principalColumn: "ordem_servico_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordens_servico_referencias_servicos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ordem_servico_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico_referencias_servicos", x => x.id);
                    table.ForeignKey(
                        name: "FK_ordens_servico_referencias_servicos_ordens_servico_referenc~",
                        column: x => x.ordem_servico_id,
                        principalTable: "ordens_servico_referencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orcamentos_servicos_ordem_servico_id",
                table: "orcamentos_servicos",
                column: "ordem_servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_referencias_servicos_ordem_servico_id",
                table: "ordens_servico_referencias_servicos",
                column: "ordem_servico_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagamentos_ordem_servico_id",
                table: "pagamentos",
                column: "ordem_servico_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orcamentos_servicos");

            migrationBuilder.DropTable(
                name: "ordens_servico_referencias_servicos");

            migrationBuilder.DropTable(
                name: "pagamentos");

            migrationBuilder.DropTable(
                name: "orcamentos");

            migrationBuilder.DropTable(
                name: "ordens_servico_referencias");
        }
    }
}
