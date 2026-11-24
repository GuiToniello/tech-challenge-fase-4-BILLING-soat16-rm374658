# Pipelines (GitHub Actions)

As pipelines deste repositório cuidam só do Billing:

- **Aplicação** (`server/`): build, testes e a imagem do Billing no ECR, no repositório `techchallenge-oficina-billing`, criado manualmente.
- **Banco** (`DB/infra`): o RDS PostgreSQL do Billing, a tabela DynamoDB do histórico de pagamentos ([ADR-024](../../docs/adr/ADR-024-Historico-de-Pagamentos-no-DynamoDB.md)) e a política IAM dela, anexada ao usuário IAM do Billing criado pelo MANAGEMENT.
- **Manifests** (`kubernetes/k8s`): namespace `billing`, ConfigMap, Secret, Deployment, Service `NodePort` 30082 e HPA.

A rede, o EKS, o API Gateway (rota `/billing` e a rota pública do webhook do Mercado Pago), o NLB, o Datadog e as filas SQS (com o usuário IAM do Billing e a chave dele no SSM) ficam no repo [MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658), que precisa estar de pé antes. O padrão é o mesmo das [pipelines do MANAGEMENT](https://github.com/GuiToniello/tech-challenge-fase-4-MANAGEMENT-soat16-rm374658/blob/main/.github/workflows/README.md) e do [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/.github/workflows/README.md), com menos partes.

```
.github/workflows/
  _build-test.yml   (workflow_call)      - restore, build e testes da solution .slnx
  _build-push.yml   (workflow_call)      - docker compose build + push da imagem do Billing no ECR
  _terraform.yml    (workflow_call)      - validate/plan/apply/destroy do DB/infra
  _k8s-apply.yml    (workflow_call)      - kustomize offline (validate) ou kubectl apply -k no EKS (apply)
  bootstrap.yml     (workflow_dispatch)  - sobe o Billing, na ordem
  deploy.yml        (pull_request, push) - PR: valida tudo; push na main: aplica só o que mudou, na ordem
  destroy.yml       (workflow_dispatch)  - remove o namespace, o RDS e a tabela DynamoDB do Billing
  k8s-apply.yml     (workflow_dispatch)  - reaplica os manifests e reinicia a API
```

**Ordem com os outros repositórios:**
- **Para subir:** Bootstrap do MANAGEMENT → Bootstrap deste repo, a qualquer momento (ele não depende do [OS-SERVICE](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658) nem do [EXECUTION](https://github.com/GuiToniello/tech-challenge-fase-4-EXECUTION-AND-PRODUCTION-soat16-rm374658), que sobem nesta ordem: OS-SERVICE → EXECUTION) → Deploy do [LAMBDA](https://github.com/GuiToniello/tech-challenge-fase-4-LAMBDA-soat16-rm374658). O MANAGEMENT vem antes também porque o `DB/infra` daqui anexa a política do DynamoDB ao usuário IAM do Billing, criado por ele. Se o MANAGEMENT recriar esse usuário, a política some junto: rode o Bootstrap deste repo de novo.
- **Para derrubar:** Destroy deste repo, do OS-SERVICE, do EXECUTION e do LAMBDA (em qualquer ordem) → Destroy do MANAGEMENT. O Destroy do MANAGEMENT (job `billing-check`) falha se o RDS `techchallenge-oficina-billing-postgres` ou o SG `techchallenge-oficina-billing-rds-sg` ainda existirem.

## 1. Workflows reutilizáveis

### `_build-test.yml`
- `dotnet restore`, `dotnet build -c Release` e `dotnet test -c Release` em `server/TechChallenge.Oficina.Billing.slnx`, com .NET 10.
- Os testes usam banco em memória e mocks (SQS, Resend e DynamoDB), e o SDK do Mercado Pago roda contra um `HttpMessageHandler` falso: não precisam de AWS, banco, Docker nem conta no Mercado Pago.

### `_build-push.yml`
- Recebe o input `image-tag`; o Bootstrap e o Deploy passam sempre `latest`, a tag dos manifests.
- Falha fora da `main`, confere a variable `ECR_REGISTRY`, faz login no ECR e roda `docker compose build` e `docker compose push` do serviço `billing-api`.

### `_terraform.yml`
- `working-directory`: só `DB/infra`. `command`: `validate`, `plan`, `apply` ou `destroy`.
- Falha em apply/destroy fora da `main`. Terraform 1.15.8, `init` (com `-backend=false` no validate), `fmt -check -recursive` e `validate`; depois, `plan -lock=false` ou `apply`/`destroy -auto-approve -lock-timeout=15m`.
- Recebe `TF_VAR_rds_password` do secret `RDS_PASSWORD`.
- O `DB/infra` tem, no mesmo state, o RDS (`rds.tf`), a tabela DynamoDB do histórico de pagamentos (`dynamodb.tf`) e a política inline `dynamodb-historico-pagamentos` (`iam.tf`), anexada ao usuário IAM `techchallenge-oficina-billing-sqs`.

| Pasta | State (bucket `terraform-state-soat16`) |
|---|---|
| `DB/infra` | `techchallenge-oficina/fase-4/billing-database.tfstate` |

### `_k8s-apply.yml`
- **validate:** copia `kubernetes/k8s/.env.example` para `.env` e roda `kubectl kustomize kubernetes/k8s`, offline.
- **apply:**
  1. Bloqueio fora da `main` e checkout da ponta da `main`.
  2. Confere `EKS_CLUSTER_NAME`, `RDS_INSTANCE_IDENTIFIER`, `RDS_DATABASE`, `RDS_USERNAME` e `RDS_PASSWORD`. `MERCADOPAGO_ACCESS_TOKEN` e `RESEND_API_KEY` são opcionais: vazios, só geram um aviso. Depois roda `aws eks update-kubeconfig` (o EKS é do MANAGEMENT).
  3. Descobre o endpoint do RDS pelo `RDS_INSTANCE_IDENTIFIER`.
  4. Descobre o endpoint do API Gateway pelo nome da API (`techchallenge-oficina-api`, criada pelo Bootstrap do MANAGEMENT). Ele muda a cada recriação, por isso não é variable.
  5. Gera o `kubernetes/k8s/.env` com a connection string, `MercadoPagoSettings__AccessToken`, `MercadoPagoSettings__NotificationUrl` (`<endpoint do API Gateway>/billing/api/pagamentos/notificacoes`) e `ResendSettings__ApiKey`. Os valores entram por variável de ambiente, para caracteres como `$` na senha não serem interpretados.
  6. Lê do SSM a chave do usuário IAM do Billing (`/techchallenge-oficina/sqs/billing/access-key-id` e `/secret-access-key`, criados pelo `SQS/infra` do MANAGEMENT), mascara os valores e os acrescenta ao `.env` como `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`. A mesma chave serve às filas SQS e à tabela DynamoDB do histórico. Se os parâmetros não existirem, falha pedindo o Bootstrap do MANAGEMENT.
  7. `kubectl apply -k kubernetes/k8s` e, se `restart-pods`, `kubectl rollout restart` no namespace `billing`.
- O Secret `billing-api-secrets` não tem hash no nome: trocar a senha, a chave do usuário IAM, o token do Mercado Pago ou a chave do Resend só chega aos pods com restart.

## 2. `bootstrap.yml`

- **Gatilho:** `workflow_dispatch`, a partir da `main`.
- **Jobs, nesta ordem:** `build-test` → `terraform-db` (RDS, tabela DynamoDB e política IAM) → `build-push` → `k8s-apply` (com restart).
- Use na primeira criação, para recriar o Billing depois de um Destroy (deste repo ou do MANAGEMENT), para reaplicar a política do DynamoDB depois que o MANAGEMENT recriar o usuário IAM do Billing ou para reaplicar a HEAD da `main`.

## 3. `deploy.yml`

| Evento | Jobs | Observação |
|---|---|---|
| `pull_request` → `main` | `build-test`, `validate-db`, `validate-k8s` | **Checks obrigatórios.** Não usam AWS |
| | `plan-db` | Informativo. Precisa do cluster do MANAGEMENT de pé (o DB acha a VPC pelo EKS). Pulado em PR de fork |
| `push` na `main` | `build-test` e `changes` (`dorny/paths-filter`) | Filtros: `app` (`server/**`, `docker-compose.yml`, `_build-test.yml` e `_build-push.yml`), `db` (`DB/infra/**` e `_terraform.yml`) e `k8s` (`kubernetes/k8s/**` e `_k8s-apply.yml`) |
| | `apply-db` | Se `db` mudou |
| | `build-push` | Se `app` mudou. Espera os testes e o DB |
| | `apply-k8s` (com restart) | Se qualquer parte mudou (imagem nova, manifests ou RDS recriado) |

- **O PR não usa filtro de paths**, para os checks obrigatórios não ficarem *pending* em PRs só de documentação.
- Os pushes na `main` rodam um de cada vez, na ordem; num PR, um run novo cancela o anterior.
- **Custo:** o primeiro push na `main` marca todos os arquivos como alterados e aplica tudo, inclusive o RDS. Se os secrets da AWS já estiverem configurados, ele cria o RDS do Billing (cobrado por hora); sem eles, os applies falham na credencial e não criam nada. Com o MANAGEMENT desligado, o `apply-db` e o `apply-k8s` falham, porque não há cluster.
- **Não use "Re-run" em um Deploy antigo:** ele publicaria `latest` de um commit antigo. Para reaplicar a HEAD, use o Bootstrap.

## 4. `destroy.yml`

- **Gatilho:** `workflow_dispatch` com o input obrigatório `confirm`.

| Job | Quando roda | O que faz |
|---|---|---|
| `reject` | `confirm` ≠ `destroy` ou branch ≠ `main` | **Falha** com erro, em vez de terminar verde sem ter destruído nada |
| `gate` | `confirm` = `destroy` na `main` | `environment: destroy`: aprovação manual |
| `k8s-delete` | depois do `gate` | Se o cluster do MANAGEMENT existir, apaga o namespace `billing` (e tudo dentro dele); se não existir, só avisa |
| `terraform-destroy-db` | depois do `k8s-delete` | Destroy do `DB/infra`: RDS, tabela DynamoDB e política IAM. **Os dados do RDS do Billing (orçamentos e pagamentos) e o histórico de pagamentos são perdidos** |

- Rode **antes** do Destroy do MANAGEMENT: o `DB/infra` encontra a VPC pelo cluster EKS, o MANAGEMENT só derruba a rede depois que este RDS sai, e a política da tabela DynamoDB está no usuário IAM do Billing, criado por ele.
- Configure o environment `destroy` antes do primeiro uso, com *Required reviewers* e *Deployment branches* restrito a `main`.

## 5. `k8s-apply.yml`

- **Gatilho:** `workflow_dispatch`, com o input `restart-pods` (padrão `true`).
- **Quando usar:**
  - depois de criar ou trocar os secrets `MERCADOPAGO_ACCESS_TOKEN` ou `RESEND_API_KEY`;
  - depois que o MANAGEMENT recriar as filas e a chave do usuário IAM do Billing ou o API Gateway (a URL de notificação do Mercado Pago muda);
  - depois de ligar ou desligar o Datadog no MANAGEMENT, ou para reiniciar a API.
- **Para trocar `RDS_PASSWORD`, use o Bootstrap**, que aplica a senha nova no RDS antes de gravá-la no Secret. **Se o MANAGEMENT recriar o próprio usuário IAM do Billing**, também use o Bootstrap: a política do DynamoDB precisa ser aplicada de novo no usuário novo.

---

## Secrets e Variables

Configure em **Settings → Secrets and variables → Actions**, no nível do repositório:

| Nome | Tipo | Uso |
|---|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Secret | Todos os jobs que acessam a AWS (o mesmo usuário IAM `terraform` do MANAGEMENT) |
| `RDS_PASSWORD` | Secret | Senha master do RDS do Billing: `_terraform.yml` (`TF_VAR_rds_password`) e `_k8s-apply.yml` (connection string). Use de 8 a 128 caracteres ASCII imprimíveis, sem `/`, `'`, `"`, `@`, espaço e `;` |
| `MERCADOPAGO_ACCESS_TOKEN` | Secret (opcional) | `_k8s-apply.yml` → `MercadoPagoSettings__AccessToken`. Vazio, o Billing roda no modo simulado. Para testar sem dinheiro real, use o Access Token das credenciais de teste (`APP_USR-...`) |
| `RESEND_API_KEY` | Secret (opcional) | `_k8s-apply.yml` → `ResendSettings__ApiKey`. Vazio, o envio do orçamento por e-mail responde 400 |
| `AWS_REGION` | Variable | Todos (`us-east-1`) |
| `ECR_REGISTRY` | Variable | `_build-push.yml` (`903936907231.dkr.ecr.us-east-1.amazonaws.com`) |
| `EKS_CLUSTER_NAME` | Variable | `_k8s-apply.yml` e `destroy.yml` (`techchallenge-oficina-eks`) |
| `RDS_INSTANCE_IDENTIFIER` | Variable | `_k8s-apply.yml` (`techchallenge-oficina-billing-postgres`) |
| `RDS_DATABASE` / `RDS_USERNAME` | Variable | `_k8s-apply.yml` (`oficina_billing` / `sa`) |

Não há secret para o SQS, o DynamoDB nem o Datadog: a chave do usuário IAM do Billing (filas e tabela) vem do SSM, e o Datadog é configurado no MANAGEMENT. A URL de notificação do Mercado Pago também não é configurada: o `_k8s-apply.yml` a monta com o endpoint do API Gateway.

**Permissões IAM do usuário `terraform`:** as mesmas usadas pelo MANAGEMENT. O state fica em `techchallenge-oficina/fase-4/billing-database.tfstate`, coberto pela permissão em `techchallenge-oficina/fase-4/*`, e o push no ECR usa a permissão em `repository/techchallenge-oficina-*`. O `DB/infra` também cria e apaga a tabela DynamoDB e a política inline do usuário do Billing: precisa de permissões no DynamoDB e de `iam:PutUserPolicy` (com `iam:GetUserPolicy` e `iam:DeleteUserPolicy`); hoje o usuário é administrador. O apply dos manifests também lê os parâmetros do SSM (`ssm:GetParameter`) e consulta o RDS e o API Gateway pelo nome.

**Environment `destroy`** (Settings → Environments): crie antes do primeiro Destroy, com *Required reviewers* e *Deployment branches* restrito a `main`.

**Proteção da `main`** (Settings → Rules → Rulesets): exija Pull Request e os checks abaixo, que aparecem na lista depois que o primeiro PR roda.
- `build-test / build-test`
- `validate-db / terraform`
- `validate-k8s / kubectl`

Os callers passam os secrets aos workflows reutilizáveis com `secrets: inherit` (o `_build-test.yml` não usa secrets), e todos os workflows declaram `permissions: contents: read`.
