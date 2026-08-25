# Cloud Armor: the outermost of three rate-limiting layers, plus a WAF.
#
# Dropping abuse here is by far the cheapest place to do it — the request never reaches a pod,
# a database connection, or the Redis limiter. The gateway and the services then apply the
# per-user and per-endpoint limits that actually shape behaviour.

resource "google_compute_security_policy" "api" {
  name        = "${local.name}-api-armor"
  description = "Edge protection for the Sama API"

  # Learns each backend's normal traffic shape and mitigates volumetric L7 attacks.
  adaptive_protection_config {
    layer_7_ddos_defense_config {
      enable          = true
      rule_visibility = "STANDARD"
    }
  }

  # --- Per-IP throttling ------------------------------------------------------------------
  rule {
    action   = "throttle"
    priority = 1000

    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }

    rate_limit_options {
      conform_action = "allow"
      exceed_action  = "deny(429)"

      enforce_on_key = "IP"

      rate_limit_threshold {
        count        = var.edge_rate_limit_per_minute
        interval_sec = 60
      }
    }

    description = "Volumetric ceiling per source IP"
  }

  # --- Automatic banning ------------------------------------------------------------------
  rule {
    action   = "rate_based_ban"
    priority = 1100

    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }

    rate_limit_options {
      conform_action = "allow"
      exceed_action  = "deny(429)"
      enforce_on_key = "IP"

      # Sustained abuse well above the throttle earns a temporary ban, so a persistent
      # attacker stops consuming evaluation capacity entirely.
      rate_limit_threshold {
        count        = var.edge_rate_limit_per_minute * 5
        interval_sec = 60
      }

      ban_duration_sec = 600
    }

    description = "Ban sources that sustain heavy abuse"
  }

  # --- OWASP preconfigured WAF rules ------------------------------------------------------
  # Sensitivity 1 is the conservative tier. Higher tiers catch more but produce false
  # positives on legitimate payloads — a product description containing SQL-like text, for
  # instance — and a WAF that blocks real customers is worse than one that blocks less.
  rule {
    action   = "deny(403)"
    priority = 2000
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('sqli-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "SQL injection"
  }

  rule {
    action   = "deny(403)"
    priority = 2010
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('xss-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "Cross-site scripting"
  }

  rule {
    action   = "deny(403)"
    priority = 2020
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('lfi-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "Local file inclusion"
  }

  rule {
    action   = "deny(403)"
    priority = 2030
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('rce-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "Remote code execution"
  }

  rule {
    action   = "deny(403)"
    priority = 2040
    match {
      expr {
        expression = "evaluatePreconfiguredWaf('scannerdetection-v33-stable', {'sensitivity': 1})"
      }
    }
    description = "Vulnerability scanners"
  }

  # --- Default --------------------------------------------------------------------------
  rule {
    action   = "allow"
    priority = 2147483647
    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }
    description = "Default allow"
  }
}

resource "google_compute_security_policy" "web" {
  name        = "${local.name}-web-armor"
  description = "Edge protection for the storefront bundle"

  adaptive_protection_config {
    layer_7_ddos_defense_config {
      enable = true
    }
  }

  rule {
    action   = "throttle"
    priority = 1000
    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }
    rate_limit_options {
      conform_action = "allow"
      exceed_action  = "deny(429)"
      enforce_on_key = "IP"
      rate_limit_threshold {
        # Static assets are cheap and cached at the edge, so this is far looser than the API's.
        count        = var.edge_rate_limit_per_minute * 4
        interval_sec = 60
      }
    }
    description = "Volumetric ceiling for static assets"
  }

  rule {
    action   = "allow"
    priority = 2147483647
    match {
      versioned_expr = "SRC_IPS_V1"
      config {
        src_ip_ranges = ["*"]
      }
    }
    description = "Default allow"
  }
}

# Modern TLS only. RESTRICTED drops the cipher suites and protocol versions that no current
# client needs and that weaken the handshake for everyone else.
resource "google_compute_ssl_policy" "modern" {
  name            = "${local.name}-modern-tls"
  profile         = "RESTRICTED"
  min_tls_version = "TLS_1_2"
}
