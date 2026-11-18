namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos.Contratos;

// Item de servicos do OrdemServicoValidada: {servicoId, nome, valor}.
public sealed record ServicoValidado(Guid ServicoId, string Nome, decimal Valor);
