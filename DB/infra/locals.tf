locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Prefixo dos recursos deste repo (RDS, subnet group, SG e tabela DynamoDB), para nao colidir com
  # os do DB/infra dos repos MANAGEMENT e OS-SERVICE. As buscas da rede continuam por var.project_name.
  name_prefix = "${var.project_name}-billing"
}
