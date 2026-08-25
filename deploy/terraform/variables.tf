variable "project_id" {
  description = "Google Cloud project that will own every resource here."
  type        = string
}

variable "region" {
  description = "Primary region. Regional resources (GKE, Cloud SQL HA, Memorystore) live here."
  type        = string
  default     = "europe-west1"
}

variable "environment" {
  description = "Environment name; prefixes resource names and separates Pub/Sub topics."
  type        = string

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment must be one of: dev, staging, prod."
  }
}

variable "domain" {
  description = "Apex domain for the storefront, e.g. sama.example."
  type        = string
}

variable "api_domain" {
  description = "Hostname the API is served on, e.g. api.sama.example."
  type        = string
}

# --- Sizing -------------------------------------------------------------------------------
# Defaults are production-shaped. Overridden downwards in dev.tfvars, because a regional HA
# Cloud SQL instance and a standard-tier Memorystore are a lot of money for a test environment.

variable "database_tier" {
  description = "Cloud SQL machine type."
  type        = string
  default     = "db-custom-4-15360"
}

variable "database_availability_type" {
  description = "REGIONAL gives a synchronous standby and automatic failover; ZONAL does not."
  type        = string
  default     = "REGIONAL"
}

variable "database_disk_size_gb" {
  type    = number
  default = 100
}

variable "redis_tier" {
  description = "STANDARD_HA runs a replica with automatic failover; BASIC is a single node."
  type        = string
  default     = "STANDARD_HA"
}

variable "redis_memory_size_gb" {
  type    = number
  default = 10
}

variable "deletion_protection" {
  description = "Blocks `terraform destroy` from removing the database and the cluster."
  type        = bool
  default     = true
}

variable "labels" {
  description = "Applied to every resource that supports labels, for cost attribution."
  type        = map(string)
  default     = {}
}

# --- Edge policy --------------------------------------------------------------------------

variable "edge_rate_limit_per_minute" {
  description = <<-EOT
    Cloud Armor per-IP request ceiling. This is the outermost of three layers and is
    deliberately loose: it exists to absorb volumetric abuse before it costs a pod, while the
    gateway and the services apply the per-user and per-endpoint limits that actually shape
    behaviour.
  EOT
  type        = number
  default     = 1200
}

variable "allowed_admin_cidrs" {
  description = "Source ranges permitted to reach back-office paths. Empty means no IP allow-list."
  type        = list(string)
  default     = []
}

variable "github_repository" {
  description = "owner/repo permitted to federate into the deployer service account."
  type        = string
  default     = "ahmedselimmansor-ctrl/sama-commerce"
}
