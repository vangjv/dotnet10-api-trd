# Technical Requirements Document (TRD)
## Enterprise-grade C#/.NET 10 API Backend (Azure AD + EF Core + Azure Container Apps)

**Document owner:** Engineering Platform  
**Last updated:** 2026-03-01  
**Target runtime:** .NET 10 (LTS/enterprise standard)  
**Primary hosting:** Azure Container Apps (ACA)  
**Auth standard:** Microsoft Entra ID (Azure AD)  
**Persistence:** EF Core (SQL Server/Azure SQL by default)

---

## 1. Purpose and scope
This TRD defines **mandatory engineering rules** and **testable technical requirements** for building and operating an enterprise-grade HTTP API backend in C#/.NET 10 on Azure Container Apps.

### In scope
- REST APIs built with ASP.NET Core
- Microsoft Entra ID authentication + policy-based authorization
- EF Core persistence layer and migrations
- Observability (logs, metrics, distributed tracing) via Azure Monitor
- Resilience, performance, security requirements
- CI/CD quality gates and operational runbooks

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
- Telemetry data should reach Azure Monitor within **≤ 5 minutes** for operational usefulness.

### 2.2 Service Level Indicators (SLIs)
**Must measure**
- Request rate, error rate (by status code class), duration
- Dependency duration + failures (Azure SQL, HTTP dependencies)
- Resource utilization: CPU, memory, GC, thread pool, connection pools

### 2.3 Capacity and scaling
**Must**
- Scale horizontally via ACA revisions/scale rules.
- Define target concurrency and scaling trigger(s) per service:
  - HTTP concurrency (requests) and/or CPU
- Define max replica count per service and per environment.

---

## 3. Platform and hosting requirements (Azure Container Apps)

### 3.1 Container requirements
**Must**
- Build Linux containers.
- Run as non-root user where feasible.
- Provide readiness and liveness probes:
  - `GET /health/live`
  - `GET /health/ready`

**Must**
- Use **Managed Identity** for Azure resources where supported (Key Vault, Azure SQL with AAD, App Configuration).
- Configure **ingress** to HTTPS only; TLS terminated at platform edge.

### 3.2 Configuration and secrets
**Must**
- Use **Azure App Configuration** for non-secret configuration (recommended).
- Use **Azure Key Vault** for secrets. No secrets in code, images, or plain environment variables outside approved secret mounts.
- Validate required configuration at startup; fail fast with clear error.

### 3.3 Deployment strategy
**Must**
- Use ACA revisions with controlled rollout:
  - Rolling or blue/green using revisions
- Every deployment must be auditable and reproducible.

---

## 4. Approved libraries and standards

### 4.1 Web/API
- **ASP.NET Core (.NET 10)**
- **Swashbuckle.AspNetCore** (OpenAPI/Swagger generation)
- **Asp.Versioning.Mvc** (API versioning) – URL versioning preferred (`/v1/...`)
- **Microsoft.AspNetCore.OpenApi** may be used if org standard, but be consistent.

### 4.2 Authentication/Authorization (Entra ID)
- **Microsoft.Identity.Web** (JWT bearer + token validation + downstream API support)
- Policy-based authorization via `IAuthorizationPolicyProvider` / `AddAuthorization`

### 4.3 Data access
- **Microsoft.EntityFrameworkCore**
- Provider package per DB (e.g., **Microsoft.EntityFrameworkCore.SqlServer**)

### 4.4 Validation and mapping
- **FluentValidation.AspNetCore** (request validation)
- Mapping:
  - Approved: **AutoMapper** *or* **Mapster** (choose 1 per service; org default: AutoMapper)

### 4.5 Observability
- **OpenTelemetry**:
  - OpenTelemetry.Extensions.Hosting
  - OpenTelemetry.Instrumentation.AspNetCore
  - OpenTelemetry.Instrumentation.Http
  - OpenTelemetry.Instrumentation.EntityFrameworkCore
- **Azure.Monitor.OpenTelemetry.Exporter** (send traces/metrics/logs to Azure Monitor)
- Logging:
  - **Serilog.AspNetCore**
  - Sinks: Console + (optional) Application Insights sink if required by org

### 4.6 Resilience
- **Polly** (timeouts, retries, circuit breakers) using typed `HttpClient` policies
  - Polly.Extensions.Http (if needed for helpers)

### 4.7 Testing
- **xUnit**
- **FluentAssertions**
- **Microsoft.AspNetCore.Mvc.Testing** (API integration tests)
- **Testcontainers for .NET** (recommended for Azure SQL / SQL Server integration testing)
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

### 8.1 Authentication (Entra ID)
**Must**
- Validate issuer, audience, signature, expiry, and required scopes.
- Disable weak algorithms and reject unsigned tokens.

### 8.2 Authorization
**Must**
- Policy-based authorization with scope/role checks.
- Default deny; endpoints explicitly require a policy.

### 8.3 Data protection and secrets
**Must**
- TLS 1.2+ (prefer 1.3).
- Use Key Vault for secrets; rotate regularly.
- Never log access tokens, secrets, or raw PII.

### 8.4 PII and compliance
**Must**
- Maintain a PII field inventory and logging redaction rules.
- Audit log security-relevant events (permission changes, admin actions).

---

## 9. Observability requirements (Azure Monitor + OpenTelemetry)

### 9.1 Logging
**Must**
- Structured logs (Serilog) with:
  - `traceId`, `spanId`, `requestId`
  - `route`, `statusCode`, `durationMs`
  - `tenantId`/`clientId` (non-PII where possible)

### 9.2 Metrics
**Must**
- Publish RED metrics per endpoint.
- Track EF Core query durations and connection pool usage.

### 9.3 Tracing
**Must**
- Propagate W3C trace context.
- Trace inbound requests and outbound HTTP/SQL dependencies.

### 9.4 Alerting (minimum)
**Must**
- Alerts for:
  - Availability burn (fast/slow)
  - 5xx error spikes
  - p95 latency regressions
  - Dependency failure rate spikes (Azure SQL, downstream APIs)

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
- Immutable artifacts; build once, deploy many.

---

## 14. Operational requirements

### 14.1 Health checks
**Must**
- `/health/live` checks process health only.
- `/health/ready` checks critical dependencies (Azure SQL connectivity, Key Vault access if required).

### 14.2 Runbooks
**Must**
- Provide runbooks for:
  - Elevated error rates / latency
  - Azure SQL throttling / transient failures
  - Token validation / Entra ID outages
  - Rollback in ACA using revision traffic

---

## 15. Definition of Done (DoD)
A feature is done only when:
- OpenAPI updated and validated
- Validation + error mapping implemented
- Telemetry (logs/metrics/traces) added and verified
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

