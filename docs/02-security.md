# Sama — Security Model

What Sama defends against, and what specifically does the defending. Everything below is
implemented, not aspirational; each row names the file that does it.

---

## 1. Threat model

The platform holds customer identities, delivery addresses and order history, and it moves
money. The realistic adversaries are, in rough order of likelihood:

| Adversary | Goal | Primary defence |
|---|---|---|
| Credential stuffer | Take over accounts using breached password lists | Layered rate limits, account lockout, expensive password hashing |
| Opportunistic scanner | Find an unauthenticated endpoint or an injection | Cloud Armor WAF, fail-closed authorization, parameterised queries |
| Malicious customer | Buy below list price, read another customer's orders | Server-side re-pricing, row-level access scopes |
| Compromised service | Move laterally to other services or their data | Per-service DB roles, NetworkPolicies, no shared credentials |
| Stolen device / XSS | Reuse a session indefinitely | Short access tokens, HttpOnly refresh cookie, rotation with theft detection |

Explicitly **out of scope**: Sama never handles raw card details. The client tokenises them
directly with the payment provider and Sama sees only the resulting token, which is what keeps
the platform out of PCI-DSS scope.

---

## 2. Authentication

**Tokens.** Access tokens are RS256 JWTs, valid 15 minutes. Refresh tokens are 256-bit random
values, valid 30 days, stored only as SHA-256 hashes.

Asymmetric signing is the load-bearing choice. With a shared HMAC secret, all six resource
services would need the key that *mints* tokens, so compromising any one of them would let an
attacker forge an administrator. With RSA they hold only the public key: they can verify, and
they cannot issue.

**Rotation and theft detection** — `Features/Auth/TokenService.cs`.
Every refresh consumes the presented token and issues a replacement in the same family. If a
*consumed* token is ever presented again, the only explanations are a stolen token or a cloned
client, so the entire family is revoked. Both the attacker and the legitimate user are signed
out. That is the intended outcome: a forced re-login beats an attacker holding a
self-renewing session.

**Where tokens live.**

| Client | Access token | Refresh token |
|---|---|---|
| Browser | In memory only (an Angular signal) | HttpOnly, Secure, SameSite=Strict cookie scoped to `/api/v1/auth` |
| Android | In memory (`AtomicReference`) | `EncryptedSharedPreferences`, key in the hardware keystore |

Nothing in web storage. A refresh token in `localStorage` is readable by any injected script,
which turns one XSS into permanent account access; an HttpOnly cookie is not readable by
JavaScript at all, so an XSS is bounded to what it can do while the page is open.

`SameSite=Strict` closes the CSRF hole that a cookie-authenticated refresh endpoint would
otherwise open — no legitimate flow initiates a refresh from another site.

**Immediate revocation** — `Redis/RedisTokenRevocationStore.cs`.
Short token lifetimes make revocation *mostly* unnecessary, but "sign out everywhere" and "this
account is compromised" must take effect now. Each user carries a security stamp embedded in
their tokens; rotating it invalidates every issued token at once. Individual tokens can also be
denylisted by `jti` for their remaining lifetime.

**Password storage** — PBKDF2-HMAC-SHA512, 210,000 iterations. Deliberately above the framework
default: hashing is the only thing between a leaked table and the plaintext passwords, and
~100 ms per sign-in is invisible to a user and paid per guess by an attacker.

**Account enumeration.** Sign-in returns one message for every failure. When the account does
not exist, the code still verifies against a throwaway hash, so the "unknown account" path is
not measurably faster. Registration returns the same shape whether or not the address is taken.

---

## 3. Authorization

**Permission-based, never role-based.** Endpoints require `catalog:product:write`, not "is this
user an Admin" — `Permissions/SamaPermissions.cs`. Roles are bundles resolved at token-issue
time, so re-shaping who can do what never means editing endpoints.

**Fails closed.** The authorization fallback policy requires an authenticated user, so an
endpoint that forgets `[Authorize]` is protected rather than silently public. Anonymous access
is opt-in and explicit.

**Row-level access control** — `AccessControl/AccessScope.cs`.
Permissions ending `.own` and `.any` form a pair: the first restricts the caller to rows they
own, the second does not. The scope becomes a predicate *inside the SQL*, so the database never
returns rows the caller may not see.

**Tenant isolation** — `Persistence/SamaDbContext.cs`.
Tenant scoping is an EF Core model-level query filter, not a convention. A hand-written LINQ
query that forgets its tenant predicate still cannot read another tenant's rows, because the
filter is welded to the model rather than to the call site.

**404, not 403, for another customer's order.** A 403 would confirm the record exists, turning
sequential id probing into an order-count oracle.

---

## 4. Rate limiting

Three layers, each catching what the one outside it cannot see.

| Layer | Where | Catches |
|---|---|---|
| Cloud Armor | `deploy/terraform/security.tf` | Volumetric abuse, before it costs a pod |
| Gateway | `src/gateway/Sama.Gateway` | Per-route limits across all traffic |
| Service | `RateLimiting/` | Anything reaching a service by another path |

The limiter is **distributed** (Redis sliding-window log). An in-process limiter on N replicas
permits N times the configured rate, and the replica count changes under autoscaling.

A sliding-window *log* rather than a fixed-window counter: the cheaper counter allows a double
burst across a window boundary, and for login throttling that doubling is the whole attack.

| Policy | Limit | Partition |
|---|---|---|
| `auth-login` | 5 / 5 min | IP **and** username |
| `auth-register` | 3 / hour | IP |
| `auth-refresh` | 600 / 5 min | IP |
| `checkout` | 10 / min | user |
| `search` | 30 / min | user |
| `write` | 60 / min | user |
| `read` | 600 / min | user |

Login is partitioned on IP *and* username because credential stuffing rotates the username on
one IP and the IP on one username; partitioning on either alone misses half the attack.

Refresh is deliberately loose. It cannot be brute-forced — it requires an unguessable 256-bit
token, and presenting a consumed one already revokes the whole family — so the limit is a
volumetric guard rather than an anti-guessing one. Setting it tight would mostly punish
legitimate users sharing an address behind corporate NAT or carrier-grade NAT, who would find
themselves signed out at random as they throttled each other's renewals.

**The edge fails closed** and the services fail open. A Redis outage must not silently remove
all throttling from the internet-facing tier, but it also should not stop a shop trading.

**Client IP is taken from a header the load balancer overwrites**, not from `X-Forwarded-For`
— `Http/ClientIpMiddleware.cs`. Google's LB *appends* to any XFF the client supplied, so the
leftmost entry is attacker-controlled; trusting it would let a caller mint a fresh rate-limit
bucket on every request. IPv6 clients are bucketed by /64, since one residential allocation
hands out billions of addresses.

---

## 5. Data access

**Per-service database roles.** Each service connects as its own PostgreSQL role with grants on
only its own schema — `db/init/01-roles-and-schemas.sql`. A compromised catalogue service
cannot read the users table. `PUBLIC` is revoked from `public`, so nothing lands there by
default.

**No shared connection string.** Each role's credential is a separate Secret Manager entry, so
one leaked secret does not open every schema.

**Migrations run as the schema owner**, not as an administrator — `run-migrations.sh`.

**Soft deletes.** Orders and products are referenced by financial records and by other
services' read models; physically deleting them turns those references into dangling ids.

**Optimistic concurrency** via PostgreSQL's `xmin`, so two replicas updating the same aggregate
collide loudly rather than one silently overwriting the other. For stock counts that is a
correctness property, not a nicety.

---

## 6. Network and runtime

**Nothing has a public IP.** Pods reach the internet through Cloud NAT; the only inbound path
is the load balancer. Cloud SQL and Memorystore are on private addresses with no public
endpoint at all.

**Default-deny NetworkPolicies** — `deploy/k8s/base/network-policy.yaml`. Without them a
compromised pod can reach every service and both data stores directly, bypassing the gateway's
authentication entirely.

**Distroless, non-root containers.** No shell, no package manager, UID 1654, read-only root
filesystem, all capabilities dropped, `seccompProfile: RuntimeDefault`. A container with no
shell removes the most common post-exploitation foothold. The namespace enforces the
`restricted` Pod Security Standard, so a violating workload is rejected at admission.

**Workload Identity.** Pods authenticate to Google Cloud without any service-account key
existing — nothing to leak, rotate, or accidentally commit. CI federates from GitHub OIDC with
an attribute condition restricting it to this repository.

**Internal endpoints are not routable from the internet.** The bulk pricing lookup that Basket
and Ordering call service-to-service is blocked at the edge ahead of routing, returning 404
rather than 403 so its existence is not advertised.

**Service-to-service calls forward the caller's token** — `Http/BearerForwardingHandler.cs`.
The alternative, each service holding a broad machine credential, means an internal call is no
longer bounded by what the original caller was allowed to do.

---

## 7. Application-level

**Prices come from the catalogue, never the request.** The basket captures a price when an item
is added, and Ordering re-prices every line at checkout. A client-supplied price is the oldest
e-commerce vulnerability there is.

**Checkout requires an `Idempotency-Key`** and rejects requests without one. A client that
omits it has no safe retry story, and allowing that turns a dropped response into a double
charge. Payment is additionally guarded by a unique index on `order_id`.

**Response hardening.** `X-Content-Type-Options`, `X-Frame-Options: DENY`, a restrictive CSP,
`Referrer-Policy`, `Permissions-Policy` and HSTS. The `Server` header is disabled fleet-wide —
it hands an attacker a free version match against known CVEs, and Kestrel writes it after
middleware runs, so it had to be turned off at the source.

**Errors reveal nothing.** Stack traces and raw driver messages never reach a client in
production; failures are mapped to RFC 9457 problem documents with stable codes.

**CORS lists exact origins.** Never a wildcard: the API answers with credentials, and
`Access-Control-Allow-Origin: *` is invalid with credentials anyway.

---

## 8. Known gaps

Honest about what is not done:

- **Email verification and password reset** are modelled (`EmailConfirmed`, security stamps)
  but the delivery flow is not implemented.
- **MFA** is not implemented. The security-stamp mechanism is the right hook for it.
- **Certificate pinning** in the Android app is not configured. It would raise the bar against
  a compromised device CA, at the cost of an app update whenever the certificate rotates.
- **Cloud Armor WAF rules run at sensitivity 1**, the conservative tier. Higher tiers catch
  more but produce false positives on legitimate payloads, and a WAF that blocks real customers
  is worse than one that blocks less. Raising it needs traffic data first.
- **Secrets rotation is manual.** External Secrets re-reads hourly, so rotating in Secret
  Manager propagates, but nothing initiates rotation on a schedule.
