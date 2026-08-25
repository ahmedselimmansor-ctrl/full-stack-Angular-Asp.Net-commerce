terraform {
  required_version = ">= 1.9"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
    google-beta = {
      source  = "hashicorp/google-beta"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # State lives in GCS with versioning and object locking, not on a laptop. Terraform's
  # GCS backend takes a lock object for the duration of an apply, so two engineers cannot
  # race each other into a half-applied plan.
  backend "gcs" {
    # Supplied by `terraform init -backend-config=...` so the same code serves every environment.
    prefix = "sama/state"
  }
}

provider "google" {
  project = var.project_id
  region  = var.region
}

provider "google-beta" {
  project = var.project_id
  region  = var.region
}
