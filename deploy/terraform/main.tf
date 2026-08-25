locals {
  name    = "sama-${var.environment}"
  is_prod = var.environment == "prod"

  labels = merge(
    {
      application = "sama"
      environment = var.environment
      managed-by  = "terraform"
    },
    var.labels,
  )

  # One topic per publishing service. Subscribers attach attribute filters for the event types
  # they handle, which keeps the topic count flat as the event catalogue grows.
  pubsub_topics = ["identity", "catalog", "basket", "ordering", "inventory", "payment"]

  # Each Sama service connects with its own database role, so a compromise of one service
  # cannot read another's schema.
  services = ["identity", "catalog", "basket", "ordering", "inventory", "payment"]

  # Services that own a PostgreSQL schema. Basket is Redis-only and deliberately absent.
  database_services = ["identity", "catalog", "ordering", "inventory", "payment"]
}

# Enabling APIs in Terraform rather than by hand means a fresh project can be built from
# nothing by a single apply.
resource "google_project_service" "required" {
  for_each = toset([
    "compute.googleapis.com",
    "container.googleapis.com",
    "sqladmin.googleapis.com",
    "redis.googleapis.com",
    "pubsub.googleapis.com",
    "secretmanager.googleapis.com",
    "artifactregistry.googleapis.com",
    "servicenetworking.googleapis.com",
    "cloudtrace.googleapis.com",
    "monitoring.googleapis.com",
    "logging.googleapis.com",
    "certificatemanager.googleapis.com",
    "iamcredentials.googleapis.com",
  ])

  service = each.value

  # Leaving an API enabled costs nothing, and disabling one on destroy can break unrelated
  # resources that happen to share the project.
  disable_on_destroy = false
}
