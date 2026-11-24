# Historico de pagamentos do Billing em NoSQL (DynamoDB), complementar ao RDS.
# O PostgreSQL (rds.tf) continua sendo a fonte da verdade: a API grava aqui em best-effort, e uma
# falha no DynamoDB nao quebra o fluxo de pagamento. Os itens sao descartaveis e expiram pelo TTL
# (ExpiraEm, epoch em segundos preenchido pela API, 180 dias apos o evento).
# Chave: OrdemServicoId (particao) + OcorridoEm (ordenacao, data/hora em texto), para consultar o
# historico de uma OS em ordem cronologica.
# Sob demanda, sem PITR e sem streams; deletion_protection desligado para o Destroy funcionar.
# As tags vem do default_tags do provider.
resource "aws_dynamodb_table" "historico_pagamentos" {
  name                        = "${local.name_prefix}-pagamentos-historico"
  billing_mode                = "PAY_PER_REQUEST"
  hash_key                    = "OrdemServicoId"
  range_key                   = "OcorridoEm"
  deletion_protection_enabled = false

  attribute {
    name = "OrdemServicoId"
    type = "S"
  }

  attribute {
    name = "OcorridoEm"
    type = "S"
  }

  ttl {
    attribute_name = "ExpiraEm"
    enabled        = true
  }
}
