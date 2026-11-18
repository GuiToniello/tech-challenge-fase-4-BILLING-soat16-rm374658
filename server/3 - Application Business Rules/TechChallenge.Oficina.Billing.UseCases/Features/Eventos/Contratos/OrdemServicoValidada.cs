namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Consumido: a Management validou cliente, veiculo e servicos da OS (na abertura ou na atualizacao) e envia o snapshot para o orcamento.
// O Billing grava ou atualiza a referencia local mantendo o status ja conhecido.
public sealed record OrdemServicoValidada(
    Guid OrdemServicoId,
    Guid ClienteId,
    Guid VeiculoId,
    string? ClienteEmail,
    bool EstoqueSuficiente,
    IReadOnlyCollection<ServicoValidado> Servicos,
    decimal ValorTotal);
