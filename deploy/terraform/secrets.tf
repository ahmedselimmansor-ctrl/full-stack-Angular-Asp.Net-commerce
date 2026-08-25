# Secret Manager holds every credential. Terraform creates the containers and, where it
# generated the value itself (database passwords, the Redis auth string), the versions too.
#
# Values Terraform must not know — the RSA signing key, the payment acquirer's API key — are
# created empty here and populated out of band, so they never pass through a plan file or
# Terraform state.

locals {
  # Connection strings are assembled from resources Terraform already owns, so a database
  # rebuild propagates automatically instead of being copied by hand.
  database_connection_strings = {
    for service in local.database_services :
    service => join(";", [
      "Host=${google_sql_database_instance.postgres.private_ip_address}",
      "Port=5432",
      "Database=${google_sql_database.sama.name}",
      "Username=${google_sql_user.service[service].name}",
      "Password=${random_password.database[service].result}",
      "SSL Mode=Require",
      "Trust Server Certificate=false",
      # Bounded so a leak cannot exhaust the instance's connection limit and lock out the
      # other services sharing it.
      "Maximum Pool Size=20",
      "Timeout=15",
    ])
  }

  redis_connection_string = join(",", [
    "${google_redis_instance.sama.host}:${google_redis_instance.sama.port}",
    "password=${google_redis_instance.sama.auth_string}",
    "ssl=true",
    "abortConnect=false",
  ])

  # secret name -> value. A null value means Terraform creates the container but not the
  # version, leaving the value to be supplied elsewhere.
  managed_secrets = merge(
    {
      for service, connection in local.database_connection_strings :
      "sama-${service}-connectionstrings-postgres" => connection
    },
    {
      for service in local.services :
      "sama-${service}-redis-connectionstring" => local.redis_connection_string
    },
    {
      "sama-gateway-redis-connectionstring"        = local.redis_connection_string
      "sama-migrations-connectionstrings-postgres" = local.database_connection_strings["identity"]
    },
    {
      # Populated out of band; Terraform never sees these.
      "sama-identity-signing-activekeyid"          = null
      "sama-identity-signing-keys-0-keyid"         = null
      "sama-identity-signing-keys-0-privatekeypem" = null
      "sama-payment-paymentprovider-apikey"        = null
    },
  )
}

resource "google_secret_manager_secret" "sama" {
  for_each = local.managed_secrets

  secret_id = each.key

  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }

  labels = local.labels

  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "sama" {
  # Only the secrets whose values Terraform generated.
  for_each = { for name, value in local.managed_secrets : name => value if value != null }

  secret      = google_secret_manager_secret.sama[each.key].id
  secret_data = each.value
}
