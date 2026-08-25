# Pub/Sub carries integration events between services. One topic per publishing service; each
# consumer attaches a filtered subscription for only the event types it handles.

resource "google_pubsub_topic" "service" {
  for_each = toset(local.pubsub_topics)

  name   = "${local.name}-${each.key}"
  labels = local.labels

  message_retention_duration = "604800s" # 7 days

  depends_on = [google_project_service.required]
}

# Messages a consumer repeatedly fails to process land here instead of cycling forever. A
# poison message that blocks its subscription would stall the saga for every order behind it.
resource "google_pubsub_topic" "dead_letter" {
  name   = "${local.name}-dead-letter"
  labels = local.labels

  message_retention_duration = "2592000s" # 30 days — long enough to notice and replay
}

locals {
  # consumer -> { topic, filter }. The filters mirror the handlers each service registers;
  # anything else is never delivered, so a consumer is not woken for events it would only ack
  # and discard.
  subscriptions = {
    "inventory-ordering" = {
      topic  = "ordering"
      filter = "attributes.eventType = \"ordering.order.placed.v1\" OR attributes.eventType = \"ordering.order.cancelled.v1\""
    }
    "payment-ordering" = {
      topic  = "ordering"
      filter = "attributes.eventType = \"ordering.payment.requested.v1\""
    }
    "basket-ordering" = {
      topic  = "ordering"
      filter = "attributes.eventType = \"ordering.order.confirmed.v1\""
    }
    "ordering-inventory" = {
      topic  = "inventory"
      filter = ""
    }
    "ordering-payment" = {
      topic  = "payment"
      filter = ""
    }
  }
}

resource "google_pubsub_subscription" "service" {
  for_each = local.subscriptions

  name  = "${local.name}-${each.key}"
  topic = google_pubsub_topic.service[each.value.topic].id

  filter = each.value.filter

  # Must exceed the slowest handler. A deadline shorter than the work means the message is
  # redelivered while the first attempt is still running, doubling the work rather than
  # recovering from anything.
  ack_deadline_seconds = 60

  message_retention_duration = "604800s"
  retain_acked_messages      = false

  # Keeps a tenant's events in sequence without serialising the whole topic.
  enable_message_ordering = true

  expiration_policy {
    # Never expire. The default deletes a subscription after 31 days of inactivity, which for
    # a low-volume topic would silently drop events the first time traffic resumed.
    ttl = ""
  }

  retry_policy {
    minimum_backoff = "10s"
    maximum_backoff = "600s"
  }

  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 10
  }

  labels = local.labels
}

# A subscription on the dead-letter topic so the messages are retained and inspectable rather
# than simply accumulating unread.
resource "google_pubsub_subscription" "dead_letter" {
  name  = "${local.name}-dead-letter"
  topic = google_pubsub_topic.dead_letter.id

  ack_deadline_seconds       = 60
  message_retention_duration = "2592000s"
  retain_acked_messages      = true

  expiration_policy {
    ttl = ""
  }

  labels = local.labels
}

# Pub/Sub's own service agent needs permission to publish into the dead-letter topic and to
# acknowledge on the source subscriptions; without these, dead-lettering silently fails and
# poison messages keep redelivering.
data "google_project" "current" {}

locals {
  pubsub_agent = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-pubsub.iam.gserviceaccount.com"
}

resource "google_pubsub_topic_iam_member" "dead_letter_publisher" {
  topic  = google_pubsub_topic.dead_letter.name
  role   = "roles/pubsub.publisher"
  member = local.pubsub_agent
}

resource "google_pubsub_subscription_iam_member" "dead_letter_subscriber" {
  for_each = local.subscriptions

  subscription = google_pubsub_subscription.service[each.key].name
  role         = "roles/pubsub.subscriber"
  member       = local.pubsub_agent
}
