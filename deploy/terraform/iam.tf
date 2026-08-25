# Workload Identity: pods authenticate to Google Cloud as this service account. No JSON key
# exists, so there is nothing to leak, rotate, or accidentally commit.

resource "google_service_account" "workload" {
  account_id   = "${local.name}-workload"
  display_name = "Sama workloads (${var.environment})"
  description  = "Identity assumed by Sama pods via Workload Identity"
}

# Binds the Kubernetes service account `sama/sama-workload` to the Google service account.
resource "google_service_account_iam_member" "workload_identity" {
  service_account_id = google_service_account.workload.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "serviceAccount:${var.project_id}.svc.id.goog[${local.is_prod ? "sama" : "sama-${var.environment}"}/sama-workload]"
}

# Least privilege: publish and subscribe, read the secrets it needs, write telemetry. Notably
# absent are any admin roles — the workload cannot create topics, alter IAM, or read the
# database's own configuration.
resource "google_project_iam_member" "workload" {
  for_each = toset([
    "roles/pubsub.publisher",
    "roles/pubsub.subscriber",
    "roles/cloudtrace.agent",
    "roles/monitoring.metricWriter",
    "roles/logging.logWriter",
  ])

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.workload.email}"
}

# Secret access is granted per secret rather than project-wide, so the workload can read only
# the values it is meant to.
resource "google_secret_manager_secret_iam_member" "workload" {
  for_each = google_secret_manager_secret.sama

  secret_id = each.value.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.workload.email}"
}

resource "google_storage_bucket_iam_member" "media_writer" {
  bucket = google_storage_bucket.media.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.workload.email}"
}

# --- Deployment identity for CI -----------------------------------------------------------
# CI authenticates through Workload Identity Federation from GitHub Actions, so no
# long-lived key is issued to the pipeline either.

resource "google_service_account" "deployer" {
  account_id   = "${local.name}-deployer"
  display_name = "Sama CI deployer (${var.environment})"
}

resource "google_project_iam_member" "deployer" {
  for_each = toset([
    "roles/container.developer",
    "roles/artifactregistry.writer",
  ])

  project = var.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.deployer.email}"
}

resource "google_iam_workload_identity_pool" "github" {
  workload_identity_pool_id = "${local.name}-github"
  display_name              = "GitHub Actions"
}

resource "google_iam_workload_identity_pool_provider" "github" {
  workload_identity_pool_id          = google_iam_workload_identity_pool.github.workload_identity_pool_id
  workload_identity_pool_provider_id = "github"

  attribute_mapping = {
    "google.subject"       = "assertion.sub"
    "attribute.repository" = "assertion.repository"
    "attribute.ref"        = "assertion.ref"
  }

  # Without this condition any GitHub repository in the world could mint tokens for this
  # project. It restricts federation to the Sama repository alone.
  attribute_condition = "assertion.repository == '${var.github_repository}'"

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

resource "google_service_account_iam_member" "deployer_federation" {
  service_account_id = google_service_account.deployer.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github.name}/attribute.repository/${var.github_repository}"
}
