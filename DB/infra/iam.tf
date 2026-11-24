# Permissao da API do Billing na tabela de historico (dynamodb.tf).
# Anexada ao usuario IAM do Billing que ja existe, criado pelo SQS/infra do repo MANAGEMENT (Bootstrap
# da MANAGEMENT antes, como hoje): o pod reaproveita a mesma chave de acesso (SSM -> Secret), sem
# chave ou secret novos. Politica inline com nome proprio, separada da politica "sqs" da MANAGEMENT.
# Se a MANAGEMENT recriar o usuario, a politica some junto: rode de novo o Bootstrap deste repo
# (reaplica o DB/infra, recriando a politica, e os manifests com a chave nova).

data "aws_iam_policy_document" "dynamodb" {
  statement {
    sid = "HistoricoPagamentos"
    actions = [
      "dynamodb:PutItem",
      "dynamodb:Query",
    ]
    resources = [aws_dynamodb_table.historico_pagamentos.arn]
  }
}

resource "aws_iam_user_policy" "dynamodb" {
  name   = "dynamodb-historico-pagamentos"
  user   = "${var.project_name}-billing-sqs"
  policy = data.aws_iam_policy_document.dynamodb.json
}
