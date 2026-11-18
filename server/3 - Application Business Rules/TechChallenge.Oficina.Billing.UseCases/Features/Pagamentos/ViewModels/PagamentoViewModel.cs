namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

public sealed class PagamentoViewModel
{
    public Guid Id { get; set; }
    public Guid OrdemServicoId { get; set; }
    public decimal Valor { get; set; }
    public int Status { get; set; }
    public string StatusDescricao { get; set; } = string.Empty;
    public string Provedor { get; set; } = string.Empty;
    public string PreferenciaId { get; set; } = string.Empty;
    public string? PagamentoExternoId { get; set; }
    public string LinkPagamento { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
}
