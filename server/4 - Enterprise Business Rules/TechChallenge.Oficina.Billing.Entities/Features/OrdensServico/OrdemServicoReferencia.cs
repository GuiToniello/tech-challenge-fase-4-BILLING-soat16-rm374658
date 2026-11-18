using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;

namespace TechChallenge.Oficina.Billing.Entities.Features.OrdensServico;

// Snapshot local (read model) da ordem de servico, mantido pelos eventos recebidos via SQS:
// os dados (cliente, veiculo, email, estoque e servicos com valor) vem do OrdemServicoValidada (Management)
// e o status vem do OrdemServicoStatusAlterado (OS Service). Nao ha ordem garantida entre os dois publicadores.
public sealed class OrdemServicoReferencia
{
    public const int TamanhoMaximoEmail = 254;

    private readonly List<ServicoOrdemServico> _servicos = [];

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public string? ClienteEmail { get; private set; }
    public bool EstoqueSuficiente { get; private set; }
    public StatusOrdemServico Status { get; private set; }
    public IReadOnlyCollection<ServicoOrdemServico> Servicos => _servicos.AsReadOnly();
    public decimal ValorTotal { get; private set; }

    // Falso na referencia parcial (criada pelo status antes do OrdemServicoValidada): ainda nao ha servicos para orcar
    public bool PossuiDadosParaOrcamento { get; private set; }

    private OrdemServicoReferencia()
    {
    }

    private OrdemServicoReferencia(Guid ordemServicoId, Guid clienteId, StatusOrdemServico status)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("O identificador da ordem de servico e obrigatorio.");
        }

        Id = ordemServicoId;
        ClienteId = clienteId;
        AtualizarStatus(status);
    }

    // Snapshot completo (OrdemServicoValidada): sem status conhecido ainda, a OS e considerada recebida
    public static OrdemServicoReferencia Criar(
        Guid ordemServicoId,
        Guid clienteId,
        Guid veiculoId,
        string? clienteEmail,
        bool estoqueSuficiente,
        IReadOnlyCollection<ServicoOrdemServico> servicos,
        decimal valorTotal)
    {
        var referencia = new OrdemServicoReferencia(ordemServicoId, clienteId, StatusOrdemServico.Recebida);
        referencia.AtualizarDados(clienteId, veiculoId, clienteEmail, estoqueSuficiente, servicos, valorTotal);
        return referencia;
    }

    // Referencia parcial: o OrdemServicoStatusAlterado chegou antes do OrdemServicoValidada; os dados chegam depois
    public static OrdemServicoReferencia CriarParcial(Guid ordemServicoId, Guid clienteId, StatusOrdemServico status)
    {
        return new OrdemServicoReferencia(ordemServicoId, clienteId, status);
    }

    // Mantem o status atual: ele pertence ao OS Service e pode ter chegado antes dos dados
    public void AtualizarDados(
        Guid clienteId,
        Guid veiculoId,
        string? clienteEmail,
        bool estoqueSuficiente,
        IReadOnlyCollection<ServicoOrdemServico> servicos,
        decimal valorTotal)
    {
        if (clienteId == Guid.Empty)
        {
            throw new DomainException("O cliente da ordem de servico e obrigatorio.");
        }

        if (veiculoId == Guid.Empty)
        {
            throw new DomainException("O veiculo da ordem de servico e obrigatorio.");
        }

        if (servicos is null || servicos.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico.");
        }

        if (servicos.Any(servico => servico is null))
        {
            throw new DomainException("Todos os servicos informados devem ser validos.");
        }

        if (valorTotal < 0)
        {
            throw new DomainException("O valor total da ordem de servico nao pode ser negativo.");
        }

        var email = string.IsNullOrWhiteSpace(clienteEmail) ? null : clienteEmail!.Trim();

        if (email is not null && email.Length > TamanhoMaximoEmail)
        {
            throw new DomainException($"O email do cliente deve ter no maximo {TamanhoMaximoEmail} caracteres.");
        }

        ClienteId = clienteId;
        VeiculoId = veiculoId;
        ClienteEmail = email;
        EstoqueSuficiente = estoqueSuficiente;
        ValorTotal = decimal.Round(valorTotal, 2, MidpointRounding.ToEven);

        var servicosIdsAdicionados = new HashSet<Guid>();
        _servicos.Clear();
        _servicos.AddRange(servicos.Where(servico => servicosIdsAdicionados.Add(servico.ServicoId)));

        PossuiDadosParaOrcamento = true;
    }

    public void AtualizarStatus(StatusOrdemServico status)
    {
        if (!Enum.IsDefined(typeof(StatusOrdemServico), status))
        {
            throw new DomainException("O status da ordem de servico informado e invalido.");
        }

        Status = status;
    }

    public void ValidarGeracaoOrcamento()
    {
        ValidarStatusPermiteOrcamento("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento gerado.");

        if (!PossuiDadosParaOrcamento)
        {
            throw new DomainException("A ordem de servico ainda nao foi validada pela Management. Tente novamente em instantes.");
        }

        if (!EstoqueSuficiente)
        {
            throw new DomainException("Estoque insuficiente para gerar orcamento.");
        }
    }

    public void ValidarAprovacaoOrcamento()
    {
        ValidarStatusPermiteOrcamento("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento aprovado.");
    }

    public void ValidarRecusaOrcamento()
    {
        ValidarStatusPermiteOrcamento("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento recusado.");
    }

    private void ValidarStatusPermiteOrcamento(string mensagem)
    {
        if (Status != StatusOrdemServico.Recebida && Status != StatusOrdemServico.EmDiagnostico && Status != StatusOrdemServico.AguardandoAprovacao)
        {
            throw new DomainException(mensagem);
        }
    }
}
