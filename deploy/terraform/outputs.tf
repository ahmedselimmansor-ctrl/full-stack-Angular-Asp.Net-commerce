output "cluster_name" {
  description = "GKE cluster name, for `gcloud container clusters get-credentials`."
  value       = google_container_cluster.sama.name
}

output "cluster_location" {
  value = google_container_cluster.sama.location
}

output "ingress_ip" {
  description = "Static address to point the storefront's DNS A records at."
  value       = google_compute_global_address.ingress.address
}

output "artifact_registry" {
  description = "Image prefix for the Kustomize `images:` replacements."
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.containers.repository_id}"
}

output "database_instance" {
  value = google_sql_database_instance.postgres.name
}

output "database_private_ip" {
  value     = google_sql_database_instance.postgres.private_ip_address
  sensitive = true
}

output "redis_host" {
  value     = google_redis_instance.sama.host
  sensitive = true
}

output "media_bucket" {
  value = google_storage_bucket.media.name
}

output "workload_service_account" {
  description = "Annotate the Kubernetes ServiceAccount with this for Workload Identity."
  value       = google_service_account.workload.email
}

output "deployer_service_account" {
  description = "Impersonated by GitHub Actions through Workload Identity Federation."
  value       = google_service_account.deployer.email
}

output "workload_identity_provider" {
  description = "Full provider resource name for the google-github-actions/auth step."
  value       = google_iam_workload_identity_pool_provider.github.name
}

output "dns_records_required" {
  description = "DNS that must exist before the managed certificate can be issued."
  value = {
    (var.domain)          = google_compute_global_address.ingress.address
    ("www.${var.domain}") = google_compute_global_address.ingress.address
    (var.api_domain)      = google_compute_global_address.ingress.address
  }
}
