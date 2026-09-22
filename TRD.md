# Technical Requirements Document (TRD)
## Enterprise-grade C#/.NET 10 API Backend (API Key Auth + EF Core + AWS Fargate)

**Document owner:** Engineering Platform  
**Last updated:** 2026-09-21  
**Target runtime:** .NET 10 (LTS/enterprise standard)  
**Primary hosting:** AWS Fargate (Amazon ECS)  
**Auth standard:** API Key (interim) 
**Persistence:** EF Core (SQL Server)

---

## 1. Purpose and scope
This TRD defines **mandatory engineering rules** and **testable technical requirements** for building and operating an enterprise-grade HTTP API backend in C#/.NET 10 on AWS Fargate.

### In scope
- REST APIs built with ASP.NET Core
- API key authentication + policy-based authorization (interim standard)
- EF Core persistence layer and migrations
- Observability (logs, metrics, distributed tracing) via Datadog
- Resilience, performance, security requirements
- CI/CD quality gates and operational runbooks
- Infrastructure as Code patterns (AWS CDK) for AWS Fargate hosting

### Out of scope (unless adopted by exception)
- UI/front-end
- gRPC/event streaming (may be added as an appendix)
- Data warehouse/BI pipelines

---

## 2. Non-functional requirements (NFRs) and SLOs

### 2.1 Service Level Objectives (SLOs)
These SLOs apply per service, per production environment, measured at the **API ingress** (client-visible).

**Availability**
- **Monthly availability SLO:** **99.9%**
- **Error budget:** ~43m 50s downtime/month

**Latency (successful requests)**
- **Read endpoints (GET/HEAD):** p95 **≤ 200 ms**, p99 **≤ 500 ms**
- **Write endpoints (POST/PUT/PATCH/DELETE):** p95 **≤ 500 ms**, p99 **≤ 1200 ms**
- **Search/list endpoints:** p95 **≤ 800 ms**, p99 **≤ 2000 ms** (with paging)

**Reliability**
- **5xx error rate:** **≤ 0.5%** (monthly)
- **4xx rate:** monitored but not part of SLO (often client-caused); investigate sustained spikes

**Freshness**
- Telemetry data should reach Datadog within **≤ 5 minutes** for operational usefulness.

### 2.2 Service Level Indicators (SLIs)
**Must measure**
- Request rate, error rate (by status code class), duration
- Dependency duration + failures (Amazon RDS for SQL Server, HTTP dependencies)
- Resource utilization: CPU, memory, GC, thread pool, connection pools

### 2.3 Capacity and scaling
**Must**
- Scale horizontally via ECS/Fargate service auto scaling (target-tracking on CPU and/or memory utilization; request-count-per-target via the ALB where appropriate).
- Define target concurrency and scaling trigger(s) per service:
  - HTTP concurrency (requests) and/or CPU/memory utilization
- Define min/max task (desired capacity) count per service and per environment.

---

## 3. Platform and hosting requirements (AWS Fargate)

### 3.1 Container requirements
**Must**
- Build Linux containers.
- Run as non-root user where feasible.
- Provide readiness and liveness probes, exposed as ALB target group / ECS container health checks:
  - `GET /health/live`
  - `GET /health/ready`

**Must**
- Use **IAM Task Roles** (least-privilege, scoped per task) for AWS resource access (Secrets Manager, S3, etc.) instead of instance-wide credentials.
- Configure **ingress** through an Application Load Balancer (ALB) to HTTPS only; TLS terminated at the ALB using an ACM certificate; HTTP listener redirects to HTTPS.

### 3.2 Configuration and secrets
**Must**
- Use environment variables / AWS Systems Manager Parameter Store for non-secret configuration (populated from the CDK environment profile, e.g. `apiEnvVars`).
- Use **AWS Secrets Manager** for secrets (one secret per environment, e.g. `sidekick/prod`, `sidekick/uat`), injected into the ECS task definition at deploy time. No secrets in code, images, or plain environment variables outside approved secret injection.
- Validate required configuration at startup; fail fast with clear error.

### 3.3 Deployment strategy (Infrastructure as Code via AWS CDK)
**Must**
- Define all infrastructure in TypeScript AWS CDK (see `deploy/cdk`), with one stack deployment per environment profile (`sandbox`, `staging`, `uat`, `prod`) under `deploy/cdk/lib/profiles/`. Each profile parameterizes: ECR repository names, VPC name (pre-existing, looked up by name), domain/subdomain for Route53 + ACM, DB security group id, Secrets Manager secret name, Fargate task/service CPU & memory sizing, autoscaling targets, and container environment variables.
- Support the following stack patterns:
  - **API/service stack** (`SidekickStack` pattern): an `ApplicationLoadBalancedFargateService` per API, running the application container plus a **Datadog Agent sidecar** container.
  - **Scheduled worker stack** (`KnowledgeBaseIngestionStack` pattern): one-off Fargate tasks triggered by an EventBridge Scheduler (cron) for batch/background jobs, also paired with a Datadog Agent sidecar.
  - **Repository stack** (`KnowledgeBaseIngestionRepositoryStack` pattern): dedicated ECR repositories for worker images with image scan-on-push, AES-256 encryption, `RETAIN` removal policy, and a lifecycle rule expiring untagged images after 30 days.
- Build container images once and promote the same image tag across environments (image tag passed as a CDK context parameter, e.g. `--context tag=<sha>`); never rebuild per environment.
- Every deployment must be auditable and reproducible: rollouts are driven by CDK stack deploys of a specific, versioned image tag (rolling ECS/Fargate deployment).
- Rollback = redeploy the prior known-good image tag via the same CDK stack/profile.

### 3.4 Networking and security groups
**Must**
- Deploy into a pre-existing VPC, looked up by name per environment (not created by the stack).
- Place the ALB in public subnets (one per Availability Zone) and Fargate tasks in private/application subnets.
- Grant database access explicitly: add the service's security group as an allowed ingress source on the existing DB security group for the database port (e.g., 1433 for SQL Server) — never open the DB security group broadly.
- When a domain is configured, provision an ACM certificate with DNS validation via Route53; enforce an SSL policy of TLS 1.2+ (e.g. `TLS12_EXT`) and alarm on certificate expiration (e.g., 45 days prior to expiry).

### 3.5 Autoscaling and sizing
**Must**
- Explicitly size Fargate tasks per environment (container CPU/memory, service CPU/memory, memory reservation) — do not rely on defaults.
- Define target-tracking autoscaling on CPU and memory utilization (recommended default: 80%/80%) plus explicit min/max task counts per environment.
- Set ALB idle timeout (e.g., 180s) and health check grace period (e.g., 3 minutes) explicitly to avoid premature deregistration during slow starts.

---

## 4. Approved libraries and standards

### 4.1 Web/API
- **ASP.NET Core (.NET 10)**
- **Swashbuckle.AspNetCore** (OpenAPI/Swagger generation)
- **Asp.Versioning.Mvc** (API versioning) – URL versioning preferred (`/v1/...`)
- **Microsoft.AspNetCore.OpenApi** may be used if org standard, but be consistent.

### 4.2 Authentication/Authorization (interim: API Key)
- API key authentication implemented as a custom ASP.NET Core `AuthenticationHandler` validating a request header (default: `X-Api-Key`) against key(s) resolved from AWS Secrets Manager / Parameter Store at startup.
- Policy-based authorization via `IAuthorizationPolicyProvider` / `AddAuthorization`; a default `RequireApiKey` policy is applied to all endpoints unless explicitly marked `[AllowAnonymous]` (e.g., health checks).
- **Must**
  - Reject requests with a missing or invalid key with `401` (Problem Details body, no key material echoed back).
  - Never log the raw API key value.
  - Support multiple concurrently valid keys per environment to allow zero-downtime key rotation.
  - Rate-limit or alert on repeated failed authentication attempts from the same source.
- This is a **deliberate interim standard**. It must be revisited and matured 

### 4.3 Data access
- **Microsoft.EntityFrameworkCore**
- Provider package per DB (e.g., **Microsoft.EntityFrameworkCore.SqlServer** for SQL Server / Amazon RDS for SQL Server)

### 4.4 Validation and mapping
- **FluentValidation.AspNetCore** (request validation)
- Mapping:
  - Approved: **AutoMapper** *or* **Mapster** (choose 1 per service; org default: AutoMapper)

### 4.5 Observability (Datadog)
- **Datadog Agent** container, deployed as a sidecar alongside every Fargate task (per the CDK API/worker stack patterns), receiving APM traces and runtime metrics.
- **Datadog.Trace.Bundle** (dd-trace-dotnet auto-instrumentation) for distributed tracing of ASP.NET Core requests, outbound `HttpClient` calls, and EF Core queries; enabled via `DD_APM_ENABLED=true`.
- Required container environment variables (set via the CDK task definition): `DD_API_KEY` (resolved from Secrets Manager), `DD_ENV`, `DD_SERVICE`, `DD_VERSION`, `DD_LOGS_INJECTION=true`, `DD_RUNTIME_METRICS_ENABLED=true`.
- Logging:
  - **Serilog.AspNetCore**, console sink only (JSON-formatted); the Datadog Agent tails container stdout/stderr and forwards logs. Do not add a separate log-shipping sink/exporter.
  - `DD_LOGS_INJECTION=true` correlates logs with traces automatically (injects `dd.trace_id` / `dd.span_id`).
- Dashboards, monitors, and SLO burn-rate tracking are configured in Datadog (APM, Log Management, Infrastructure monitoring). OpenTelemetry-to-Azure-Monitor exporters are **not used**.

### 4.6 Resilience
- **Polly** (timeouts, retries, circuit breakers) using typed `HttpClient` policies
  - Polly.Extensions.Http (if needed for helpers)

### 4.7 Testing
- **xUnit**
- **AwesomeAssertions** (fluent, drop-in-compatible assertions library — see https://awesomeassertions.org/). Do not add new dependencies on **FluentAssertions**; existing usages must be migrated to AwesomeAssertions.
- **Microsoft.AspNetCore.Mvc.Testing** (API integration tests)
- **Testcontainers for .NET** (recommended for SQL Server integration testing)
- **coverlet.collector** or built-in coverage tool per org CI

### 4.8 Analyzers and style
**Must**
- Enable .NET analyzers + treat warnings as errors in CI.
**Approved**
- **Microsoft.CodeAnalysis.NetAnalyzers**
- **StyleCop.Analyzers** (optional but recommended)
- **Roslynator.Analyzers** (optional)

---

## 5. Reference architecture and project structure

### 5.1 Layering and dependency direction
**Must**
- Dependencies flow inward:
  - `Api -> Application -> Domain`
  - `Infrastructure -> Application` (implements interfaces)
- `Domain` must not depend on EF Core or any external packages.

### 5.2 Reference repo layout (required)
```
/src
  /Company.Product.Service.Api
  /Company.Product.Service.Application
  /Company.Product.Service.Domain
  /Company.Product.Service.Infrastructure
/tests
  /Company.Product.Service.Tests.Unit
  /Company.Product.Service.Tests.Integration
/deploy
  /cdk (AWS CDK infrastructure as code)
/build
  /pipelines (optional)
```

### 5.3 API layer rules
**Must**
- Controllers/minimal endpoints only orchestrate:
  - auth, validation, mapping, calling application services/handlers
- No direct EF Core usage in controllers.
- Standardize responses and errors (RFC 7807 Problem Details).
- Provide OpenAPI descriptions for every endpoint.

### 5.4 Application layer rules
**Must**
- Define use cases (commands/queries/services) that coordinate domain + infrastructure.
- Own transaction boundaries (unit of work) if needed.
- Define interfaces for infrastructure concerns (repositories, external clients).

### 5.5 Domain layer rules
**Must**
- Encode invariants; avoid anemic domain.
- Throw domain exceptions for invariant violations (mapped to 409/422 as appropriate).

### 5.6 Infrastructure layer rules
**Must**
- EF Core DbContext, configurations, migrations.
- External service clients with resilience policies and timeouts.
- No business logic beyond persistence and integration concerns.

---

## 6. API design requirements

### 6.1 Routing, conventions, and status codes
**Must**
- Use resource-oriented routes: `/v1/customers/{customerId}`
- Correct status codes:
  - 200, 201, 204, 400, 401, 403, 404, 409, 422 (if org standard), 429, 500
- Enforce paging for collections:
  - Default page size: 50
  - Max page size: 200
- Support `Idempotency-Key` for create operations that must tolerate retries.

### 6.2 Versioning and deprecation
**Must**
- URL versioning: `/v{major}/...`
- Deprecation policy:
  - Announce at least **90 days** before removal.
  - Return `Deprecation` and `Sunset` headers where applicable.

### 6.3 Content negotiation
**Must**
- `application/json` only unless exception approved.
- Use `System.Text.Json` with deterministic settings (camelCase, enums as strings if chosen).

---

## 7. Error handling and validation

### 7.1 Standard error format
**Must**
- Return RFC7807 Problem Details (or an org-approved envelope) including:
  - `traceId` (from current Activity)
  - stable `errorCode`
  - field errors for validation failures

### 7.2 Centralized exception handling
**Must**
- Single middleware/filter translates exceptions to Problem Details.
- Never leak stack traces or secrets to clients.

### 7.3 Input validation
**Must**
- Validate all requests with FluentValidation.
- Reject payloads exceeding configured max size.
- Normalize and cap string lengths to prevent abuse.

---

## 8. Security requirements

### 8.1 Authentication (API Key — interim)
**Must**
- Require a valid API key on every request unless the endpoint is explicitly annotated `[AllowAnonymous]` (e.g., health checks).
- Keys are opaque, high-entropy (≥ 256 bits), stored only in AWS Secrets Manager, and never committed to source control or logged.
- Support multiple concurrently valid keys per environment to allow zero-downtime rotation.
- Reject requests with missing/invalid keys with `401` and a Problem Details response (no key material in the response body).

### 8.2 Authorization
**Must**
- Policy-based authorization; default deny — endpoints explicitly require a policy (currently `RequireApiKey`).

**Should** (future maturity)
- Scope/role-based checks once a richer identity model is adopted.

### 8.3 Data protection and secrets
**Must**
- TLS 1.2+ (prefer 1.3).
- Use AWS Secrets Manager for secrets; rotate regularly.
- Never log access tokens, API keys, secrets, or raw PII.

### 8.4 PII and compliance
**Must**
- Maintain a PII field inventory and logging redaction rules.
- Audit log security-relevant events (permission changes, admin actions).

---

## 9. Observability requirements (Datadog)

### 9.1 Logging
**Must**
- Structured logs (Serilog) with:
  - `traceId`, `spanId`, `requestId`
  - `route`, `statusCode`, `durationMs`
  - `tenantId`/`clientId` (non-PII where possible)
- Written to console (stdout/stderr) so the Datadog Agent can collect and forward them.

### 9.2 Metrics
**Must**
- Publish RED metrics per endpoint via Datadog APM (dd-trace auto-instrumentation).
- Track EF Core query durations and connection pool usage (dd-trace EF Core integration; custom DogStatsD metrics if additional detail is needed).

### 9.3 Tracing
**Must**
- Propagate distributed trace context via dd-trace (Datadog distributed tracing, W3C trace-context compatible).
- Trace inbound requests and outbound HTTP/SQL dependencies.

### 9.4 Alerting (minimum)
**Must**
- Configure as Datadog Monitors:
  - Availability burn (fast/slow)
  - 5xx error spikes
  - p95 latency regressions
  - Dependency failure rate spikes (Amazon RDS for SQL Server, downstream APIs)

---

## 10. Resilience and reliability

### 10.1 Timeouts
**Must**
- Every outbound dependency call has a timeout:
  - HTTP default timeout: 10s (override per dependency)
  - DB command timeout: 30s (override per query class)

### 10.2 Retries
**Must**
- Retries only for transient failures and idempotent operations.
- Use exponential backoff with jitter and max attempts:
  - max 3 retries for HTTP transient failures (5xx, timeouts, 429 where safe)

### 10.3 Circuit breakers
**Should**
- Circuit breaker for unstable dependencies:
  - open after sustained failures, half-open trial, with telemetry.

---

## 11. EF Core requirements

### 11.1 DbContext and lifetime
**Must**
- Register DbContext with scoped lifetime.
- Disable lazy loading unless explicitly approved.
- Use cancellation tokens on async calls.

### 11.2 Migrations
**Must**
- Migrations stored in source control.
- Production migration strategy:
  - Apply via controlled pipeline step with approvals.
- Never auto-migrate on startup in production.

### 11.3 Query performance
**Must**
- Use `.AsNoTracking()` for read-only queries.
- Avoid N+1 queries; use `Include` judiciously and project to DTOs.
- Index critical query paths and validate with query plans.

---

## 12. Performance requirements
**Must**
- End-to-end async for I/O.
- No blocking calls (`.Result`, `.Wait()`).
- Define and test endpoint budgets against SLOs.
- Enforce paging and upper bounds on query complexity.

---

## 13. Quality gates and CI/CD

### 13.1 Required checks on every PR
**Must**
- Build + unit tests
- Integration tests (at least smoke integration)
- Static analysis (analyzers) with warnings-as-errors
- Dependency vulnerability scan
- OpenAPI generation and contract validation

### 13.2 Coverage targets
**Must**
- Application + Domain: **≥ 80% line coverage**
- Overall solution: **≥ 60% line coverage**
(Adjust per org maturity; do not game coverage.)

### 13.3 Release
**Must**
- Semantic versioning.
- Immutable artifacts; build once, deploy many (same image tag promoted across environments via CDK context parameters).

---

## 14. Operational requirements

### 14.1 Health checks
**Must**
- `/health/live` checks process health only.
- `/health/ready` checks critical dependencies (Amazon RDS for SQL Server connectivity, AWS Secrets Manager access if required).
- The ALB target group / ECS container health check should target `/health/ready`.

### 14.2 Runbooks
**Must**
- Provide runbooks for:
  - Elevated error rates / latency
  - Database throttling / transient failures
  - API key validation failures / Secrets Manager outages
  - Rollback in AWS Fargate: redeploy the prior image tag via the CDK stack/profile

---

## 15. Definition of Done (DoD)
A feature is done only when:
- OpenAPI updated and validated
- Validation + error mapping implemented
- Telemetry (logs/metrics/traces) added and verified in Datadog
- Tests added (unit + integration as appropriate)
- Security policy applied (authz policies, redaction)
- Performance reviewed vs endpoint budgets
- Docs/runbooks updated

---

## Appendix A: Defaults
- Default page size: 50; max 200
- Default outbound HTTP timeout: 10s
- Default DB command timeout: 30s
- Availability SLO: 99.9% monthly
- Read latency: p95 ≤ 200ms; write latency: p95 ≤ 500ms
- Default Fargate task sizing: 1024 CPU units / 2048 MiB memory (override per service/environment)
- Default autoscaling target: 80% CPU / 80% memory, min 1 / max 5 tasks (override per environment)

