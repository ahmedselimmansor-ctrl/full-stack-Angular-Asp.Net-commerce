# Sama

A commerce platform built for Google Cloud: an Angular storefront, a Kotlin app, and six .NET
microservices behind a single API gateway, deployed to GKE.

```
Browser / Android app
        │
        ▼
Cloud Load Balancer ── Cloud Armor (WAF, per-IP limits) ── Cloud CDN
        │
        ▼
   Sama.Gateway (YARP)  ── authN · rate limits · routing
        │
   ┌────┴────┬─────────┬──────────┬───────────┬──────────┐
Identity  Catalog   Basket    Ordering   Inventory   Payment
   │         │         │          │           │          │
   └─────────┴────┬────┴──────────┴───────────┴──────────┘
                  ▼
   Cloud SQL (schema per service) · Memorystore · Pub/Sub
```

---

## What is here

| Path | What it is |
|---|---|
| `src/gateway/Sama.Gateway` | YARP edge: token validation, rate limiting, routing |
| `src/services/*` | Six .NET 10 services, each owning its own schema |
| `src/shared/*` | Kernel, security, infrastructure and messaging libraries |
| `src/web/sama-web` | Angular 22 storefront |
| `src/mobile/sama-android` | Kotlin / Compose Android app |
| `deploy/terraform` | GCP infrastructure |
| `deploy/k8s` | Kustomize base and per-environment overlays |
| `build/docker` | Container images |
| `scripts` | Local development and end-to-end test scripts |
| `docs` | Architecture, security model, operations |

---

## Running it locally

Dependencies come up in Docker; the services run on the host.

```bash
docker compose up -d
```

```bash
./scripts/run-local.sh start
```

That starts Postgres, Redis and the Pub/Sub emulator, provisions the topics and subscriptions,
and brings up all seven .NET processes. `./scripts/run-local.sh status` shows readiness and
`stop` shuts them down.

**Ports.** The gateway is on `8080` and is the only one a client needs. Postgres and Redis are
mapped to **55432** and **56379** rather than their defaults, because those are frequently
already taken.

Apply the database schema once:

```bash
dotnet dotnet-ef database update --project src/services/Sama.Identity/Sama.Identity.csproj
```

Then the storefront:

```bash
cd src/web/sama-web && npm install && npm start
```

### Proving it works

```bash
./scripts/test-saga.sh
```

Exercises the whole checkout saga end to end — order placed, stock reserved, payment taken,
basket emptied — plus both compensating paths (declined payment, insufficient stock),
idempotent retry, and row-level access control. `./scripts/test-gateway.sh` does the same
through the public edge only.

---

## Deploying to GCP

Infrastructure first:

```bash
cd deploy/terraform && terraform init -backend-config="bucket=YOUR_STATE_BUCKET"
```

```bash
terraform apply -var-file=environments/prod.tfvars
```

This creates the VPC, GKE Autopilot cluster, Cloud SQL, Memorystore, Pub/Sub, Cloud Armor
policies, Artifact Registry and the Secret Manager entries. Two secrets are deliberately
created **empty**, because Terraform must never see their values — add them yourself:

```bash
gcloud secrets versions add sama-identity-signing-keys-0-privatekeypem --data-file=signing-key.pem
```

Point DNS at the `ingress_ip` output, then deploy:

```bash
kubectl apply -k deploy/k8s/overlays/prod
```

CI does this automatically on a push to `main`; see `.github/workflows/deploy.yml`. It
authenticates through Workload Identity Federation, so no service-account key is stored in
GitHub.

---

## The parts worth knowing about

**Checkout is a saga, not a transaction.** An order spans four services, so it is coordinated
by events with compensating actions rather than a distributed transaction. Stock is reserved
before payment is taken, so the common failure — an item selling out between browsing and
checkout — costs a rejection rather than a charge and a refund. See
[docs/01-architecture.md](docs/01-architecture.md).

**Events go through a transactional outbox.** A service writes its state change and its
outgoing event in one database transaction, and a separate dispatcher delivers them. Nothing
can announce an order that was rolled back, or commit one whose announcement was lost.

**Prices are never trusted from the client.** The basket records the price at the time of
adding, and Ordering re-prices every line against the catalogue at checkout.

**Rate limiting is layered three deep.** Cloud Armor drops volumetric abuse before it costs a
pod; the gateway applies Redis-backed per-route limits; each service enforces its own. The
limiter is distributed because an in-process one would permit N times the configured rate
across N replicas.

**Authorization is permission-based, not role-based.** Endpoints require
`catalog:product:write`, never "is this an admin". Roles are bundles resolved at token-issue
time, so re-shaping who can do what needs no endpoint changes.

**Tenant and ownership filters live in the data layer.** They are EF Core model-level query
filters, so a hand-written query that forgets its `WHERE` clause still cannot read another
tenant's rows. See [docs/02-security.md](docs/02-security.md).

---

## Documentation

- [Architecture](docs/01-architecture.md) — services, data ownership, the saga, caching
- [Security](docs/02-security.md) — the threat model and what answers each part of it
- [Operations](docs/03-operations.md) — runbook, failure modes, what to do about them
