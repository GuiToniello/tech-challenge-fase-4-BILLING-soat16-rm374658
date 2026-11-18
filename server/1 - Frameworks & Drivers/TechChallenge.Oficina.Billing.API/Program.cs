using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TechChallenge.Oficina.Billing.Adapters.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Adapters.Features.Pagamentos;
using TechChallenge.Oficina.Billing.API.Extensions;
using TechChallenge.Oficina.Billing.API.Features.Orcamentos;
using TechChallenge.Oficina.Billing.API.Features.Pagamentos;
using TechChallenge.Oficina.Billing.API.Middleware;
using TechChallenge.Oficina.Billing.API.Settings;
using TechChallenge.Oficina.Billing.Controllers.Features.Eventos;
using TechChallenge.Oficina.Billing.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos;
using TechChallenge.Oficina.Billing.DB.Data;
using TechChallenge.Oficina.Billing.Email;
using TechChallenge.Oficina.Billing.Email.Configuration;
using TechChallenge.Oficina.Billing.Messaging;
using TechChallenge.Oficina.Billing.Messaging.Configuration;
using TechChallenge.Oficina.Billing.Pagamentos;
using TechChallenge.Oficina.Billing.Pagamentos.Configuration;
using TechChallenge.Oficina.Billing.UseCases.Features.Eventos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.UseCases;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddLoggingConfiguration(builder.Configuration);
builder.Services.AddSwaggerConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);

var databaseSettings = builder.Configuration
    .GetSection(DatabaseSettings.SectionName)
    .Get<DatabaseSettings>()
    ?? throw new InvalidOperationException("A configuração de banco de dados é obrigatória.");

if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
{
    throw new InvalidOperationException("A connection string do banco de dados é obrigatória.");
}

// Integracao com o OS Service e a Management por eventos (Amazon SQS); credenciais e regiao pela cadeia padrao do AWS SDK
var sqsSettings = builder.Configuration
    .GetSection(SqsSettings.SectionName)
    .Get<SqsSettings>()
    ?? new SqsSettings();

if (sqsSettings.FilasPublicacao.Count == 0 || sqsSettings.FilasPublicacao.Any(string.IsNullOrWhiteSpace))
{
    throw new InvalidOperationException("SqsSettings:FilasPublicacao é obrigatório (filas do OS Service e da Management).");
}

// Opcionais: sem chave do Resend o envio do orçamento responde 400; sem access token do Mercado Pago a cobrança é simulada
var resendSettings = builder.Configuration
    .GetSection(ResendSettings.SectionName)
    .Get<ResendSettings>()
    ?? new ResendSettings();

var mercadoPagoSettings = builder.Configuration
    .GetSection(MercadoPagoSettings.SectionName)
    .Get<MercadoPagoSettings>()
    ?? new MercadoPagoSettings();

builder.Services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);
builder.Services.AddScoped<IOrcamentoUseCasesFacade, OrcamentoUseCasesFacade>();
builder.Services.AddScoped<IOrcamentoUseCases, OrcamentoUseCases>();
builder.Services.AddScoped<IPagamentoUseCases, PagamentoUseCases>();
builder.Services.AddScoped<IOrdemServicoEventosUseCases, OrdemServicoEventosUseCases>();
builder.Services.AddInfraData(databaseSettings.ConnectionString);
builder.Services.AddInfraEmail(resendSettings);
builder.Services.AddInfraPagamentos(mercadoPagoSettings);
builder.Services.AddInfraMessaging(sqsSettings);

builder.Services.AddScoped<IOrcamentosController, OrcamentosController>();
builder.Services.AddScoped<IOrcamentosAdapter, OrcamentosAdapter>();
builder.Services.AddScoped<IPagamentosController, PagamentosController>();
builder.Services.AddScoped<IPagamentosAdapter, PagamentosAdapter>();
builder.Services.AddScoped<IEventosController, EventosController>();

var app = builder.Build();

try
{
    app.Services.ApplyMigrations();
}
catch (Exception ex)
{
    app.Logger.LogCritical(new EventId(9002, "app.migracao_falhou"), ex, "Falha ao aplicar as migrations do banco");
    Environment.ExitCode = 1;
    return;
}

app.UseLoggingConfiguration();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseSwaggerConfiguration();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapOrcamentoEndpoints();
app.MapPagamentoEndpoints();

await app.RunAsync();
