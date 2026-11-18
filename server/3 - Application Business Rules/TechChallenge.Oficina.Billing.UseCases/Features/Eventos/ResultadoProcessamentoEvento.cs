namespace TechChallenge.Oficina.Billing.UseCases.Features.Eventos;

// Processado: o evento alterou a referencia local. Ignorado: evento repetido, OS desconhecida ou tipo nao tratado.
// Nos dois casos a mensagem pode sair da fila; so uma excecao faz o evento ser reprocessado.
public enum ResultadoProcessamentoEvento
{
    Processado,
    Ignorado
}
