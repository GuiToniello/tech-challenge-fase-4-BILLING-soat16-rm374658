# ADR-024: Histórico de Pagamentos no Amazon DynamoDB

**Status**: Aceita (fase 4). Complementa a [ADR-021](ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md), em que o PostgreSQL continua sendo o banco do Billing, e a [ADR-022](ADR-022-Pagamentos-com-Mercado-Pago.md), cuja verificação e cujo webhook passam a registrar o resultado no histórico

**Resumo**:
O Billing passa a guardar um **histórico dos eventos de pagamento** de cada OS (cobrança criada, notificação recebida e o resultado de cada verificação ou notificação) numa tabela do **Amazon DynamoDB**, um banco NoSQL. O PostgreSQL continua sendo a fonte da verdade: o status do pagamento, o orçamento e a decisão de publicar `PagamentoAprovado` não mudam. O histórico é complementar e descartável: a gravação é *best-effort*, os itens expiram em 180 dias, e nenhuma regra o lê para decidir algo. A consulta é feita em `GET /api/pagamentos/{ordemServicoId}/historico`.

> As ADRs e RFCs são numeradas em sequência única entre os repositórios [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658), BILLING (este), [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658) e [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O índice das decisões está no [docs/arquitetura.md do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/arquitetura.md#5-decisões-de-arquitetura-rfcs-e-adrs).

**Contexto**:
- A tabela `pagamentos` do RDS (ADR-021) guarda só o estado atual de cada cobrança (`status`, `pagamento_externo_id` e `atualizado_em`). As verificações, as notificações do Mercado Pago e os resultados intermediários (por exemplo, pendente, depois recusado, depois aprovado) não ficam registrados em nenhum lugar que a API consulte.
- Esse histórico tem o formato típico de NoSQL: só recebe gravações novas (*append-only*), é lido sempre pela OS (uma chave), sem junções, os atributos variam conforme o evento (`PreferenciaId` só na cobrança, `PagamentoExternoId` só depois da consulta ao provedor) e pode expirar sozinho.
- A [RFC-002](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-002-Escolha-do-Banco-de-Dados.md) rejeitou o DynamoDB como banco principal, porque o modelo da oficina é cheio de relacionamentos ([ADR-017](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md)). Isso continua valendo: aqui, ele entra só para um dado sem relacionamentos.
- Um banco por serviço ([ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e ADR-021): a tabela é só do Billing.
- É um projeto de estudo: o custo precisa ficar perto de zero, e o ambiente é destruído depois da demonstração.

**Decisão**:

1. **Papel de cada banco**:

   | Banco | O que guarda | Papel |
   |---|---|---|
   | PostgreSQL (RDS, banco `oficina_billing`) | Snapshot da OS, orçamento e cobranças (`pagamentos`) | **Fonte da verdade**. Decide o status do pagamento e do orçamento e a publicação do `PagamentoAprovado`, como antes |
   | DynamoDB (tabela `techchallenge-oficina-billing-pagamentos-historico`) | Um item por evento de pagamento | **Histórico complementar e descartável**, só para consulta e auditoria. Nenhuma regra o lê; se faltar um item, nada quebra |

2. **Tabela**, em [DB/infra/dynamodb.tf](../../DB/infra/dynamodb.tf), no mesmo Terraform, state e pipeline do RDS:
   - sob demanda (`PAY_PER_REQUEST`), sem capacidade provisionada;
   - chave de partição `OrdemServicoId` (S) e chave de ordenação `OcorridoEm` (S): data e hora UTC em ISO-8601 com milissegundos, mais `#` e 8 caracteres hexadecimais (por exemplo, `2026-11-24T10:00:00.000Z#abcd1234`). A data ordena os eventos, e o sufixo evita colisão entre dois eventos no mesmo milissegundo;
   - TTL no atributo `ExpiraEm` (epoch em segundos, preenchido pela API: data do evento + 180 dias);
   - sem proteção contra exclusão (o Destroy apaga a tabela), sem PITR e sem streams;
   - outputs `dynamodb_historico_pagamentos_nome` e `dynamodb_historico_pagamentos_arn`.

3. **Item**, sem dados pessoais (o e-mail do cliente nunca vai para o histórico):

   | Atributo | Conteúdo |
   |---|---|
   | `Tipo` | `CobrancaCriada`, `NotificacaoRecebida`, `PagamentoPendente`, `PagamentoAprovado` ou `PagamentoRecusado` |
   | `Origem` | `aprovacao`, `verificacao` ou `notificacao` |
   | `PagamentoId`, `Provedor` e `Valor` | Sempre presentes |
   | `PagamentoExternoId` e `PreferenciaId` | Só quando existem |

4. **Quando registra**: sempre depois de gravar no PostgreSQL e de publicar o `PagamentoAprovado`, com `CancellationToken.None`, para o registro não se perder se a requisição for cancelada.

   | Fluxo | O que registra |
   |---|---|
   | `aprovar` (`OrcamentoUseCases.AprovarAsync`), com cobrança nova | `CobrancaCriada`, com o `PreferenciaId`. A aprovação repetida, que devolve a cobrança pendente, não registra |
   | `verificar` (`PagamentoUseCases.VerificarAsync`) | O resultado: `PagamentoAprovado`, `PagamentoRecusado` ou `PagamentoPendente`, conforme o status da cobrança depois de aplicada a situação do provedor (sem pagamento no provedor, `PagamentoPendente`). Cobrança já aprovada: nada |
   | Webhook (`PagamentoUseCases.ProcessarNotificacaoAsync`) | `NotificacaoRecebida`, com o horário de chegada, e depois o resultado, como na verificação. Notificação repetida de pagamento já aprovado: só `NotificacaoRecebida`. Pagamento desconhecido no provedor ou OS sem cobrança: nada |

5. **Best-effort**:
   - `RegistrarAsync` nunca lança: uma falha (DynamoDB fora do ar, permissão negada, tabela inexistente) gera `integracao.falha` (3001) como **Warning**, com `Integracao` = `dynamodb` e `Operacao` = `registro_historico`, e o fluxo de pagamento segue. Só aquela linha falta no histórico.
   - `ListarAsync` registra `integracao.falha` como **Error** (`Operacao` = `consulta_historico`) e relança a falha: a rota de consulta responde 500, pelo middleware global ([ADR-013](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md)).
   - A consulta é um `Query` na partição da OS, em ordem crescente da chave de ordenação, com leitura consistente (`ConsistentRead`) e percorrendo todas as páginas.

6. **Rota**: `GET /api/pagamentos/{ordemServicoId}/historico`, com JWT (na AWS, `/billing/api/pagamentos/{ordemServicoId}/historico`). Responde 200 com a lista em ordem cronológica, ou com a lista vazia quando não há registros.

7. **Código**, no padrão de porta e adapter do e-mail e do pagamento ([ADR-011](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md) e ADR-022):
   - porta `IHistoricoPagamentoGateway` e record `EventoHistoricoPagamento` no projeto UseCases (`Features/Pagamentos/Interfaces`). No `OrcamentoUseCases`, a porta chega pelo facade `IOrcamentoUseCasesFacade`, como as outras integrações;
   - projeto novo `TechChallenge.Oficina.Billing.NoSql` (anel "1 - Frameworks & Drivers"), o único com o pacote `AWSSDK.DynamoDBv2` (4.0.*): `HistoricoPagamentoGateway`, a interface interna `IDynamoDbClient` com o `DynamoDbClientAdapter` (o único ponto que usa o SDK) e o `AddInfraNoSql`;
   - configuração `DynamoDbSettings`: `TabelaHistoricoPagamentos` (na AWS, `DynamoDbSettings__TabelaHistoricoPagamentos` no ConfigMap) e `ServiceUrl` (só localmente);
   - testes no projeto `TechChallenge.Oficina.Billing.NoSql.Tests`, com o SDK mockado; os testes de integração usam um histórico em memória.

8. **Credenciais e IAM, sem usuário nem chave novos**: o [DB/infra/iam.tf](../../DB/infra/iam.tf) anexa a política inline `dynamodb-historico-pagamentos` (só `dynamodb:PutItem` e `dynamodb:Query`, no ARN da tabela) ao usuário IAM do Billing que já existe, `techchallenge-oficina-billing-sqs`, criado pelo `SQS/infra` do MANAGEMENT. A chave desse usuário já chega ao pod (SSM → Secret `billing-api-secrets`) e passa a servir às filas e à tabela. Na AWS, a API nunca cria nem descreve a tabela (sem `CreateTable` e `DescribeTable`): ela é do Terraform. Se o MANAGEMENT recriar o usuário, a política some junto, e é preciso rodar o Bootstrap deste repositório de novo.

9. **Ambiente local**: o `docker-compose.yml` sobe o DynamoDB Local (`amazon/dynamodb-local:3.1.0`, em memória, porta `8000`), e a API aponta para ele por `DynamoDbSettings__ServiceUrl`, com as mesmas credenciais fictícias do ElasticMQ. Só com `ServiceUrl` preenchido a API cria a tabela no startup (`GarantirTabelasLocaisAsync`), com as mesmas chaves e o mesmo TTL; uma falha aí só gera um aviso, e a API sobe.

**Consequências**:
- ✅ Dá para ver tudo o que aconteceu com o pagamento de uma OS (cobranças, notificações e resultados), sem mudar a tabela `pagamentos` nem as regras do pagamento.
- ✅ Custo perto de zero: tabela sob demanda, poucos itens e expiração automática, sem instância ligada.
- ✅ Uma falha no DynamoDB não derruba o pagamento.
- ✅ NoSQL onde o formato combina (gravação e leitura por chave) e relacional onde há relacionamentos, como recomenda a RFC-002.
- ⚠ **O histórico pode ter buracos**: uma falha de gravação perde aquela linha (fica só o log `integracao.falha`), e os itens somem depois de 180 dias. Não serve como registro financeiro, que continua sendo a tabela `pagamentos`.
- ⚠ **Sem atomicidade entre os dois bancos**: as gravações são separadas. Se o processo cair entre elas, o histórico fica sem o registro.
- ⚠ **O monitor de erros de integração também alerta para `dynamodb`**: a métrica `oficina.integracao.erros` (filtro `@Integracao:*`, no MANAGEMENT) conta inclusive o Warning do registro ([observabilidade.md](../observabilidade.md)).
- ⚠ **Mais uma ligação com o MANAGEMENT**: a política fica no usuário IAM criado por ele; se o usuário for recriado, o Bootstrap do Billing precisa rodar de novo. O usuário `terraform` da CI precisa de permissões no DynamoDB e de `iam:PutUserPolicy` (hoje ele é administrador).
- ⚠ **Mais uma peça local**: o DynamoDB Local roda em memória, e o histórico some quando o container reinicia.

**Alternativas Rejeitadas**:
- Levar o snapshot da OS (o *read model* alimentado pelos eventos) para o DynamoDB:
  - rejeitada porque mexeria em mais de 20 arquivos, exigiria migrar os dados e colocaria o DynamoDB no caminho crítico do consumidor SQS e de quatro rotas (gerar, enviar, aprovar e recusar). O histórico mostra o uso de NoSQL sem esse risco.
- Uma tabela de histórico no próprio PostgreSQL:
  - rejeitada porque não tem expiração nativa (exigiria uma rotina de limpeza) e trataria com colunas opcionais um dado que é só gravação e leitura por chave, o caso de uso para o qual o DynamoDB sob demanda serve, sem custo fixo.
- Um usuário IAM e uma chave só para o DynamoDB:
  - rejeitada porque traria mais um segredo no SSM, no Secret e no pipeline, sem ganho real: a política inline já limita o acesso a `PutItem` e `Query` nesta tabela.
- Criar a tabela no MANAGEMENT, junto das filas SQS:
  - rejeitada porque a tabela é só do Billing (um banco por serviço). Ela fica no `DB/infra` deste repositório, ao lado do RDS.

**Fora do escopo**: levar a OS, o orçamento ou o pagamento para o NoSQL; PITR (backup contínuo); DynamoDB Streams; métricas e monitores específicos do DynamoDB no Datadog.

**Relacionados**: [ADR-021](ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md) e [ADR-022](ADR-022-Pagamentos-com-Mercado-Pago.md) (este repo), [ADR-025](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658/blob/main/docs/adr/ADR-025-Linha-do-Tempo-da-Producao-no-DynamoDB.md) (repo EXECUTION: o mesmo padrão na linha do tempo da produção do Execução e Produção, com tabela e política próprias), [ADR-026](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-026-Historico-da-Saga-no-DynamoDB.md) (repo OS-SERVICE: o mesmo padrão no histórico da saga do OS Service, com tabela e política próprias), [ADR-011](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-011-Envio-de-Email-de-Orcamento.md), [ADR-013](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-013-Estrategia-de-Tratamento-de-Erros-em-Duas-Camadas.md), [ADR-014](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-014-Observabilidade-com-Datadog.md), [ADR-017](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-017-PostgreSQL-no-Amazon-RDS-e-Modelo-de-Dados.md), [ADR-020](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/adr/ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md) e [RFC-002](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/docs/rfc/RFC-002-Escolha-do-Banco-de-Dados.md), no repo MANAGEMENT.
