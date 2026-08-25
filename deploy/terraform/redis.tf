# Memorystore for Redis, backing the cache, baskets, rate-limit counters, idempotency records
# and the token denylist.
#
# Note what is and is not durable here: a lost basket is an annoyance, whereas a lost order is
# a financial incident — which is why baskets live here and orders live in PostgreSQL.

resource "google_redis_instance" "sama" {
  name           = local.name
  tier           = var.redis_tier
  memory_size_gb = var.redis_memory_size_gb
  region         = var.region

  redis_version = "REDIS_7_2"

  authorized_network      = google_compute_network.vpc.id
  connect_mode            = "PRIVATE_SERVICE_ACCESS"
  transit_encryption_mode = "SERVER_AUTHENTICATION"

  # Without AUTH, anything that can reach the VPC can read every session token and basket.
  auth_enabled = true

  redis_configs = {
    # Evict the least-recently-used key when memory fills, rather than refusing writes.
    # Refusing writes would take out rate limiting and idempotency, which fail far worse than
    # a cache miss.
    maxmemory-policy = "allkeys-lru"
  }

  maintenance_policy {
    weekly_maintenance_window {
      day = "WEDNESDAY"
      start_time {
        hours   = 3
        minutes = 0
      }
    }
  }

  labels = local.labels

  depends_on = [google_service_networking_connection.private_services]
}
