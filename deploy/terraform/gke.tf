# GKE Autopilot: Google manages nodes, and workloads are billed on requested resources rather
# than on provisioned machines. It also enforces a hardened baseline by default — no privileged
# containers, no host namespaces, Workload Identity always on — which is exactly the posture
# the manifests already assume.

resource "google_container_cluster" "sama" {
  name     = local.name
  location = var.region

  enable_autopilot = true

  network    = google_compute_network.vpc.id
  subnetwork = google_compute_subnetwork.gke.id

  deletion_protection = var.deletion_protection

  ip_allocation_policy {
    cluster_secondary_range_name  = "pods"
    services_secondary_range_name = "services"
  }

  private_cluster_config {
    # Nodes have no public addresses.
    enable_private_nodes = true

    # The control plane keeps a public endpoint, restricted below to known networks. A fully
    # private endpoint would require a bastion or a VPN for every kubectl call and for CI.
    enable_private_endpoint = false
    master_ipv4_cidr_block  = "172.16.0.0/28"
  }

  master_authorized_networks_config {
    dynamic "cidr_blocks" {
      for_each = var.allowed_admin_cidrs
      content {
        cidr_block   = cidr_blocks.value
        display_name = "admin"
      }
    }
  }

  workload_identity_config {
    workload_pool = "${var.project_id}.svc.id.goog"
  }

  release_channel {
    # Regular trades the newest features for versions that have had time to be exercised.
    channel = "REGULAR"
  }

  maintenance_policy {
    recurring_window {
      # Low-traffic window, expressed in UTC.
      start_time = "2024-01-01T01:00:00Z"
      end_time   = "2024-01-01T05:00:00Z"
      recurrence = "FREQ=WEEKLY;BYDAY=TU,WE,TH"
    }
  }

  # Scans running workloads for known vulnerabilities and misconfiguration.
  security_posture_config {
    mode               = "BASIC"
    vulnerability_mode = "VULNERABILITY_BASIC"
  }

  logging_config {
    enable_components = ["SYSTEM_COMPONENTS", "WORKLOADS"]
  }

  monitoring_config {
    enable_components = ["SYSTEM_COMPONENTS"]
    managed_prometheus {
      enabled = true
    }
  }

  resource_labels = local.labels

  depends_on = [
    google_project_service.required,
    google_service_networking_connection.private_services,
  ]
}
