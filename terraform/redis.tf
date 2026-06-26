# ---------------------------------------------------------------------------
# Azure Cache for Redis
# ---------------------------------------------------------------------------
resource "azurerm_redis_cache" "main" {
  name                = "redis-${var.project_name}-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  capacity            = var.redis_capacity
  family              = var.redis_sku == "Premium" ? "P" : "C"
  sku_name            = var.redis_sku
  enable_non_ssl_port = false # TODO: rename to non_ssl_port_enabled in azurerm v4.0
  minimum_tls_version = "1.2"
  redis_version       = "6"

  redis_configuration {
    maxmemory_policy = "allkeys-lru"
  }

  tags = azurerm_resource_group.main.tags
}
