namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

public sealed class EventoHistoricoPagamentoViewModel
{
    public Guid OrdemServicoId { get; set; }
    public DateTime OcorridoEm { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Origem { get; set; } = string.Empty;
    public Guid PagamentoId { get; set; }
    public string Provedor { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string? PagamentoExternoId { get; set; }
    public string? PreferenciaId { get; set; }
}
