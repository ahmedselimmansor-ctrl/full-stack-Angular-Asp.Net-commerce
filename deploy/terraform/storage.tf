# Product media. Served to browsers through Cloud CDN, never proxied by a service — an image
# request should not consume a request slot on the catalogue.

resource "google_storage_bucket" "media" {
  name          = "${local.name}-media-${data.google_project.current.number}"
  location      = var.region
  storage_class = "STANDARD"

  # Objects are content-addressed by the upload pipeline, so versioning would retain
  # unreferenced copies indefinitely for no benefit.
  versioning {
    enabled = false
  }

  uniform_bucket_level_access = true

  # No object may be made public individually; access is granted at the bucket level to the
  # CDN backend below, which is the only intended reader.
  public_access_prevention = "inherited"

  cors {
    origin          = ["https://${var.domain}", "https://www.${var.domain}"]
    method          = ["GET", "HEAD"]
    response_header = ["Content-Type", "ETag"]
    max_age_seconds = 3600
  }

  lifecycle_rule {
    condition {
      age = 30
    }
    action {
      # Media is written once and read for years; colder storage after a month is materially
      # cheaper and the CDN absorbs the higher first-byte latency.
      type          = "SetStorageClass"
      storage_class = "NEARLINE"
    }
  }

  labels = local.labels
}

# Read access for anyone, mediated by the CDN. Product imagery is public by nature — it is on
# the storefront — and gating it behind signed URLs would make it uncacheable.
resource "google_storage_bucket_iam_member" "public_read" {
  bucket = google_storage_bucket.media.name
  role   = "roles/storage.objectViewer"
  member = "allUsers"
}

resource "google_compute_backend_bucket" "media" {
  name        = "${local.name}-media"
  bucket_name = google_storage_bucket.media.name
  enable_cdn  = true

  cdn_policy {
    cache_mode       = "CACHE_ALL_STATIC"
    client_ttl       = 3600
    default_ttl      = 86400
    max_ttl          = 31536000
    negative_caching = true

    # Collapses a stampede of misses for the same object into a single origin fetch.
    request_coalescing = true

    # Keeps serving the old object while a refresh happens, so an origin blip is invisible.
    serve_while_stale = 86400

    cache_key_policy {
      include_http_headers   = []
      query_string_whitelist = []
    }
  }
}

# Artifact Registry holds the service images the cluster pulls.
resource "google_artifact_registry_repository" "containers" {
  location      = var.region
  repository_id = "sama"
  format        = "DOCKER"
  description   = "Sama service container images"

  docker_config {
    immutable_tags = local.is_prod
  }

  cleanup_policies {
    id     = "keep-recent"
    action = "KEEP"
    most_recent_versions {
      keep_count = 20
    }
  }

  cleanup_policies {
    id     = "drop-untagged"
    action = "DELETE"
    condition {
      tag_state  = "UNTAGGED"
      older_than = "604800s"
    }
  }

  labels = local.labels

  depends_on = [google_project_service.required]
}
