# Sama — Operations

How Sama behaves when things go wrong, and what to do about it.

---

## 1. Health and readiness

Every service exposes two probes, and the distinction matters.

| Endpoint | Checks | Failure means |
|---|---|---|
| `/health/live` | Nothing external | The process is wedged; Kubernetes restarts the pod |
| `/health/ready` | PostgreSQL, Redis | Remove this pod from the load balancer; do **not** restart it |

Liveness is deliberately dependency-free. A liveness probe that tested Redis would restart
every pod in the fleet during a Memorystore failover, turning a degraded cache into a full
outage. Readiness is the one that tests dependencies, because the correct response to a
transient dependency blip is to stop sending traffic, not to kill the process.

---

## 2. Deploys

CI runs on a push to `main`: build → test → scan → push images → migrate → roll out.

The ordering is load-bearing. Schema migrations run as a **Job that must complete** before any
new pod starts. Applying migrations from application startup instead would have every replica
race to run them, and would present a failed migration as a crash-looping deployment rather
than a failed job with a readable log.

`maxUnavailable: 0` means a failed rollout leaves the previous ReplicaSet serving. The pipeline
waits on `rollout status`, so a bad deploy fails the pipeline rather than silently half-landing.

**Rolling back.** Images are tagged with the commit SHA, so a rollback is a re-point:

```bash
kubectl -n sama set image deployment/sama-catalog app=REGISTRY/catalog:PREVIOUS_SHA
```

Migrations are not automatically reversed. A schema change must be backwards-compatible with
the previous application version — expand first, contract in a later release — or a rollback
is not actually available.

---

## 3. Failure modes

### Redis is unavailable

| Component | Behaviour |
|---|---|
| Catalogue cache | Misses fall through to PostgreSQL. Slower, still correct. |
| Baskets | **Unavailable.** Redis is their system of record. |
| Rate limiting (services) | Fails **open** — traffic flows unthrottled |
| Rate limiting (gateway) | Fails **closed** — requests are rejected |
| Idempotency | Unavailable, so checkout rejects rather than risk a double charge |

The asymmetry is intentional: the internet-facing tier must not silently lose all throttling,
while an internal service should not stop a shop trading over a cache outage.

Readiness fails, so pods leave the load balancer. They do not restart.

### PostgreSQL is unavailable

Reads served from cache continue until the TTL expires. Writes fail with 503. The outbox
retains unpublished events and drains once the database returns — no events are lost.

Cloud SQL failover is typically under a minute. Connection resiliency retries three times with
backoff, so a failover usually surfaces as a latency bump rather than errors.

### Pub/Sub is unavailable

The saga stalls; nothing is lost. Orders sit in `Pending`, the outbox accumulates, and both
drain when the broker returns. HTTP traffic is unaffected — the subscriber supervises itself
and retries with backoff rather than taking the host down with it.

**Watch for:** unprocessed outbox rows climbing.

```sql
SELECT count(*) FROM ordering.outbox_messages WHERE processed_at IS NULL;
```

### A message keeps failing

After 10 attempts a Pub/Sub message goes to the dead-letter topic, and an outbox row is
**parked** — `next_attempt_at` set to null so it leaves the retry query but stays visible with
its last error.

```sql
SELECT id, type, attempts, last_error
FROM ordering.outbox_messages
WHERE processed_at IS NULL AND next_attempt_at IS NULL;
```

To replay after fixing the cause:

```sql
UPDATE ordering.outbox_messages SET next_attempt_at = now(), attempts = 0 WHERE id = '...';
```

### An order is stuck in Pending or AwaitingPayment

The saga did not complete. In order of likelihood:

1. **Outbox not draining** — check for parked rows as above.
2. **Subscription missing** — `gcloud pubsub subscriptions list`. Locally, the emulator holds
   topics in memory and loses them on restart; `scripts/provision-pubsub.sh` recreates them.
3. **A consumer is failing** — check the dead-letter subscription.

An order stuck in `AwaitingPayment` has stock held against it. Cancelling it releases that
stock through the normal compensating path:

```bash
curl -X POST "$API/api/v1/orders/$ORDER_ID/cancel" -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' -d '{"reason":"Stuck saga, cancelled by operations"}'
```

### Payment succeeded but the order was already cancelled

Money was taken against a dead order. The handler deliberately **throws** rather than swallowing
this, so the message dead-letters and a human sees it. It needs a refund:

```bash
curl -X POST "$API/api/v1/payments/$PAYMENT_ID/refund" -H "Authorization: Bearer $ADMIN_TOKEN"
```

---

## 4. Scaling

HPAs scale on CPU at 65% utilisation — not 80%, because scaling must begin before existing
pods saturate: a new pod is not serving until its startup probe passes.

Scale-up is fast (30 s stabilisation, up to 100% growth). Scale-down is slow (300 s), because
retreating into a lull that is about to end costs another cold start.

| Service | min | max | Scales on |
|---|---|---|---|
| gateway | 3 | 30 | All traffic lands here |
| catalog | 2 | 20 | Read-heavy, cache-fronted |
| basket | 2 | 20 | High write rate, no DB |
| ordering | 2 | 15 | Saga plus outbox dispatcher |
| identity | 2 | 10 | Password hashing is CPU-bound |
| inventory, payment | 2 | 10 | Mostly event consumers |

**Database connections are the real ceiling.** Each service pools up to 20 connections; at
maximum replicas that is well within a `db-custom-4-15360` instance's limit, but raising
`maxReplicas` without checking that arithmetic is how a scale-up event becomes an outage.

---

## 5. Observability

Traces and metrics go to Cloud Trace and Cloud Monitoring over OTLP via the collector. Logs are
structured JSON on stdout, shaped for Cloud Logging: `severity` drives the console filter and
`logging.googleapis.com/trace` links each entry to its trace, so a request's logs and spans
appear together.

Sampling is 10% in production (5% at higher volume), parent-based so a distributed trace is
either wholly sampled or wholly absent — which is what makes the sampled ones readable.

**Worth alerting on:**

| Signal | Why |
|---|---|
| Unprocessed outbox rows > 100 for 5 min | The saga has stalled |
| Parked outbox rows > 0 | A message needs human attention |
| Dead-letter subscription depth > 0 | A consumer is failing repeatedly |
| Orders in `Pending` older than 10 min | Checkout is silently broken |
| 5xx rate > 1% | Something is wrong |
| p99 latency > 2 s at the gateway | Something is slow |
| Cloud SQL CPU > 80% | Approaching the connection/throughput ceiling |
| Certificate expiry < 14 days | Managed certs renew automatically; failure is silent |

---

## 6. Backups and recovery

Cloud SQL takes daily backups with point-in-time recovery over a 7-day window of write-ahead
logs, which is what makes an accidental bulk update survivable.

```bash
gcloud sql instances clone SOURCE RECOVERED --point-in-time='2026-08-25T10:30:00Z'
```

Recover into a **clone**, never in place — recovering over the live instance destroys the
window you would need for a second attempt.

Redis is not backed up, by design. It holds caches, baskets, rate-limit counters and
idempotency records: a lost basket is an annoyance, and everything else rebuilds. Anything that
would be a financial incident if lost is in PostgreSQL.

---

## 7. Routine tasks

**Rotate the signing key.** Add the new key to `Signing:Keys` and set `Signing:ActiveKeyId` to
it, leaving the old key in the list. Tokens signed by the old key keep validating until they
expire; remove it after 30 days.

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out new-signing-key.pem
gcloud secrets versions add sama-identity-signing-keys-0-privatekeypem --data-file=new-signing-key.pem
```

Other services need no redeploy — they re-fetch JWKS on their own.

**Rotate a database password.** Update the Cloud SQL user, add a Secret Manager version.
External Secrets re-reads hourly; restart the deployment to pick it up sooner.

**Purge the CDN** after a media change:

```bash
gcloud compute url-maps invalidate-cdn-cache sama-prod --path="/media/*"
```

Catalogue API responses do not need this — they carry `Cache-Tag` headers and are invalidated
by tag when a product changes.
