# Private VPC. Nothing in Sama gets a public IP: pods reach the internet through Cloud NAT,
# and the only inbound path is the load balancer.

resource "google_compute_network" "vpc" {
  name                    = local.name
  auto_create_subnetworks = false
  routing_mode            = "REGIONAL"

  depends_on = [google_project_service.required]
}

resource "google_compute_subnetwork" "gke" {
  name          = "${local.name}-gke"
  network       = google_compute_network.vpc.id
  region        = var.region
  ip_cidr_range = "10.10.0.0/20"

  # Required for pods to reach Google APIs (Pub/Sub, Secret Manager) without a public IP or a
  # NAT hop — traffic stays on Google's network.
  private_ip_google_access = true

  # Secondary ranges for the cluster's pods and services. Sized generously because they cannot
  # be resized after the cluster is created without rebuilding it.
  secondary_ip_range {
    range_name    = "pods"
    ip_cidr_range = "10.20.0.0/14"
  }

  secondary_ip_range {
    range_name    = "services"
    ip_cidr_range = "10.24.0.0/20"
  }

  log_config {
    aggregation_interval = "INTERVAL_10_MIN"
    flow_sampling        = 0.1
    metadata             = "INCLUDE_ALL_METADATA"
  }
}

# --- Egress ---------------------------------------------------------------------------------
# Cloud NAT gives pods outbound internet access (package pulls, the payment acquirer) without
# any of them being reachable from outside.

resource "google_compute_router" "nat" {
  name    = "${local.name}-nat-router"
  network = google_compute_network.vpc.id
  region  = var.region
}

resource "google_compute_router_nat" "nat" {
  name   = "${local.name}-nat"
  router = google_compute_router.nat.name
  region = var.region

  nat_ip_allocate_option             = "AUTO_ONLY"
  source_subnetwork_ip_ranges_to_nat = "ALL_SUBNETWORKS_ALL_IP_RANGES"

  log_config {
    enable = true
    filter = "ERRORS_ONLY"
  }
}

# --- Private services access ------------------------------------------------------------
# Cloud SQL and Memorystore are attached to the VPC through a peered range, so they are
# reachable on private addresses only and never expose a public endpoint.

resource "google_compute_global_address" "private_services" {
  name          = "${local.name}-private-services"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 20
  network       = google_compute_network.vpc.id
}

resource "google_service_networking_connection" "private_services" {
  network                 = google_compute_network.vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_services.name]

  depends_on = [google_project_service.required]
}

# The static address the storefront's DNS points at.
resource "google_compute_global_address" "ingress" {
  name = "${local.name}-ingress-ip"
}
