using Moq;
using Resend;
using TechChallenge.Oficina.Billing.Email.Configuration;
using TechChallenge.Oficina.Billing.Email.Features;
using TechChallenge.Oficina.Billing.Email.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Exceptions;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.OrdensServico.VOs;
using Xunit;

namespace TechChallenge.Oficina.Billing.Email.Tests.Features.Orcamentos;

public sealed class OrcamentoEmailSenderTests
{
    private readonly Mock<IResendClient> _resendClientMock = new();
    private readonly ResendSettings _settingsConfiguradas = new() { ApiKey = "api-key", FromEmail = "onboarding@resend.dev" };

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveMontarEmailEEnviar()
    {
        var orcamento = CriarOrcamento();
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, _settingsConfiguradas);
        EmailMessage? emailCapturado = null;
        _resendClientMock
            .Setup(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => emailCapturado = message)
            .Returns(Task.CompletedTask);

        await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(emailCapturado);
        Assert.Contains("onboarding@resend.dev", emailCapturado!.From.ToString());
        Assert.Contains("cliente@teste.com", emailCapturado.To);
        Assert.Contains(orcamento.OrdemServicoId.ToString(), emailCapturado.Subject);
        Assert.Contains("data:", emailCapturado.Subject);
        Assert.Contains(orcamento.OrdemServicoId.ToString(), emailCapturado.HtmlBody);
        Assert.Contains("Valor total", emailCapturado.HtmlBody);
        Assert.Contains("Status do orçamento:</strong> Gerado", emailCapturado.HtmlBody);
        Assert.Contains("Troca de oleo", emailCapturado.HtmlBody);
        Assert.DoesNotContain("Pagamento:", emailCapturado.HtmlBody);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveIncluirLinkDePagamento_QuandoInformado()
    {
        var orcamento = CriarOrcamento();
        orcamento.AprovarParaPagamento();
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, _settingsConfiguradas);
        EmailMessage? emailCapturado = null;
        _resendClientMock
            .Setup(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => emailCapturado = message)
            .Returns(Task.CompletedTask);

        await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", "https://checkout/pref?a=1&b=2");

        Assert.NotNull(emailCapturado);
        Assert.Contains("Status do orçamento:</strong> Aguardando pagamento", emailCapturado!.HtmlBody);
        Assert.Contains("<a href='https://checkout/pref?a=1&amp;b=2'>", emailCapturado.HtmlBody);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarQuandoOrcamentoNulo()
    {
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, _settingsConfiguradas);

        var action = async () => await sender.EnviarOrcamentoAsync(null!, "cliente@teste.com");

        await Assert.ThrowsAsync<ArgumentNullException>(action);
        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarDomainExceptionQuandoApiKeyNaoConfigurada()
    {
        var orcamento = CriarOrcamento();
        var settings = new ResendSettings { ApiKey = string.Empty, FromEmail = "onboarding@resend.dev" };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);

        var action = async () => await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Servico de email nao esta configurado. Verifique as configuracoes do Resend.", exception.Message);
        _resendClientMock.Verify(client => client.SendEmailAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarOrcamentoAsync_DeveLancarDomainExceptionQuandoFromEmailNaoConfigurado()
    {
        var orcamento = CriarOrcamento();
        var settings = new ResendSettings { ApiKey = "api-key", FromEmail = string.Empty };
        var sender = new OrcamentoEmailSender(_resendClientMock.Object, settings);

        var action = async () => await sender.EnviarOrcamentoAsync(orcamento, "cliente@teste.com");

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Servico de email nao esta configurado. Verifique as configuracoes do Resend.", exception.Message);
    }

    private static Orcamento CriarOrcamento()
    {
        var servico = ServicoOrdemServico.Criar(Guid.NewGuid(), "Troca de oleo", 50m);
        return Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2026, 06, 06, 10, 00, 00, DateTimeKind.Utc));
    }
}
