# Sama Commerce — System Architecture

> Target platform: **Google Cloud Platform**. Runtime: **GKE Autopilot** (regional).
> Status: reference implementation. Every section below maps to code in this repo.

---

## 1. Context

Sama is a multi-channel commerce platform. Three clients talk to one edge:

| Client | Tech | Repo path |
|---|---|---|
| Web storefront + admin | Angular 20 (standalone, signals, SSR-ready) | `src/web/sama-web` |
| Android app | Kotlin, Jetpack Compose, Hilt | `src/mobile/sama-android` |
| Third-party / partner | REST + OAuth2 client credentials | via gateway |

All three hit a **single ingress**: Google Cloud Global External Application Load Balancer → Cloud Armor → Cloud CDN → GKE → `Sama.Gateway` (YARP).

---

## 2. Service map

```
                      ┌──────────────── Internet ────────────────┐
                      │                                          │
              ┌───────▼────────┐                        ┌────────▼────────┐
              │ Cloud DNS      │                        │  Cloud Storage  │
              └───────┬────────┘                        │ (product media) │
                      │                                 └────────┬────────┘
        ┌─────────────▼──────────────┐                           │
        │ Global External HTTPS LB   │◄──── Cloud CDN ───────────┘
        │  + Cloud Armor (WAF/DDoS)  │
        │  + Managed TLS cert        │
        └─────────────┬──────────────┘
                      │  (GCE NEG, container-native LB)
        ┌─────────────▼──────────────────────────────────────────┐
        │  GKE Autopilot — namespace: sama                       │
        │                                                        │
        │   ┌──────────────────────────────────────────────┐     │
        │   │  Sama.Gateway  (YARP)                        │     │
        │   │  authN · rate limit · CORS · headers · route │     │
        │   └───┬───────┬───────┬────────┬────────┬────────┘     │
        │       │       │       │        │        │              │
        │  ┌────▼──┐ ┌──▼────┐ ┌▼──────┐ ┌▼──────┐ ┌▼─────────┐  │
        │  │Identity│ │Catalog│ │Basket │ │Order  │ │Inventory │  │
        │  └────┬──┘ └──┬────┘ └┬──────┘ └┬──────┘ └┬─────────┘  │
        │       │       │       │         │  ┌──────▼──────┐     │
        │       │       │       │         │  │  Payment    │     │
        │       │       │       │         │  └──────┬──────┘     │
        └───────┼───────┼───────┼─────────┼─────────┼────────────┘
                │       │       │         │         │
        ┌───────▼───────▼───────┴─────────▼─────────▼──────┐
        │ Cloud SQL for PostgreSQL 16 (private IP, HA)     │
        │  schema-per-service, per-service DB role         │
        └──────────────────────────────────────────────────┘
        ┌──────────────────────────────────────────────────┐
        │ Memorystore Redis (HA)                           │
        │  cache · basket · rate limits · idempotency · lock│
        └──────────────────────────────────────────────────┘
        ┌──────────────────────────────────────────────────┐
        │ Pub/Sub — domain events (via transactional outbox)│
        └──────────────────────────────────────────────────┘
```

### 2.1 Services and ownership

| Service | Owns | Store | Sync API | Emits |
|---|---|---|---|---|
| `Sama.Gateway` | edge policy | — (Redis for limits) | all public routes | — |
| `Sama.Identity` | users, roles, permissions, refresh tokens | `identity` schema | `/api/v1/auth/*`, `/api/v1/users/*` | `user.registered` |
| `Sama.Catalog` | products, categories, brands | `catalog` schema | `/api/v1/products/*` | `product.priceChanged` |
| `Sama.Basket` | active carts | Redis (primary) | `/api/v1/basket/*` | `basket.checkedOut` |
| `Sama.Ordering` | orders, order lines, state machine | `ordering` schema | `/api/v1/orders/*` | `order.placed`, `order.cancelled` |
| `Sama.Inventory` | stock, reservations | `inventory` schema | `/api/v1/stock/*` | `stock.reserved`, `stock.rejected` |
| `Sama.Payment` | payment intents | `payment` schema | `/api/v1/payments/*` | `payment.succeeded`, `payment.failed` |

**Rule:** no service reads another service's tables. Cross-service reads are HTTP (synchronous, on the read path) or Pub/Sub events (asynchronous, on the write path).

---

## 3. The checkout saga

Checkout is the one flow that spans services. It is a **choreographed saga** with compensations, driven by Pub/Sub events written through a transactional outbox.

```
Client            Ordering          Inventory        Payment          Basket
  │                  │                  │               │               │
  ├─ POST /orders ──►│                  │               │               │
  │   Idempotency-Key│                  │               │               │
  │                  ├─ order: Pending  │               │               │
  │                  ├─ outbox: order.placed            │               │
  │◄─ 202 + orderId ─┤                  │               │               │
  │                  │                  │               │               │
  │                  └── order.placed ─►│               │               │
  │                                     ├─ reserve stock│               │
  │                     ┌─ stock.reserved               │               │
  │                  ◄──┘                               │               │
  │                  ├─ order: AwaitingPayment          │               │
  │                  ├─ outbox: payment.requested ─────►│               │
  │                                                     ├─ charge       │
  │                     ┌── payment.succeeded ──────────┤               │
  │                  ◄──┘                               │               │
  │                  ├─ order: Confirmed                │               │
  │                  ├─ outbox: order.confirmed ────────────────────────►│ clear
  │                                                                      │
  Compensations: stock.rejected → order Cancelled
                 payment.failed  → outbox: stock.release → order Cancelled
```

Every consumer is **idempotent** — handlers dedupe on `(consumer, messageId)` in Redis with a 24h TTL, so Pub/Sub at-least-once delivery is safe.

---

## 4. Cross-cutting concerns → implementation

| Requirement | Where it lives | How |
|---|---|---|
| **Rate limiting** | Cloud Armor + `Sama.Gateway` | L7 edge policy (per-IP) + Redis sliding-window per user/API key/route. See §5 |
| **Caching** | `Sama.Shared.Infrastructure` | Two-tier: in-process `HybridCache` L1 + Redis L2, stampede-protected, tag-invalidated |
| **CDN** | Cloud CDN on the LB | Static Angular bundle + product media + cacheable `GET /api/v1/products` w/ `Cache-Control` + `ETag` |
| **Scaling** | K8s HPA + Autopilot | CPU + RPS-driven HPA per service, PDBs, `topologySpreadConstraints` |
| **Load balancing** | GCLB + K8s Service | Container-native LB via NEGs (LB → pod directly, no kube-proxy hop) |
| **Auth** | `Sama.Shared.Security` | OAuth2 password + refresh, RS256 JWT w/ JWKS rotation, short-lived access tokens |
| **Permissions** | `Sama.Shared.Security` | Permission-based policies (`catalog:product:write`), not role checks |
| **Data access control** | `Sama.Shared.Infrastructure` | EF Core global query filters: tenant scope, ownership scope, soft delete |
| **Security** | everywhere | See `docs/02-security.md` |
| **Observability** | `Sama.Shared.Infrastructure` | OpenTelemetry traces/metrics → Cloud Trace, structured logs → Cloud Logging |

---

## 5. Rate limiting — layered

Three layers, each catching what the one outside it cannot see:

1. **Cloud Armor** (`deploy/terraform/modules/security/armor.tf`)
   Per-IP throttle before traffic costs you a pod. Also OWASP preconfigured WAF rules and adaptive DDoS protection. Cheapest place to drop abuse.

2. **Gateway global limiter** (`src/gateway/Sama.Gateway`)
   Redis-backed **sliding window** keyed by, in priority order: API key → user id → IP. Distributed, so it holds across replicas — an in-process limiter would let N replicas serve N× the limit.

3. **Per-endpoint policies**
   Tight buckets on the expensive/abusable routes:

   | Policy | Limit | Applies to |
   |---|---|---|
   | `auth-login` | 5 / 5 min / IP+username | `POST /auth/login` — credential stuffing |
   | `auth-register` | 3 / hour / IP | `POST /auth/register` |
   | `write` | 60 / min / user | all `POST/PUT/PATCH/DELETE` |
   | `read` | 600 / min / user | all `GET` |
   | `search` | 30 / min / user | `GET /products?q=` — hits the DB |

   Responses use `429` + `Retry-After` + `RateLimit-*` headers (RFC 9331 draft).

---

## 6. Caching strategy

| Layer | TTL | Invalidation |
|---|---|---|
| Browser | `max-age=60` on product reads | `ETag` revalidation |
| Cloud CDN | `s-maxage=300`, `stale-while-revalidate=600` | cache-tag purge on `product.priceChanged` |
| Redis L2 | 5 min product, 1 h category tree | tag-based, on write |
| In-process L1 | 30 s | `HybridCache` coordinates with L2 |

Cache-aside for reads. Writes invalidate by tag (`product:{id}`, `category:{id}`) rather than clearing keys one by one.

**Never cached:** anything under `/api/v1/basket`, `/api/v1/orders`, `/api/v1/auth` — these carry `Cache-Control: no-store` and the CDN is configured to bypass on `Authorization`.

---

## 7. Data

- **PostgreSQL 16** on Cloud SQL, private IP only, HA (regional), PITR on, automated backups.
- **Schema-per-service** on one instance to start; each service connects with **its own DB role** that has grants on only its own schema. This gives service isolation now and a clean split into separate instances later without a code change.
- Migrations are EF Core, applied by a **Kubernetes Job** on deploy — never by the app at startup (racy across replicas).
- **Redis** (Memorystore, HA) for cache, baskets, rate-limit counters, idempotency records, and distributed locks.

---

## 8. Environments

| Env | GKE | Cloud SQL | Redis | Replicas |
|---|---|---|---|---|
| dev | Autopilot, shared | `db-g1-small`, zonal | 1 GB basic | 1 |
| staging | Autopilot | `db-custom-2-7680`, regional | 4 GB standard | 2 |
| prod | Autopilot, regional | `db-custom-4-15360`, regional HA | 10 GB standard HA | 3–20 (HPA) |

Kustomize overlays in `deploy/k8s/overlays/{dev,staging,prod}`.

---

## 9. Repository layout

```
src/
  gateway/Sama.Gateway            YARP edge: auth, rate limit, routing
  services/
    Sama.Identity                 users, tokens, roles, permissions
    Sama.Catalog                  products, categories
    Sama.Basket                   Redis-backed carts
    Sama.Ordering                 orders + saga orchestration
    Sama.Inventory                stock + reservations
    Sama.Payment                  payment intents
  shared/
    Sama.Shared.Kernel            Result, Money, paging, domain primitives
    Sama.Shared.Security          JWT, permissions, current user, DAC
    Sama.Shared.Infrastructure    EF base, cache, Redis, telemetry, health
    Sama.Shared.Messaging         event contracts, outbox, Pub/Sub
  web/sama-web                    Angular storefront + admin
  mobile/sama-android             Kotlin Compose app
deploy/
  terraform/                      GCP infrastructure as code
  k8s/                            Kustomize base + overlays
build/docker/                     Dockerfiles
tests/                            unit + integration (Testcontainers)
```
