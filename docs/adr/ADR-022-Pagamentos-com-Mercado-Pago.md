# ADR-022: Pagamentos com Mercado Pago

**Status**: Aceita (fase 4)

**Resumo**:
O Billing ([ADR-021](ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)) cobra o orçamento aprovado pelo **Checkout Pro do Mercado Pago**: cria uma preferência com o valor do orçamento e devolve o link de pagamento. A situação do pagamento é sempre consultada no próprio Mercado Pago, pela rota de verificação ou pelo webhook, e o `PagamentoAprovado` é publicado uma única vez. Sem access token, um gateway simulado aprova o pagamento, para a saga rodar sem conta no Mercado Pago.

> As ADRs e RFCs são numeradas em sequência única entre os repositórios [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), BILLING (este), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658).

**Contexto**:
- Aprovar o orçamento passou a exigir o pagamento antes da execução (ADR-021).
- É um projeto de estudo: precisa rodar sem conta de pagamento (localmente, nos testes e na demonstração) e, quando houver, com as credenciais de teste do Mercado Pago, sem dinheiro real.
- A API não tem front-end: o cliente precisa pagar numa página pronta, a partir de um link.
- O webhook precisa de uma URL pública, e o Mercado Pago não envia o JWT do Auth0. Localmente não há URL pública.
- As camadas internas não podem depender do SDK, como no e-mail ([ADR-011](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md)), e os testes não podem chamar o Mercado Pago.

**Decisão**:

1. **Checkout Pro**: `POST /api/orcamentos/{ordemServicoId}/aprovar` cria uma preferência (`PreferenceClient.CreateAsync`) com:
   - um item: título "Orçamento da ordem de serviço" com os 8 primeiros caracteres do id da OS, quantidade 1, moeda `BRL` e o valor total do orçamento;
   - `external_reference` = id da OS, usado para achar o pagamento depois;
   - `notification_url`, quando configurada, e o e-mail do cliente como pagador, quando existe.

   A resposta traz o link (`init_point`) em `pagamento.linkPagamento`, e o e-mail do orçamento (`/enviar`) também o leva enquanto a cobrança estiver pendente. Cada cobrança é um `Pagamento` (`Pendente` 1, `Aprovado` 2 ou `Recusado` 3; provedor `MercadoPago` ou `Simulado`). Uma OS pode ter várias cobranças, e vale a mais recente.

2. **A situação é sempre consultada no Mercado Pago**:
   - **Verificação (*pull*)**: `POST /api/pagamentos/{ordemServicoId}/verificar` busca o pagamento mais recente da OS (`PaymentClient.SearchAsync` por `external_reference`, ordenado por `date_created`, decrescente). Sem cobrança: 404. Cobrança já aprovada: responde sem consultar.
   - **Webhook**: `POST /api/pagamentos/notificacoes`, anônimo e sempre 200. Lê `type` e `data.id` (query ou corpo JSON) ou `topic` e `id` (formato IPN legado), e só trata `payment`. A notificação só informa o id: o Billing busca o pagamento (`PaymentClient.GetAsync`) e usa o status e o `external_reference` devolvidos pelo Mercado Pago.
   - **Notificação forjada é inofensiva**: um id que o Mercado Pago da conta não conhece, um pagamento sem OS na referência externa ou uma OS sem cobrança aguardando confirmação são ignorados (só log). Se o processamento falhar, a resposta continua 200, e o pagamento pode ser conferido em `/verificar`.
   - **Histórico** ([ADR-024](ADR-024-Historico-de-Pagamentos-no-DynamoDB.md)): a criação da cobrança, cada notificação processada e o resultado de cada verificação ou notificação também vão para o histórico de pagamentos no DynamoDB, depois da gravação e da publicação. O histórico não participa de nenhuma decisão.
   - A rota pública `POST /billing/api/pagamentos/notificacoes`, sem authorizer, é criada pelo `kubernetes/infra/foundation` do MANAGEMENT ([api-gateway.tf](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/kubernetes/infra/foundation/api-gateway.tf)). A rota exata tem prioridade sobre `/billing/{proxy+}`.

3. **Mapeamento de status**:

   | Status no Mercado Pago | Situação no Billing | Efeito |
   |---|---|---|
   | `approved`, `authorized` | `Aprovado` | Pagamento e orçamento `Aprovado`, e `PagamentoAprovado` publicado, se o orçamento estava `AguardandoPagamento`. Senão (por exemplo, orçamento recusado depois da cobrança), o pagamento fica `Aprovado`, mas o evento não é publicado (log de aviso) |
   | `pending`, `in_process`, `in_mediation` e qualquer outro | `Pendente` | Nada muda |
   | `rejected`, `cancelled`, `refunded`, `charged_back` | `Recusado` | Pagamento `Recusado` (só a partir de `Pendente`). O orçamento continua `AguardandoPagamento` |

   Um pagamento `Recusado` ainda pode ser aprovado depois, porque o checkout aceita nova tentativa no mesmo link. Aprovar o orçamento de novo cria outra cobrança.

4. **Modo simulado automático**: sem `MercadoPagoSettings:AccessToken`, o `AddInfraPagamentos` registra o `GatewayPagamentoSimulado`, que não chama o Mercado Pago:
   - a cobrança recebe o link fictício `https://simulado.mercadopago.local/checkout/{ordemServicoId}` e a preferência `SIM-<guid>`;
   - a verificação aprova na hora, com `pagamentoExternoId` = `SIM-PAY-{ordemServicoId:N}` (o id da OS sem hífens);
   - cada operação registra `pagamento.simulado` (Warning).

   É o modo do `docker compose`, dos testes de integração e da AWS sem o secret `MERCADOPAGO_ACCESS_TOKEN`.

5. **Configuração e segredos**: `MercadoPagoSettings:AccessToken` e `MercadoPagoSettings:NotificationUrl`, vazios no `appsettings.json`.
   - Na AWS, o `_k8s-apply.yml` grava no Secret `billing-api-secrets`:
     - `MercadoPagoSettings__AccessToken`, do secret opcional `MERCADOPAGO_ACCESS_TOKEN` do GitHub;
     - `MercadoPagoSettings__NotificationUrl`, montada com o endpoint do API Gateway (descoberto pelo nome `techchallenge-oficina-api`, que muda a cada recriação) mais `/billing/api/pagamentos/notificacoes`.
   - Localmente, `MERCADOPAGO_ACCESS_TOKEN` e `MERCADOPAGO_NOTIFICATION_URL` no `.env`, opcionais. Sem URL pública, a confirmação é pela verificação.

6. **SDK isolado, no padrão do e-mail (ADR-011)**:
   - Porta `IGatewayPagamento` no projeto UseCases (`Features/Pagamentos/Interfaces`): `CriarCobrancaAsync`, `ConsultarPorReferenciaAsync` e `ConsultarPorIdAsync`.
   - Projeto `TechChallenge.Oficina.Billing.Pagamentos` (anel "1 - Frameworks & Drivers"), o único com o pacote `mercadopago-sdk` (3.7.1):
     - `GatewayPagamentoMercadoPago`: mapeia os status e registra `integracao.falha` (`Integracao` = `mercadopago`, `Operacao` = `criacao_cobranca` ou `consulta_pagamento`) antes de relançar a falha;
     - `GatewayPagamentoSimulado` (item 4);
     - `IMercadoPagoClient`, interface interna com o `MercadoPagoClientAdapter`, o único ponto que usa o SDK: cria a preferência, busca e consulta pagamentos. Cada chamada leva um `RequestOptions` novo com o access token, sem o estado global do `MercadoPagoConfig`, e o `HttpClient` tem timeout de 30 s.
   - Os testes trocam o `HttpMessageHandler` do SDK por um falso: nenhuma chamada real ao Mercado Pago.

7. **Idempotência e concorrência**:
   - `aprovar` com uma cobrança pendente devolve a mesma (mesmo link), sem criar outra. Depois de um pagamento recusado, cria uma nova.
   - O `PagamentoAprovado` sai uma vez: a verificação de um pagamento já aprovado não consulta nem publica, e a notificação de uma OS sem cobrança aguardando confirmação é ignorada.
   - O `status` de `pagamentos` e de `orcamentos` é token de concorrência: se duas confirmações do mesmo pagamento correrem juntas, a segunda falha ao gravar e não publica nada.
   - Primeiro grava o pagamento e o orçamento, depois publica, sem cancelar a publicação junto com a requisição.

**Consequências**:
- ✅ Pagamento real (com as credenciais de teste) sem mudar o código: basta o secret e o K8s Apply.
- ✅ Webhook seguro sem validar a assinatura: ele só dispara uma nova consulta ao Mercado Pago.
- ✅ Sem webhook (localmente, ou quando o pagamento de teste não notifica), a verificação resolve.
- ✅ A saga e os testes rodam sem conta, sem rede e sem dinheiro.
- ⚠ **Confirmação manual sem webhook**: alguém precisa chamar `/verificar` (a collection do Postman faz isso).
- ⚠ **Uma consulta ao Mercado Pago por verificação ou notificação**: notificações forjadas não aprovam nada, mas geram consultas. Uma falha do Mercado Pago gera `integracao.falha`, e a verificação responde 500.
- ⚠ **Access token de longa duração**: fica no secret do GitHub e no Secret do Kubernetes.
- ⚠ **O modo simulado sempre aprova**, inclusive pela notificação com o id `SIM-PAY-...`: serve só para demonstração.
- ⚠ **Pagamento sem orçamento aguardando** (por exemplo, orçamento recusado depois da cobrança): o pagamento fica registrado, sem `PagamentoAprovado` e sem estorno automático.

**Alternativas Rejeitadas**:
- API de Orders do Mercado Pago, com PIX gerado pelo Billing:
  - rejeitada porque o Billing teria de montar e exibir o QR code e tratar a expiração. O Checkout Pro devolve um link pronto, com a página de pagamento do Mercado Pago.
- Payments API direta (Checkout Transparente):
  - rejeitada porque exige um formulário próprio para coletar e tokenizar o cartão. A API não tem front-end, e os dados do cartão ficariam mais perto do serviço.
- Confiar no conteúdo da notificação, sem consultar o Mercado Pago:
  - rejeitada porque qualquer um poderia aprovar um pagamento com uma notificação forjada.
- Validar a assinatura (`x-signature`) do webhook:
  - fica como evolução: exige mais um segredo do painel do Mercado Pago, e a nova consulta já torna a notificação forjada inofensiva.

**Fora do escopo**: estornos, validação do `x-signature` e compensação de estoque depois do pagamento (ADR-021).

Detalhes oficiais do Checkout Pro: [documentação do Mercado Pago](https://www.mercadopago.com.br/developers/pt/docs/checkout-pro/landing).

**Relacionados**: [ADR-021](ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) e [ADR-024](ADR-024-Historico-de-Pagamentos-no-DynamoDB.md) (este repo), [ADR-011](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md), [ADR-013](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md), [ADR-014](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-014-Observabilidade-com-Datadog.md) e [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md), no repo MANAGEMENT.
