# Observabilidade (Billing)

O Billing segue o mesmo contrato de logs da Management API e do OS Service. A fonte da verdade (formato do JSON, chaves, regras, valores permitidos e o mapa evento → métrica → dashboard) é o [docs/observabilidade.md do repo MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/observabilidade.md). O Datadog (Agent, métricas de log, monitores, synthetics e dashboard) é criado pelo Terraform do MANAGEMENT, em `kubernetes/infra/addons` ([DATADOG.md](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/kubernetes/infra/DATADOG.md)): este repositório não tem nada do Datadog para configurar.

Aqui ficam só as particularidades deste serviço. **Ao mudar um nome ou valor de evento**, mude também o catálogo da Management, os documentos e, se a mudança afetar métricas, monitores ou o dashboard, o Terraform do MANAGEMENT.

## 1. Serviço

| Projeto | `service` | Namespace | Prefixo no API Gateway |
|---|---|---|---|
| `TechChallenge.Oficina.Billing.API` | `billing-api` | `billing` | `/billing` |

- As tags `service` e `env` (`study`) vêm das labels `tags.datadoghq.com/service` e `tags.datadoghq.com/env` do [Deployment](../kubernetes/k8s/features/billing-api/deployment.yml).
- O Datadog Agent coleta o stdout dos pods. A Single Step Instrumentation injeta o tracer .NET (v3) nos pods do namespace `billing`, pelo admission controller, sem mudança no código nem no Dockerfile.
- O Serilog é configurado em [LoggingExtension.cs](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Billing.API/Extensions/LoggingExtension.cs), igual ao da Management: 1 JSON por linha no stdout, com `timestamp`, `level`, `message`, `evento`, `logger.name`, `dd.*`, `error.*` e as propriedades dos eventos. As requisições a `/health*` não geram log.

## 2. Eventos emitidos

O catálogo fica em [OrdemServicoLogEventos.cs](../server/3%20-%20Application%20Business%20Rules/TechChallenge.Oficina.Billing.UseCases/Features/OrdensServico/Logging/OrdemServicoLogEventos.cs). Os eventos do orçamento mantêm os nomes e os `EventId` de quando o orçamento era da Management; os de pagamento (`pagamento.*`, 5001 a 5004) são novos.

| Evento (`EventId`) | Nível | Onde é emitido | Propriedades |
|---|---|---|---|
| `os.orcamento_gerado` (1004) | Information | `OrcamentoUseCases.GerarAsync`, depois de gravar e publicar `OrcamentoGerado` | `OrdemServicoId`, `ValorTotal` (número), `Regerado` (`true` quando o orçamento já existia) |
| `os.orcamento_enviado` (1005) | Information | `OrcamentoUseCases.EnviarPorEmailAsync`, depois do envio pelo Resend | `OrdemServicoId`, `ValorTotal` (número) |
| `os.falha` (2001) | Warning | `OrcamentosController` (gerar, enviar, aprovar e recusar) e `PagamentosController` (verificação e notificação), quando a ação é rejeitada por regra de negócio ou não encontra o recurso | `Etapa`, `OrdemServicoId` (na notificação, `PagamentoExternoId`), `TipoFalha`, `Motivo` |
| `integracao.falha` (3001) | Error | `OrcamentoUseCases.EnviarPorEmailAsync` (Resend), `GatewayPagamentoMercadoPago` (Mercado Pago) e `EventosPublisher` (SQS, por fila): registram e relançam, e a rota responde 500 (a notificação continua 200). Na publicação, a alteração já gravada continua. `SqsConsumidorHostedService`, ao receber ou processar uma mensagem: ela volta para a fila. Ao obter a URL da fila de consumo, sai como `Warning`, com nova tentativa a cada 5 s | `Integracao`, `Operacao`, `error.*` e, conforme o caso, `OrdemServicoId`, `PagamentoExternoId`, `Tipo`, `EventoId`, `MensagemId` e `Fila` |
| `evento.publicado` (4001) | Information | `EventosPublisher`, depois de enviar o evento a cada fila: um log por fila (OS Service e Management) | `Tipo`, `EventoId`, `Fila`, `OrdemServicoId` |
| `evento.consumido` (4002) | Information | `SqsConsumidorHostedService`, evento processado | `Tipo`, `EventoId`, `Fila` |
| `evento.ignorado` (4003) | Warning | `SqsConsumidorHostedService`: status repetido, OS desconhecida (`OrdemServicoExcluida` ou `OrdemServicoRejeitada`) ou tipo que este serviço não trata (como `OrdemServicoAberta`). A mensagem é apagada. Antes dele, um log `Information` sem `EventId` explica o motivo, quando o evento é de uma OS | `Tipo`, `EventoId`, `Fila` |
| `pagamento.criado` (5001) | Information | `OrcamentoUseCases.AprovarAsync`, depois de gravar uma cobrança nova (a aprovação repetida, que devolve a cobrança pendente, não gera) | `PagamentoId`, `Provedor`, `OrdemServicoId`, `Valor` |
| `pagamento.aprovado` (5002) | Information | `PagamentoUseCases`, depois de gravar a aprovação (verificação ou webhook) e antes de publicar `PagamentoAprovado` | `PagamentoId`, `OrdemServicoId`, `Provedor`, `PagamentoExternoId`, `Valor` |
| `pagamento.recusado` (5003) | Warning | `PagamentoUseCases`, depois de gravar a recusa | `PagamentoId`, `OrdemServicoId`, `Provedor`, `PagamentoExternoId` |
| `pagamento.simulado` (5004) | Warning | `GatewayPagamentoSimulado`, em cada cobrança e consulta do modo simulado (sem access token) | `OrdemServicoId` e, na cobrança, `PreferenciaId` e `Valor`; na consulta, `PagamentoExternoId` |
| `http.erro_nao_tratado` (9001) | Error | [GlobalExceptionHandlerMiddleware](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Billing.API/Middleware/GlobalExceptionHandlerMiddleware.cs) | `RequestMethod`, `RouteTemplate`, `Integracao` = `postgres` só com o banco indisponível, `error.*` |
| `app.migracao_falhou` (9002) | Critical (`Fatal` no JSON) | [Program.cs](../server/1%20-%20Frameworks%20%26%20Drivers/TechChallenge.Oficina.Billing.API/Program.cs), em volta de `ApplyMigrations()`. A API encerra com código 1 | `error.*` |

Logs sem `EventId` que ajudam no diagnóstico do pagamento:
- `Information`: notificação ignorada (tipo diferente de `payment`, pagamento desconhecido no Mercado Pago ou OS sem cobrança aguardando confirmação).
- `Warning`: pagamento aprovado sem orçamento `AguardandoPagamento` ("PagamentoAprovado nao publicado").
- `Error`: falha inesperada ao processar uma notificação (a resposta continua 200).

O Billing não emite `os.criada`, `os.status_alterado` nem `os.encerrada`: o status da OS é do OS Service.

## 3. Etapas, integrações e operações deste serviço

| `Etapa` | Origem |
|---|---|
| `orcamento_geracao` | `POST /api/orcamentos/{ordemServicoId}/gerar` |
| `orcamento_envio` | `POST /api/orcamentos/{ordemServicoId}/enviar` |
| `orcamento_aprovacao` | `POST /api/orcamentos/{ordemServicoId}/aprovar` |
| `orcamento_recusa` | `POST /api/orcamentos/{ordemServicoId}/recusar` |
| `pagamento_verificacao` | `POST /api/pagamentos/{ordemServicoId}/verificar` |
| `pagamento_notificacao` | `POST /api/pagamentos/notificacoes` (webhook do Mercado Pago) |

- `TipoFalha`: `regra_negocio` (resposta 400) e `nao_encontrado` (resposta 404). Na notificação, a resposta é sempre 200.
- `Integracao`: `resend` (e-mail do orçamento), `mercadopago`, `sqs` e `postgres` (banco indisponível).
- `Operacao`: `envio_orcamento` (Resend), `criacao_cobranca` e `consulta_pagamento` (Mercado Pago), `publicacao` e `consumo` (SQS).
- Sem a chave do Resend, `/enviar` responde 400 e gera `os.falha` (`orcamento_envio`), não `integracao.falha`.

## 4. Health checks

| Endpoint | Verifica o banco? | Quem usa |
|---|---|---|
| `/health` | Não | Probes do Kubernetes, NLB (target group `techchallenge-oficina-billing`), `HEALTHCHECK` do Dockerfile e o synthetic `GET /billing/health` pelo API Gateway |
| `/health/ready` | Sim (check `postgres`, tag `ready`): `Healthy` (200) ou `Unhealthy` (503) | Só o `http_check` do Datadog Agent, na instância `billing-api` (`http://billing-api.billing.svc.cluster.local/health/ready`, service check `http.can_connect`) |

Os dois são anônimos. O `readinessProbe` fica em `/health`, para uma queda do banco não tirar os pods do Service. Nenhum dos dois verifica o SQS, o Mercado Pago ou o Resend: as falhas deles aparecem no `integracao.falha`.

## 5. O que o Datadog cobre deste serviço

- **APM (R1 e R4):** latência p95 e 5xx por rota, por `service`, nos mesmos monitores da Management.
- **Disponibilidade (R3):** `http.can_connect` do `billing-api` e o synthetic `GET /billing/health`, que entra no SLO de uptime.
- **Métricas de log (R7):** `oficina.os.falhas` (por `etapa` e `tipo_falha`) e `oficina.integracao.erros` (por `integracao`, inclusive `mercadopago`) somam os eventos dos três serviços; o monitor de erros de integração também alerta para o Billing. Os eventos `os.orcamento_*`, `pagamento.*` e `evento.*` não geram métricas: são consultados no Log Explorer.
- **Limitação:** os monitores e os widgets de Kubernetes (CPU, memória, CrashLoopBackOff, restarts e réplicas indisponíveis) filtram só o namespace `oficina` (`kube_namespace:oficina`). Os pods deste serviço ficam de fora deles.

## 6. Consultas úteis

No Log Explorer do Datadog:

| Para ver | Consulta |
|---|---|
| Logs deste serviço | `service:billing-api` |
| Erros (inclui o `Fatal`, que vira `emergency`) | `service:billing-api status:(error OR emergency)` |
| Orçamentos gerados | `@evento:os.orcamento_gerado service:billing-api` |
| Cobranças e pagamentos | `@evento:pagamento.* service:billing-api` |
| Pagamentos aprovados | `@evento:pagamento.aprovado` |
| Uso do modo simulado | `@evento:pagamento.simulado` |
| Falhas no Mercado Pago | `@Integracao:mercadopago` |
| Eventos publicados (um por fila) | `@evento:evento.publicado service:billing-api` |
| Eventos ignorados | `@evento:evento.ignorado service:billing-api` |
| Pagamento aprovado sem evento publicado | `service:billing-api "PagamentoAprovado nao publicado"` |
| Histórico de uma OS nos três serviços | `@OrdemServicoId:<guid>` |

Localmente, filtre o stdout do serviço `billing-api` do `docker-compose.yml`:

- Linux/macOS: `docker compose logs billing-api | grep '"evento":"pagamento.aprovado"'`
- Windows PowerShell: `docker compose logs billing-api | Select-String '"evento":"pagamento.aprovado"'`
