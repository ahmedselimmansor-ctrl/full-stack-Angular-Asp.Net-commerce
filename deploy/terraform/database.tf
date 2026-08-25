# Cloud SQL for PostgreSQL, private IP only.
#
# One instance holds a schema per service, each owned by its own role. That is cheaper than an
# instance per service while keeping the isolation that matters, and a schema can be lifted to
# a dedicated instance later without touching application code — only its connection string.

resource "random_id" "database_suffix" {
  # Cloud SQL reserves a deleted instance name for about a week. A suffix means a rebuild is
  # not blocked waiting for the old name to free up.
  byte_length = 4
}

resource "google_sql_database_instance" "postgres" {
  name             = "${local.name}-pg-${random_id.database_suffix.hex}"
  database_version = "POSTGRES_16"
  region           = var.region

  deletion_protection = var.deletion_protection

  settings {
    tier              = var.database_tier
    availability_type = var.database_availability_type
    disk_type         = "PD_SSD"
    disk_size         = var.database_disk_size_gb

    # Grows the disk before it fills. Running out of disk on a database is an outage that
    # takes manual intervention to clear.
    disk_autoresize       = true
    disk_autoresize_limit = var.database_disk_size_gb * 4

    ip_configuration {
      # No public IP. The only route in is the peered VPC range.
      ipv4_enabled                                  = false
      private_network                               = google_compute_network.vpc.id
      enable_private_path_for_google_cloud_services = true
      ssl_mode                                      = "ENCRYPTED_ONLY"
    }

    backup_configuration {
      enabled                        = true
      start_time                     = "02:00"
      location                       = var.region
      point_in_time_recovery_enabled = true

      # Seven days of WAL supports recovery to any second in the last week, which is what
      # makes an accidental bulk update survivable.
      transaction_log_retention_days = 7

      backup_retention_settings {
        retained_backups = local.is_prod ? 30 : 7
        retention_unit   = "COUNT"
      }
    }

    maintenance_window {
      day          = 3
      hour         = 3
      update_track = "stable"
    }

    database_flags {
      # Logs any statement slower than a second, which is how a missing index is noticed
      # before it becomes an incident.
      name  = "log_min_duration_statement"
      value = "1000"
    }

    database_flags {
      name  = "log_checkpoints"
      value = "on"
    }

    database_flags {
      # Records the connection's application name in pg_stat_activity, so a runaway query can
      # be attributed to the service that issued it.
      name  = "log_connections"
      value = "on"
    }

    insights_config {
      query_insights_enabled  = true
      record_application_tags = true
      record_client_address   = false
    }

    user_labels = local.labels
  }

  depends_on = [google_service_networking_connection.private_services]
}

resource "google_sql_database" "sama" {
  name     = "sama"
  instance = google_sql_database_instance.postgres.name

  # Deterministic collation, so ordering does not depend on the instance's locale.
  charset   = "UTF8"
  collation = "en_US.UTF8"
}

# One role per service. Passwords are generated here and stored in Secret Manager; nobody
# needs to know them, and nothing writes them to a file.
resource "random_password" "database" {
  for_each = toset(local.database_services)

  length  = 32
  special = true

  # Excludes characters that would need escaping inside a connection string.
  override_special = "-_=+"
}

resource "google_sql_user" "service" {
  for_each = toset(local.database_services)

  name     = "sama_${each.key}"
  instance = google_sql_database_instance.postgres.name
  password = random_password.database[each.key].result
}
