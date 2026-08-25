# Sama

A commerce platform built for Google Cloud. Angular storefront, Kotlin app, six .NET
microservices behind one API gateway, deployed to GKE.

[![CI](https://github.com/ahmedselimmansor-ctrl/sama-commerce/actions/workflows/ci.yml/badge.svg)](https://github.com/ahmedselimmansor-ctrl/sama-commerce/actions/workflows/ci.yml)

| | |
|---|---|
| **Backend** | .NET 10, six services plus a YARP gateway |
| **Storefront** | Angular 22, standalone, signals, zoneless |
| **Mobile** | Kotlin, Jetpack Compose, Hilt |
| **Data** | PostgreSQL 16 (schema per service), Redis, Pub/Sub |
| **Platform** | GKE Autopilot, Cloud SQL, Memorystore, Cloud Armor, Cloud CDN |
| **Tests** | 168 — unit, integration, end-to-end |

---

## Contents

- [System design](#system-design)
- [The checkout saga](#the-checkout-saga)
- [Database design](#database-design)
- [The storefront](#the-storefront)
- [Security](#security)
- [Deployment](#deployment)
- [Running it locally](#running-it-locally)
- [Testing](#testing)
- [Repository layout](#repository-layout)

---

## System design

### Context

Three clients, one public edge. Nothing reaches a service directly.

```mermaid
flowchart TB
    subgraph clients [Clients]
        web["Angular storefront<br/>browser"]
        app["Kotlin app<br/>Android"]
        partner["Partner integrations<br/>server to server"]
    end

    subgraph edge [Google Cloud edge]
        lb["Global External<br/>Application Load Balancer"]
        armor["Cloud Armor<br/>WAF, per-IP limits, DDoS"]
        cdn["Cloud CDN<br/>static bundle, product media"]
    end

    subgraph cluster [GKE Autopilot]
        gw["Sama.Gateway (YARP)<br/>token validation, rate limits, routing"]
        identity[Identity]
        catalog[Catalog]
        basket[Basket]
        ordering[Ordering]
        inventory[Inventory]
        payment[Payment]
    end

    subgraph data [Managed data]
        sql[("Cloud SQL<br/>PostgreSQL 16")]
        redis[("Memorystore<br/>Redis")]
        pubsub{{"Pub/Sub"}}
        bucket[("Cloud Storage<br/>product media")]
    end

    web --> lb
    app --> lb
    partner --> lb
    lb --> armor --> gw
    lb --> cdn --> bucket

    gw --> identity
    gw --> catalog
    gw --> basket
    gw --> ordering
    gw --> inventory
    gw --> payment

    identity --> sql
    catalog --> sql
    ordering --> sql
    inventory --> sql
    payment --> sql

    identity --> redis
    catalog --> redis
    basket --> redis
    ordering --> redis

    ordering --> pubsub
    inventory --> pubsub
    payment --> pubsub
    catalog --> pubsub
    pubsub --> ordering
    pubsub --> inventory
    pubsub --> payment
    pubsub --> basket
```

### Services and what each one owns

Nothing reads another service's tables. Cross-service reads go over HTTP; cross-service writes
go through events.

```mermaid
flowchart LR
    subgraph read [Synchronous, on the read path]
        direction TB
        b2c["Basket asks Catalog<br/>for current prices"]
        o2b["Ordering asks Basket<br/>for the lines"]
        o2c["Ordering asks Catalog<br/>to re-price at checkout"]
    end

    subgraph write [Asynchronous, on the write path]
        direction TB
        e1["ordering.order.placed"]
        e2["inventory.stock.reserved"]
        e3["ordering.payment.requested"]
        e4["payment.payment.succeeded"]
        e5["ordering.order.confirmed"]
    end

    e1 --> e2 --> e3 --> e4 --> e5
```

| Service | Owns | Store | Emits |
|---|---|---|---|
| **Gateway** | edge policy | Redis (limits) | — |
| **Identity** | users, roles, refresh tokens | `identity` schema | `user.registered` |
| **Catalog** | products, categories | `catalog` schema | `product.priceChanged`, `product.published` |
| **Basket** | active carts | Redis only | — |
| **Ordering** | orders, saga state | `ordering` schema | `order.placed`, `payment.requested`, `order.confirmed`, `order.cancelled` |
| **Inventory** | stock, reservations | `inventory` schema | `stock.reserved`, `stock.rejected` |
| **Payment** | payment intents | `payment` schema | `payment.succeeded`, `payment.failed` |

Baskets live in Redis and orders live in PostgreSQL because the two have different worth: a
lost basket is an annoyance, a lost order is a financial incident.

### How a product read is served

Four layers answer before the database does. The interesting part is what each one buys.

```mermaid
flowchart LR
    browser([Browser]) --> cdn{"Cloud CDN<br/>s-maxage 300"}
    cdn -- hit --> browser
    cdn -- miss --> gw[Gateway]
    gw --> svc[Catalog]
    svc --> l1{"In-process L1<br/>30s"}
    l1 -- hit --> svc
    l1 -- miss --> l2{"Redis L2<br/>5 min"}
    l2 -- hit --> svc
    l2 -- miss --> db[("PostgreSQL<br/>GIN full-text index")]
    db --> l2 --> l1 --> svc --> gw --> cdn
```

L1 absorbs the repeat reads one pod makes within seconds. L2 stops a newly-scaled pod arriving
with a cold cache and stampeding the database. `HybridCache` collapses concurrent misses for
the same key into one factory call, so a hot key expiring costs one query rather than a
hundred.

Writes invalidate by **tag**, not by key: one price change has to evict the product page, its
category listing and every filtered search page it appears on, and those keys cannot be
enumerated at write time.

### Rate limiting, three layers deep

```mermaid
flowchart TB
    req([Request]) --> armor{"Cloud Armor<br/>per IP, volumetric"}
    armor -- over --> d1["429 — never reaches a pod"]
    armor -- under --> gwl{"Gateway<br/>per route, Redis"}
    gwl -- over --> d2["429 + Retry-After"]
    gwl -- under --> svcl{"Service<br/>per endpoint"}
    svcl -- over --> d3["429 + Retry-After"]
    svcl -- under --> handler([Handler])
```

The limiter is distributed because an in-process one on N replicas permits N times the
configured rate, and the replica count changes under autoscaling. It uses a sliding-window
**log** rather than a fixed-window counter: the cheaper counter allows a double burst across a
window boundary, and for login throttling that doubling is the whole attack.

The edge **fails closed** and the services **fail open**. A Redis outage must not silently
remove all throttling from the internet-facing tier, but it also should not stop a shop
trading.

---

## The checkout saga

An order spans four services, so it is coordinated by events with compensating actions rather
than a distributed transaction.

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant O as Ordering
    participant I as Inventory
    participant P as Payment
    participant B as Basket

    C->>O: POST /orders (Idempotency-Key)
    Note over O: re-prices every line<br/>against the catalogue
    O->>O: order = Pending<br/>+ outbox row, one transaction
    O-->>C: 202 Accepted

    O-)I: ordering.order.placed
    I->>I: reserve stock
    alt stock available
        I-)O: inventory.stock.reserved
        O->>O: order = AwaitingPayment
        O-)P: ordering.payment.requested
        P->>P: charge
        alt payment approved
            P-)O: payment.payment.succeeded
            O->>O: order = Confirmed
            O-)B: ordering.order.confirmed
            B->>B: empty the basket
        else payment declined
            P-)O: payment.payment.failed
            O->>O: order = Cancelled
            O-)I: ordering.order.cancelled
            I->>I: release the hold
        end
    else insufficient stock
        I-)O: inventory.stock.rejected
        O->>O: order = Cancelled
        Note over P: never charged
    end
```

Stock is reserved **before** payment is taken. The common failure — an item selling out between
browsing and checkout — then costs a rejection rather than a charge followed by a refund.

### Order states

Every transition goes through a guarded method on the aggregate, which is what makes a
redelivered or out-of-order event a no-op instead of a corruption.

```mermaid
stateDiagram-v2
    [*] --> Pending: placed
    Pending --> AwaitingPayment: stock reserved
    Pending --> Cancelled: stock rejected
    AwaitingPayment --> Confirmed: payment succeeded
    AwaitingPayment --> Cancelled: payment failed
    Confirmed --> Shipped: despatched
    Confirmed --> Cancelled: cancelled by customer or support
    Cancelled --> [*]
    Shipped --> [*]

    note right of Cancelled
        Terminal. A payment arriving
        afterwards throws rather than
        reviving the order, so a human
        sees the refund that is owed.
    end note
```

### Why the outbox exists

Publishing directly from a request handler has two failure modes and no good answer to either.

```mermaid
flowchart TB
    subgraph bad1 ["Publish, then commit"]
        a1[publish event] --> a2[commit] --> a3{"rollback?"}
        a3 -- yes --> a4["an order was announced<br/>that does not exist"]
    end

    subgraph bad2 ["Commit, then publish"]
        b1[commit] --> b2{"process dies"}
        b2 -- yes --> b3["the order exists but<br/>nobody was told"]
    end

    subgraph good ["Transactional outbox"]
        c1["state change + outbox row<br/>one transaction"] --> c2[commit]
        c2 --> c3["dispatcher claims rows<br/>FOR UPDATE SKIP LOCKED"]
        c3 --> c4[publish] --> c5[mark processed]
        c4 -. failure .-> c6["backoff, retry,<br/>park after 10 attempts"]
    end
```

`FOR UPDATE SKIP LOCKED` is what lets every replica run a dispatcher and compete for the same
rows without a leader election or a distributed lock — each row goes to exactly one replica per
pass, and throughput scales with replica count.

---

## Database design

One Cloud SQL instance, a **schema per service**, and a **database role per service** with
grants on only its own schema. A compromised catalogue service cannot read the users table, and
a schema can be lifted onto its own instance later by changing one connection string.

```mermaid
flowchart TB
    subgraph instance ["Cloud SQL instance, private IP"]
        s1["identity<br/>owner: sama_identity"]
        s2["catalog<br/>owner: sama_catalog"]
        s3["ordering<br/>owner: sama_ordering"]
        s4["inventory<br/>owner: sama_inventory"]
        s5["payment<br/>owner: sama_payment"]
        pub["public<br/>CREATE revoked from PUBLIC"]
    end
```

### Identity

```mermaid
erDiagram
    users ||--o{ user_roles : "grants"
    users ||--o{ refresh_tokens : "issues"

    users {
        uuid id PK
        uuid tenant_id
        varchar email
        varchar normalized_email "UNIQUE per tenant, WHERE NOT is_deleted"
        boolean email_confirmed
        varchar display_name
        varchar password_hash "PBKDF2-SHA512, 210k iterations"
        varchar security_stamp "rotating it invalidates every issued token"
        boolean is_active
        timestamptz lockout_ends_at
        int access_failed_count
        timestamptz last_login_at
        boolean is_deleted
    }

    user_roles {
        uuid user_id PK,FK
        varchar role PK "resolved to permissions at token-issue time"
    }

    refresh_tokens {
        uuid id PK
        uuid user_id FK
        uuid family_id "a rotation chain; reuse revokes the lineage"
        varchar token_hash "UNIQUE, SHA-256 — the raw token is never stored"
        timestamptz expires_at
        timestamptz consumed_at "non-null means presenting it again is theft"
        timestamptz revoked_at
        varchar revoked_reason
    }
```

### Catalog

```mermaid
erDiagram
    categories ||--o{ categories : "parent of"
    categories ||--o{ products : "contains"
    products ||--o{ product_images : "has"

    categories {
        uuid id PK
        uuid tenant_id
        varchar name
        varchar slug "UNIQUE per tenant, WHERE NOT is_deleted"
        uuid parent_id FK "self-referencing; no fixed depth"
        int sort_order
        boolean is_active
        boolean is_deleted
    }

    products {
        uuid id PK
        uuid tenant_id
        varchar sku "UNIQUE per tenant, WHERE NOT is_deleted"
        varchar name
        varchar slug "UNIQUE per tenant — stable, cacheable URLs"
        varchar description
        uuid category_id FK
        varchar brand
        numeric price "numeric(18,2), never a float"
        varchar currency
        varchar status "Draft, Published, Archived"
        tsvector search_vector "database-generated, GIN indexed"
        boolean is_deleted
    }

    product_images {
        uuid product_id PK,FK
        varchar url PK "a path in the media bucket, served by the CDN"
        varchar alt_text
        int sort_order
    }
```

Prices are `numeric(18,2)`, never a float: binary floating point cannot represent 0.10 exactly,
and money that does not add up is the fastest way to lose a merchant's trust.

The `search_vector` is generated **by the database** from name, brand and description, so it
can never drift from the row the way an application-maintained copy would.

### Ordering, Inventory and Payment

```mermaid
erDiagram
    orders ||--|{ order_lines : "contains"

    orders {
        uuid id PK
        uuid tenant_id
        uuid owner_id "the customer; drives the row-level access scope"
        varchar order_number "UNIQUE per tenant, SAMA-YYYYMMDD-XXXXXX"
        varchar status "the saga's state"
        numeric total
        varchar currency
        uuid payment_id
        varchar cancellation_reason
        timestamptz confirmed_at
        varchar shipping_line1
        varchar shipping_city
        varchar shipping_postcode
        varchar shipping_country
    }

    order_lines {
        uuid order_id PK,FK
        uuid product_id PK
        varchar sku
        varchar name "copied at checkout — renaming a product must not rewrite old invoices"
        int quantity
        numeric unit_price
        varchar currency
    }
```

```mermaid
erDiagram
    stock_reservations ||--|{ stock_reservation_lines : "holds"

    stock_items {
        uuid id PK
        uuid tenant_id
        uuid product_id "UNIQUE per tenant"
        varchar sku
        int quantity_on_hand
        int quantity_reserved "available = on_hand - reserved"
        int reorder_level
    }

    stock_reservations {
        uuid id PK
        uuid tenant_id
        uuid order_id "UNIQUE — one order holds stock at most once"
        varchar status "Held, Released, Fulfilled"
    }

    stock_reservation_lines {
        uuid reservation_id PK,FK
        uuid product_id PK
        int quantity "what was actually taken, not what the order asked for"
    }

    payments {
        uuid id PK
        uuid tenant_id
        uuid owner_id
        uuid order_id "UNIQUE — an order is charged at most once"
        numeric amount
        varchar currency
        varchar status "Pending, Succeeded, Failed, Refunded"
        varchar provider_reference "for reconciliation and refunds"
        varchar failure_reason
        timestamptz settled_at
    }
```

Two unique indexes carry most of the saga's safety: `stock_reservations.order_id` means a
redelivered `order.placed` cannot double-hold, and `payments.order_id` means a redelivered
`payment.requested` cannot double-charge. Idempotency is enforced by the database, not only by
a handler remembering to check.

### The outbox table, in every schema

```mermaid
erDiagram
    outbox_messages {
        uuid id PK
        varchar type "ordering.order.placed.v1"
        jsonb payload
        timestamptz occurred_at
        timestamptz processed_at "null while pending"
        int attempts
        timestamptz next_attempt_at "null once parked, so it leaves the claim query"
        varchar last_error
        uuid tenant_id "restores tenant context on the consumer"
        varchar trace_parent "joins the consumer's span to the producing trace"
    }
```

Indexed `(next_attempt_at, occurred_at) WHERE processed_at IS NULL`, so the dispatcher's only
query stays on the small unprocessed tail rather than the whole history.

### Conventions across every table

| Convention | Why |
|---|---|
| `snake_case` names | PostgreSQL folds unquoted identifiers to lower case; PascalCase would need quoting in every hand-written query, forever |
| UUIDv7 primary keys | Unguessable in a URL, but time-ordered, so inserts stay at the right edge of the B-tree instead of scattering |
| `xmin` concurrency token | Optimistic concurrency without a column to maintain; two replicas updating one aggregate collide loudly |
| Audit columns | Stamped by a SaveChanges interceptor, never by the caller |
| Soft delete | Orders and other services reference these ids; a hard delete strands them |
| Partial unique indexes | `WHERE is_deleted = false`, so a withdrawn product does not reserve its slug forever |
| `tenant_id` on scoped tables | Enforced by a model-level query filter, so a forgotten `WHERE` cannot leak across tenants |

---

## The storefront

Three departments — Clothing, Shoes and Electronics — sit behind one catalogue
component; a department is `/shop` with its category pinned and its own editorial
masthead. The home page opens on the departments rather than a wall of products,
because its single job is to route someone into one of three rooms.

```mermaid
flowchart LR
    Home["/"] --> Clothing["/clothing"]
    Home --> Shoes["/shoes"]
    Home --> Electronics["/electronics"]
    Home --> Shop["/shop"]

    Clothing --> Product["/products/:slug"]
    Shoes --> Product
    Electronics --> Product
    Shop --> Product

    Product --> Basket["/basket"] --> Checkout["/checkout"] --> Orders["/orders/:id"]

    Admin["/admin"] --> AdminSections["products · categories · stock<br/>orders · payments · users"]
```

Every route is lazily loaded, and `/admin` is guarded on the parent so a child
cannot be reached directly. That decides what renders; the API decides what may
actually be read or changed.

The interface is bilingual English and Arabic, switchable at runtime, with the
whole layout mirroring for RTL through CSS logical properties. The catalogue is
bilingual too: each product carries an optional Arabic name and description, both
travel in one payload so switching language needs no refetch, and both are
indexed for full-text search. Light and dark are
joined by a third "match system" state that keeps following the operating system.
Both choices survive a reload and are applied before first paint.

Product imagery is served from Unsplash over the CDN; `scripts/seed-shop.sh`
fills the three departments with 22 priced, photographed, in-stock products so
the design can be judged against real content rather than placeholders.

See [the design system](docs/04-design-system.md) for the palette, the type
pairing, and the encapsulation trap that made Arabic headings render in a
Latin-only face.

---

## Security

The full model is in [docs/02-security.md](docs/02-security.md). The token flow in brief:

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant G as Gateway
    participant I as Identity
    participant S as Service

    B->>I: POST /auth/login
    I-->>B: access token (15 min, RS256)<br/>+ Set-Cookie refresh (HttpOnly, Strict)
    Note over B: access token in memory only —<br/>never in localStorage

    B->>G: GET /api/v1/orders + Bearer
    G->>G: validate signature against JWKS
    G->>S: forward
    S->>S: check permission claim,<br/>apply tenant and ownership filters
    S-->>B: 200

    Note over B: 15 minutes later
    B->>G: request with an expired token
    G-->>B: 401
    B->>I: POST /auth/refresh (cookie, empty body)
    I->>I: consume the token,<br/>issue a replacement in the same family
    I-->>B: new access token + rotated cookie
    Note over B,I: replaying a consumed token revokes<br/>the whole family — both the attacker<br/>and the legitimate holder
```

Access tokens are signed **asymmetrically**. With a shared secret every resource service would
hold the key that mints tokens, so compromising any one of them would let an attacker forge an
administrator. With RSA they can verify and cannot issue.

Authorization is **permission-based**: endpoints require `catalog:product:write`, never "is
this an admin". Roles are bundles resolved at token-issue time, so re-shaping who can do what
never means editing endpoints.

```mermaid
flowchart LR
    role["Role<br/>catalog_manager"] --> p1["catalog:product:write"]
    role --> p2["catalog:product:delete"]
    role --> p3["catalog:category:write"]
    role --> p4["inventory:stock:write"]
    p1 --> ep["POST /api/v1/products<br/>.RequirePermission(...)"]
```

Permissions ending `.own` and `.any` form a pair. The scope becomes a predicate **inside the
SQL**, so the database never returns rows the caller may not see.

---

## Deployment

```mermaid
flowchart TB
    subgraph gh [GitHub]
        push([push to main]) --> ci["CI: build, test, scan"]
        ci --> cd["Deploy workflow"]
    end

    subgraph gcp [Google Cloud]
        ar[("Artifact Registry")]
        job["Job: schema migrations<br/>must complete first"]
        k8s["GKE Autopilot<br/>rolling update, maxUnavailable 0"]
        sql[("Cloud SQL")]
    end

    cd -- "OIDC, no stored key" --> ar
    cd --> job --> sql
    job -- "on success" --> k8s
    ar --> k8s
```

Migrations run as a **Job that must complete** before any new pod starts. Applying them from
application startup instead would have every replica race to run them, and would present a
failed migration as a crash-looping deployment rather than a failed job with a readable log.

Images are tagged with the commit SHA, so a rollback is a re-point rather than a rebuild.

```bash
cd deploy/terraform && terraform init -backend-config="bucket=YOUR_STATE_BUCKET"
```

```bash
terraform apply -var-file=environments/prod.tfvars
```

This creates the VPC, GKE Autopilot cluster, Cloud SQL, Memorystore, Pub/Sub, Cloud Armor
policies, Artifact Registry and the Secret Manager entries. Two secrets are created **empty**
on purpose, because Terraform must never see their values:

```bash
gcloud secrets versions add sama-identity-signing-keys-0-privatekeypem --data-file=signing-key.pem
```

Point DNS at the `ingress_ip` output, then:

```bash
kubectl apply -k deploy/k8s/overlays/prod
```

---

## Running it locally

```bash
docker compose up -d
```

```bash
./scripts/run-local.sh start
```

That brings up PostgreSQL, Redis and the Pub/Sub emulator, provisions the topics and
subscriptions, builds once, and starts all seven services. `status` shows readiness, `stop`
shuts them down.

**Ports.** The gateway is on `8080` and is the only one a client needs. PostgreSQL and Redis
are mapped to **55432** and **56379** rather than their defaults, because those are frequently
already taken.

Apply the schema once:

```bash
dotnet dotnet-ef database update --project src/services/Sama.Identity/Sama.Identity.csproj
```

Then the storefront:

```bash
cd src/web/sama-web && npm install && npm start
```

---

## Testing

```mermaid
flowchart TB
    e2e["25 end-to-end<br/>real browser, real stack, real saga"]
    int["20 integration<br/>real PostgreSQL and Redis in Testcontainers"]
    unit["170 unit<br/>101 .NET, 49 Angular, 20 Kotlin"]

    unit --> int --> e2e
```

| Suite | Command | Covers |
|---|---|---|
| .NET unit | `dotnet test tests/Sama.UnitTests` | Money rounding, Result, access scopes, aggregates driven out of order |
| .NET integration | `dotnet test tests/Sama.IntegrationTests` | Query filters becoming SQL, partial indexes, Lua atomicity under concurrency |
| Angular unit | `./scripts/test-web.sh` | Auth service, interceptors, guards, theme, translations, RTL encapsulation |
| Kotlin unit | `cd src/mobile/sama-android && ./gradlew test` | Currency precision, wire-contract tolerance |
| End-to-end | `cd tests/e2e && npm test` | Browse, register, checkout, saga settlement, back office |
| Shell (API) | `./scripts/test-all.sh` | Five suites against the running stack |
| Contrast | `./scripts/check-contrast.py` | Every rendered colour pairing against WCAG AA, both themes |

The integration suite runs against real infrastructure on purpose. An in-memory substitute
would pass while production failed: what needs proving is that a model-level filter really
becomes a `WHERE` clause, that a partial unique index behaves as intended, and that 50
concurrent rate-limit acquisitions against a limit of 10 admit exactly 10.

> **Note.** Vitest receives absolute paths as glob patterns, so a checkout path containing
> parentheses never matches itself and the runner reports "No test files found" immediately
> after listing the files it found. `scripts/test-web.sh` stages the sources at a clean path
> and runs there, so the suite works from any checkout. See
> [src/web/sama-web/TESTING.md](src/web/sama-web/TESTING.md).

---

## Repository layout

```mermaid
flowchart TB
    root["sama-commerce"] --> src[src]
    root --> deploy[deploy]
    root --> tests[tests]
    root --> build["build/docker"]
    root --> scripts[scripts]
    root --> docs[docs]

    src --> gateway["gateway/Sama.Gateway"]
    src --> services["services/ — six .NET services"]
    src --> shared["shared/ — Kernel, Security, Infrastructure, Messaging"]
    src --> webdir["web/sama-web — Angular"]
    src --> mobile["mobile/sama-android — Kotlin"]

    deploy --> tf["terraform/ — GCP"]
    deploy --> k8s["k8s/ — Kustomize base and overlays"]

    tests --> ut["Sama.UnitTests"]
    tests --> it["Sama.IntegrationTests"]
    tests --> e2edir["e2e — Playwright"]
```

The shared libraries are where the cross-cutting concerns live once instead of six times:
`Kernel` (Result, Money, paging, domain primitives), `Security` (JWT, permissions, tenancy,
access scopes), `Infrastructure` (caching, rate limiting, EF base context, telemetry) and
`Messaging` (outbox, Pub/Sub publisher and subscriber).

---

## Documentation

- [Architecture](docs/01-architecture.md) — services, data ownership, caching, the saga
- [Security](docs/02-security.md) — threat model, and what answers each part of it
- [Operations](docs/03-operations.md) — runbook, failure modes, alerting, recovery
- [Design system](docs/04-design-system.md) — palette, type, theming, and how Arabic is handled
