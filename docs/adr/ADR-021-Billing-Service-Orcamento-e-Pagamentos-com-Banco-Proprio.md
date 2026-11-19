# ADR-021: Billing Service, Orçamento e Pagamentos com Banco Próprio

**Status**: Aceita (fase 4). Tira o orçamento da Management (item 4 da [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)) e amplia a [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md): terceira fila, *fan-out* no publicador e `PagamentoAprovado` no lugar de `OrcamentoAprovado`. Atualizada com a entrada do Execução e Produção (ver **Atualização (Execução e Produção)**, abaixo)

**Resumo**:
O orçamento (gerar, enviar por e-mail, aprovar e recusar) sai da Management API e, junto com o pagamento pelo Mercado Pago ([ADR-022](ADR-022-Pagamentos-com-Mercado-Pago.md)), vira o **Billing**, um microsserviço com banco, namespace no Kubernetes e pipelines próprios. Aprovar o orçamento passa a exigir o pagamento: só com o pagamento confirmado o Billing publica `PagamentoAprovado`, que leva a OS para execução e faz a Management debitar o estoque. Os serviços continuam conversando só por eventos no Amazon SQS (ADR-020).

> As ADRs e RFCs são numeradas em sequência única entre os repositórios [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), BILLING (este), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice das decisões está no [docs/arquitetura.md do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- Com o OS Service (ADR-019), a Management ficou com os cadastros, o estoque, o orçamento e os e-mails. Aprovar o orçamento debitava o estoque e levava a OS direto para `EmExecucao`, sem cobrança.
- Faltava o pagamento: o cliente precisa pagar o orçamento aprovado antes da execução.
- O orçamento depende dos preços dos serviços e insumos e do estoque, que continuam na Management ([ADR-012](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-012-Controle-de-Estoque-de-Insumos.md)).
- As regras da ADR-020 continuam: um banco por serviço, nenhum serviço lê o banco do outro e não há HTTP entre as APIs. HTTP só para fora: Mercado Pago e Resend.
- O cluster é pequeno (2 nós `t3.small`, [ADR-016](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-016-Escalabilidade-com-HPA.md)), e o custo precisa ficar perto de zero.

**Decisão**:

1. **Fronteiras**:
   - **Billing** (este repositório): o orçamento (gerar, consultar, enviar por e-mail, aprovar e recusar), a cobrança e a confirmação do pagamento no Mercado Pago (ADR-022) e, por evento, a liberação da OS para execução depois do pagamento.
   - **Management** (repo MANAGEMENT): cadastros (clientes, veículos, serviços e insumos), estoque, validação da OS, e-mail de mudança de status e a infraestrutura compartilhada (rede, EKS, API Gateway, NLB, Datadog e filas SQS).
   - **OS Service** (repo OS-SERVICE): o ciclo de vida da OS (ADR-019).
   - Orçamento e pagamento ficam juntos porque são um fluxo só: a aprovação do cliente só se completa com o pagamento. Saem da Management para que a integração de pagamento (SDK, webhook público e registro das cobranças) não fique no serviço dos cadastros e do estoque, e para evoluírem e serem publicados sem ele.

2. **Banco próprio**: RDS PostgreSQL `techchallenge-oficina-billing-postgres` (banco `oficina_billing`), criado em `DB/infra`, nas subnets privadas da rede do MANAGEMENT. As tabelas são criadas pelas migrations do Billing, no startup:

   | Tabela | O que guarda | Colunas |
   |---|---|---|
   | `ordens_servico_referencias` | Snapshot da OS (item 3) | `id` (o da OS), `cliente_id`, `veiculo_id`, `cliente_email`, `estoque_suficiente`, `status`, `valor_total` e `possui_dados` (falso na referência parcial) |
   | `ordens_servico_referencias_servicos` | Serviços do snapshot | `id`, `ordem_servico_id` (chave estrangeira, `CASCADE`), `servico_id`, `nome` e `valor` |
   | `orcamentos` | Um orçamento por OS | `ordem_servico_id` (chave), `data_geracao`, `valor_total` e `status` (token de concorrência) |
   | `orcamentos_servicos` | Itens do orçamento | `id`, `ordem_servico_id` (chave estrangeira, `CASCADE`), `servico_id`, `nome_servico` e `valor_total` |
   | `pagamentos` | Cobranças no provedor (várias por OS) | `id`, `ordem_servico_id` (sem chave estrangeira), `valor`, `status` (token de concorrência), `provedor`, `preferencia_id`, `pagamento_externo_id`, `link_pagamento`, `criado_em` e `atualizado_em` |

   ```mermaid
   erDiagram
     ordens_servico_referencias ||--o{ ordens_servico_referencias_servicos : "tem"
     orcamentos ||--|{ orcamentos_servicos : "tem"
     pagamentos
   ```

   Os pagamentos não têm chave estrangeira: ficam como registro financeiro mesmo depois da exclusão da OS e do orçamento.

3. **Snapshot da OS alimentado por eventos** (`OrdemServicoReferencia`): o Billing não guarda cadastros. Ele mantém uma cópia mínima de cada OS, só com o que o orçamento precisa:
   - `OrdemServicoValidada` (Management, na abertura e na atualização da OS): cliente, veículo, e-mail do cliente, `estoqueSuficiente`, serviços com nome e valor, e `valorTotal`. Preços, e-mail e estoque são calculados pela Management, dona dos cadastros e do estoque, no momento da validação; o Billing só os copia e mantém o status que já conhecia.
   - `OrdemServicoStatusAlterado` (OS Service): o status atual da OS. Gerar, aprovar e recusar exigem a OS em `Recebida`, `EmDiagnostico` ou `AguardandoAprovacao`.
   - `OrdemServicoExcluida` (OS Service): apaga a referência e o orçamento; os pagamentos ficam.
   - `OrdemServicoRejeitada` (Management): apaga só a referência.
   - Gerar o orçamento exige o snapshot completo e `estoqueSuficiente`. O orçamento copia os serviços e os valores do snapshot.

4. **Ordem entre publicadores diferentes**: a fila FIFO entrega as mensagens de uma OS na ordem em que chegaram, mas o `OrdemServicoValidada` (Management) e o `OrdemServicoStatusAlterado` (OS Service) saem de serviços diferentes, sem ordem entre si. Se o status chega primeiro, o Billing cria uma **referência parcial** (só o status, sem serviços), que o `OrdemServicoValidada` completa depois, mantendo o status. Enquanto isso, gerar o orçamento responde 400, avisando que a OS ainda não foi validada pela Management, ou 404 se nenhum evento da OS chegou.

5. **Fan-out no publicador**: o `SQS/infra` do MANAGEMENT cria uma terceira fila, `techchallenge-oficina-billing.fifo` (DLQ `techchallenge-oficina-billing-dlq.fifo`, `maxReceiveCount` 5), e o usuário IAM `techchallenge-oficina-billing-sqs`, que consome a própria fila e publica nas outras duas. Cada serviço publica cada evento nas filas dos outros dois (regra reescrita com o Execução e Produção: ver a atualização abaixo):
   - `SqsSettings:FilasPublicacao` é uma lista (por variável de ambiente, `SqsSettings__FilasPublicacao__0` e `__1`), no lugar do `FilaPublicacao` da ADR-020;
   - o envelope é montado uma vez e enviado a cada fila com o mesmo `id` (a deduplicação do SQS é por fila) e `MessageGroupId` = id da OS, com um log `evento.publicado` por fila;
   - quem não usa o evento o ignora (`evento.ignorado`): a Management ignora `OrcamentoGerado`, e o Billing ignora `OrdemServicoAberta`, por exemplo;
   - se o envio a uma fila falhar, sai `integracao.falha` (com a `Fila`) e a exceção sobe: as filas anteriores já receberam o evento (entrega parcial). É o mesmo risco do "publicar depois de gravar" da ADR-020, coberto pelos consumidores idempotentes.

6. **`PagamentoAprovado` no lugar de `OrcamentoAprovado`**: aprovar é aceitar e pagar antes da execução.
   - `aprovar` cria a cobrança (ADR-022) e deixa o orçamento `AguardandoPagamento`; a OS continua `AguardandoAprovacao`.
   - Com o pagamento confirmado, pela verificação ou pelo webhook, o orçamento fica `Aprovado`, e o Billing publica `PagamentoAprovado` (`ordemServicoId`, `pagamentoId`, `valor`) uma única vez.
   - O OS Service leva a OS para `EmExecucao` (4). A Management debita o estoque uma única vez por OS (marca `EstoqueDebitado` na referência dela), o que antes acontecia na aprovação (ADR-012).
   - `OrcamentoGerado` leva a OS para `AguardandoAprovacao` (3), e `OrcamentoRecusado`, para `Finalizada` (5). O evento `OrcamentoAprovado` deixou de existir.
   - O estoque é conferido na validação, não reservado. Se faltar na hora do débito, a Management registra `os.falha` com a etapa `estoque_debito`, sem nova tentativa nem compensação: o pagamento já foi feito.

7. **Infraestrutura e pipelines próprios**, como na ADR-019:
   - namespace `billing` no mesmo EKS, com o Deployment `billing-api` (Service `NodePort` 30082, atrás da rota `/billing` do API Gateway) e um HPA de 1 a 2 pods (eram 3 antes do Execução e Produção);
   - imagem `techchallenge-oficina-billing` no ECR (repositório criado manualmente);
   - workflows Bootstrap, Deploy, Destroy e K8s Apply ([.github/workflows/README.md](../../.github/workflows/README.md));
   - a rede, o EKS, o API Gateway (inclusive a rota pública do webhook do Mercado Pago), o Datadog e as filas SQS continuam no MANAGEMENT. A chave do Billing nas filas vem do SSM Parameter Store (`/techchallenge-oficina/sqs/billing/...`).
   - Com três APIs, o máximo do HPA caiu para 3 pods em cada uma: 3 APIs × 3 + 10 pods de sistema = 19, dentro dos 22 pods que cabem nos 2 nós `t3.small`. Com o Execução e Produção, caiu para 2 (atualização abaixo).

8. **Os mesmos padrões de código da Management** (ADR-001 a ADR-014, no repo MANAGEMENT), com os projetos `TechChallenge.Oficina.Billing.*` e dois projetos de integração no anel "1 - Frameworks & Drivers": `Email` (Resend, como na [ADR-011](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md)) e `Pagamentos` (Mercado Pago, ADR-022).

**Consequências**:
- ✅ Orçamento e pagamento evoluem, escalam e são publicados sem depender da Management. O SDK do Mercado Pago e o webhook público ficam num serviço só.
- ✅ Com o orçamento gerado, aprovar, pagar e verificar dependem só do Billing e do Mercado Pago. Se a Management ou o OS Service estiverem fora, os eventos esperam na fila.
- ✅ As cobranças ficam num banco próprio, como registro financeiro, mesmo depois da exclusão da OS.
- ⚠ **Consistência eventual**: gerar responde 404 ou 400 até o `OrdemServicoValidada` chegar, e o status da OS no Billing é uma cópia que pode estar alguns segundos atrás.
- ⚠ **Preços congelados na validação**: mudar o preço de um serviço ou insumo na Management não muda um snapshot existente. Só uma nova validação (atualização da OS, enquanto `Recebida`) traz os valores novos.
- ⚠ **Estoque sem reserva**: ele pode acabar entre gerar e pagar, e o débito falha na Management sem compensação automática (item 6).
- ⚠ **Entrega parcial no fan-out** (item 5): o publicador conhece as filas dos outros serviços, e uma falha no meio do envio deixa um deles sem o evento.
- ⚠ **Mais peças para operar**: outro RDS (cobrado por hora), outro repositório ECR, outro conjunto de pipelines e secrets, e uma ordem entre os repositórios (Bootstrap do MANAGEMENT antes do deste; Destroy deste antes do MANAGEMENT).
- ⚠ **Cluster mais apertado**: no máximo 3 pods por API e, desde o Execução e Produção, 2.
- ⚠ **Monitores de Kubernetes do Datadog** (CPU, memória, restarts e réplicas) continuam restritos ao namespace `oficina`; o Billing aparece no APM, nos logs, no `http_check` e no synthetic ([observabilidade.md](../observabilidade.md)).

**Atualização (Execução e Produção)**:
- Entrou um quarto serviço, o Execução e Produção ([ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md)), com a fila de produção, o diagnóstico e a execução. Ele não troca eventos com o Billing: a regra de publicação da ADR-020 passou a ser "cada serviço publica nas filas dos serviços que consomem algum evento dele", e nenhum evento do Billing interessa a ele. Por isso, o Billing continua publicando só nas filas do OS Service e da Management, e o ElasticMQ local continua com as seis filas.
- O Billing só vê o diagnóstico e a finalização pelo status novo da OS, que chega no `OrdemServicoStatusAlterado`, como as outras mudanças.
- Com quatro APIs no cluster, o HPA de cada uma, inclusive o do Billing, ficou de 1 a 2 pods: 4 × 2 + 10 pods de sistema = 18 de 22 ([ADR-016](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-016-Escalabilidade-com-HPA.md)).
- O Bootstrap do Billing continua podendo rodar a qualquer momento depois do Bootstrap do MANAGEMENT.

**Alternativas Rejeitadas**:
- Manter orçamento e pagamento na Management:
  - rejeitada porque juntaria a integração de pagamento (SDK, webhook público e registro das cobranças) aos cadastros e ao estoque, no mesmo deploy e no mesmo banco. A fase 4 separa os serviços, cada um com banco e deploy próprios.
- Consultar a Management (preços, e-mail e estoque) e o OS Service (status) por HTTP ao gerar ou aprovar o orçamento:
  - rejeitada pela regra da ADR-020: não há HTTP entre as APIs. Acoplaria a disponibilidade dos serviços e exigiria chamadas internas autenticadas. O snapshot alimentado por eventos dá ao Billing o que ele precisa.
- Um tópico SNS (FIFO) na frente das filas para o *fan-out*:
  - rejeitada por ora: com três serviços e poucos eventos, o envio direto a cada fila resolve sem tópicos, assinaturas e políticas a mais. Fica como evolução, como já previa a [RFC-004](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-004-Comunicacao-entre-Microsservicos.md).
- Orquestração (o Billing, ou o AWS Step Functions, coordenando validação, orçamento, pagamento e execução):
  - rejeitada pelos motivos da RFC-004: o coordenador precisaria conhecer os passos dos outros serviços. A saga continua coreografada: cada serviço reage aos eventos.

**Relacionados**: [ADR-022](ADR-022-Pagamentos-com-Mercado-Pago.md) (este repo), [ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md) (repo OS-SERVICE), [ADR-023](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-023-Execucao-e-Producao-Fila-de-Execucao-e-Status-por-Eventos.md) (repo EXECUTION), [ADR-012](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-012-Controle-de-Estoque-de-Insumos.md), [ADR-016](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-016-Escalabilidade-com-HPA.md), [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-004](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-004-Comunicacao-entre-Microsservicos.md), no repo MANAGEMENT.
