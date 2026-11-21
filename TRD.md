# Technical Requirements Document (TRD)
## Enterprise-grade C#/.NET 10 API Backend (API Key Auth + EF Core + AWS Fargate)

**Document owner:** Engineering Platform  
**Last updated:** 2026-11-21
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
- Inject secret values through the ECS task definition's `secrets` mapping and grant the ECS task execution role only `secretsmanager:GetSecretValue` access to the specific secrets required at startup. The application task role must not receive Datadog secrets merely because it hosts a Datadog sidecar.
- Rotate Datadog API keys independently of application credentials. Revoked or rotated keys must not require an application image rebuild.
- Validate required configuration at startup; fail fast with clear error.

### 3.3 Deployment strategy (Infrastructure as Code via AWS CDK)
**Must**
- Define all infrastructure in TypeScript AWS CDK (see `deploy/cdk`), with one stack deployment per environment profile (`sandbox`, `staging`, `uat`, `prod`) under `deploy/cdk/lib/profiles/`. Each profile parameterizes: ECR repository names, VPC name (pre-existing, looked up by name), domain/subdomain for Route53 + ACM, DB security group id, Secrets Manager secret name, Fargate task/service CPU & memory sizing, autoscaling targets, and container environment variables.
- Support the following stack patterns:
  - **API/service stack** (`SidekickStack` pattern): an `ApplicationLoadBalancedFargateService` per API, running the application container, an essential FireLens log-router container, and a **Datadog Agent sidecar** container.
  - **Scheduled worker stack** (`KnowledgeBaseIngestionStack` pattern): one-off Fargate tasks triggered by an EventBridge Scheduler (cron) for batch/background jobs, paired with the same FireLens log router and Datadog Agent sidecar.
  - **Repository stack** (`KnowledgeBaseIngestionRepositoryStack` pattern): dedicated ECR repositories for worker images with image scan-on-push, AES-256 encryption, `RETAIN` removal policy, and a lifecycle rule expiring untagged images after 30 days.
- Configure application containers with the ECS `awsfirelens` log driver and a Fluent Bit configuration that:
  - Parses the application's compact JSON `log` field into top-level fields without re-serializing it as a string.
  - Adds ECS metadata needed for operational diagnosis.
  - Sends records to a dedicated, environment-specific CloudWatch Logs log group.
  - Retries transient CloudWatch writes and exposes delivery failures as infrastructure telemetry.
- Configure the Datadog Forwarder subscription as code. Store its Datadog API key in Secrets Manager and grant read/write permissions only to the deployment execution path and forwarding component that require it.
- Use immutable, version-pinned FireLens and Datadog Agent images or validated image digests. `latest` is prohibited outside local development.
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
- Required application environment variables (set via the CDK task definition): `DD_ENV`, `DD_SERVICE`, `DD_VERSION`, `DD_LOGS_INJECTION=true`, and `DD_RUNTIME_METRICS_ENABLED=true`. `DD_TRACE_AGENT_URL` must explicitly target the task-local Datadog Agent.
- The Datadog Agent sidecar receives `DD_API_KEY` from ECS secret injection, plus `DD_SITE`, and `ECS_FARGATE=true`. The application container and log router must never receive or resolve `DD_API_KEY`.
- Logging:
  - **Serilog.AspNetCore** writes compact, newline-delimited JSON to stdout/stderr.
  - A deployed JSON formatter such as **Serilog.Formatting.Compact**, **Serilog.Expressions**, or an organization-approved `ITextFormatter` must emit the canonical field names in section 9.1.1. Stock compact metadata keys such as `@t` or `@l` are acceptable only if an auditable FireLens/Datadog pipeline immediately normalizes them to `timestamp` and `status`.
  - **Serilog.Sinks.File** is permitted only for the development local-analysis mirror described in section 9.6. It must never be enabled in a production task definition.
  - Application-owned redaction/destructuring policies are required even if Datadog Sensitive Data Scanner is also configured.
  - ECS FireLens receives application stdout/stderr and writes to a dedicated CloudWatch Logs log group. The Datadog Forwarder subscribes to the log group and forwards the records to Datadog.
  - CloudWatch is the durable infrastructure handoff; do not configure direct application log submission, a second Datadog log exporter, or Agent stdout tailing. This prevents duplicate ingestion and keeps the application independent of a Datadog API key.
  - `DD_LOGS_INJECTION=true` injects Datadog's Serilog-compatible correlation properties (`dd_trace_id` / `dd_span_id`) for Datadog log pipelines to remap into the reserved `trace_id` / `span_id` attributes.
- Dashboards, monitors, log pipelines, indexes, facets, and SLO burn-rate tracking are configured as code where the organization's Datadog tooling permits. OpenTelemetry-to-Azure-Monitor exporters are **not used**.

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
  - W3C `traceId` and `spanId`
  - Datadog-compatible `dd_trace_id` and `dd_span_id` when a Datadog trace is active
  - `http.request_id`, `http.method`, `http.route`, `http.status_code`
  - `duration` (nanoseconds, as required by Datadog's standard duration attribute)
  - `tenantId`/`clientId` (non-PII where possible)
- Written as compact, one-event-per-line JSON to console (stdout/stderr); ECS FireLens must parse the embedded JSON record into top-level fields.
- Use a Serilog JSON formatter; do not rely on Serilog's default console text formatter for any deployed environment. If the selected formatter cannot expose the canonical names directly, normalize its metadata before forwarding and contract-test the resulting CloudWatch event.
- Emit `timestamp` as UTC ISO 8601, `status` as a normalized level (`debug`, `info`, `warn`, `error`, or `critical`), and `message` as the rendered message.
- Keep message templates and structured properties. Never pre-concatenate variable values into the message.
- Configure minimum levels by environment. Production must not emit routine `Debug`/`Trace` events; sampling must be applied to high-volume events rather than disabling error telemetry.
- Redact secrets, authorization headers, cookies, tokens, raw request/response bodies, database connection strings, and PII before the event is serialized. Destructuring policies must be configured to prevent accidental capture of sensitive object graphs.
- Limit exceptions to safe type, message, and stack fields. Apply approved normalization/redaction before logging exception data that can contain user or infrastructure details.
- Do not use high-cardinality customer, user, request, URL-identifier, API-key, or SQL-parameter values as Datadog metric tags. Such values may be log attributes only when required, approved, and non-PII.

#### 9.1.1 Canonical log-event schema

The canonical application log event is flat newline-delimited JSON. Datadog-specific correlation fields use underscores because they are valid and interoperable Serilog property names.

```json
{
  "timestamp": "2026-11-21T12:34:56.789Z",
  "status": "info",
  "source": "csharp",
  "service": "company-product-service",
  "env": "prod",
  "version": "2026.11.21-sha.abcdef1",
  "message": "Completed HTTP request",
  "event.category": "http",
  "event.name": "request.completed",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7",
  "dd_env": "prod",
  "dd_service": "company-product-service",
  "dd_version": "2026.11.21-sha.abcdef1",
  "dd_trace_id": "1234567890123456789",
  "dd_span_id": "123456789012345678",
  "http.request_id": "0HN...",
  "http.method": "GET",
  "http.route": "/v1/customers/{customerId}",
  "http.url_details.path": "/v1/customers/123",
  "http.status_code": 200,
  "duration": 12400000,
  "logger.name": "Microsoft.AspNetCore.Hosting.Diagnostics",
  "error.kind": null,
  "error.code": null
}
```

Schema rules:

- `timestamp`, `status`, `source`, `service`, `env`, `version`, `message`, and event identity are required for every emitted event.
- `traceId`/`spanId` preserve W3C `Activity` values for application, Problem Details, and local diagnosis. They are strings.
- `dd_trace_id`/`dd_span_id` preserve Datadog's lower-64-bit decimal correlation values and are emitted as strings to avoid JSON numeric precision loss. They are present only while a Datadog trace is active.
- A Datadog log pipeline must define the C# source and use specialized trace/span remappers for `dd_trace_id` and `dd_span_id`. The generic attribute remapper must not be used for Datadog reserved `trace_id` or `span_id`.
- `source: csharp`, `service`, `status`, and `message` are Datadog-reserved or integration fields. Their values must not be replaced by unrelated custom fields.
- `duration` is nanoseconds. If Serilog records a millisecond value for local readability, it must use a distinct name such as `duration_ms`; the forwarding pipeline must convert only the canonical `duration` field and must not overwrite it.
- Use normalized route templates in `http.route`. Never emit the full URL, query string, authentication header, or a concrete resource identifier in a field intended for aggregation.
- `http.url_details.path` is diagnostic-only and must be included only when IDs and other sensitive path segments have been reviewed. Prefer omitting it for high-sensitivity endpoints.
- `tenantId` and `clientId` are optional bounded attributes, not metric tags. Use opaque identifiers, never names, email addresses, or other direct identifiers.
- When supplied by dd-trace auto-instrumentation, `dd_env`, `dd_service`, and `dd_version` are duplicate compatibility aliases for `env`, `service`, and `version`. Their values must be identical. Pipelines may remove the duplicate aliases after correlation is established, but must not rely on them to repair inconsistent canonical fields.

#### 9.1.2 Log pipeline and retention

**Must**
- Manage Datadog log pipelines, parsers, trace/span remappers, service/status/message/date remappers, facets, and aliases as code or through an auditable export-and-review process.
- Create a C# pipeline that maps `dd_trace_id` and `dd_span_id` to Datadog's reserved correlation attributes while retaining W3C `traceId`/`spanId` as searchable attributes.
- Use indexed facets sparingly and prefer standard attributes (`http.*`, `duration`, `logger.*`, `error.*`, `usr.*`).
- Define per-environment log indexes, retention, exclusions, and Sensitive Data Scanner rules. CloudWatch retention must be independently bounded and approved.
- Exclude expected health-check noise, but never exclude unhandled errors, security-audit events, or failed downstream calls.
- Monitor forwarding lag, dropped/rejected records, Forwarder errors, CloudWatch throttling, and logs crossing the 5-minute telemetry freshness SLO.

### 9.2 Metrics
**Must**
- Publish RED metrics per endpoint via Datadog APM (dd-trace auto-instrumentation).
- Track EF Core query durations and connection pool usage (dd-trace EF Core integration; custom DogStatsD metrics if additional detail is needed).
- Publish only bounded metric tags such as `env`, `service`, `version`, route template, method, status class, and dependency type.
- Define metric units and names in a shared catalog. Normalize route templates before tagging and prohibit raw paths, IDs, SQL, tenant IDs, or user identifiers as tags.
- Specify the expected custom-metric volume and review it during capacity and cost planning.

### 9.3 Tracing
**Must**
- Propagate distributed trace context via dd-trace (Datadog distributed tracing, W3C trace-context compatible).
- Trace inbound requests and outbound HTTP/SQL dependencies.
- Configure unified service tagging consistently on the application container through `DD_ENV`, `DD_SERVICE`, and `DD_VERSION` and matching ECS Docker labels (`com.datadoghq.tags.env`, `.service`, and `.version`).
- Use the task-local Datadog Agent for APM ingestion. The tracer endpoint must not be exposed publicly and must resolve to `localhost`/the sidecar endpoint.
- Configure trace sampling and ingestion controls per environment. Preserve all error traces and use representative sampling for normal traffic when volume requires it.
- Do not sample or drop security-audit and data-integrity events from the log stream.
- Add explicit custom spans for meaningful business operations and background jobs, including stable span names and bounded resource names. Do not expose SQL parameters, payloads, API keys, or raw identifiers as span tags.
- Verify W3C `traceparent`/`tracestate` propagation at service boundaries and avoid generating a new trace for an existing request context.

### 9.4 Alerting (minimum)
**Must**
- Configure as Datadog Monitors:
  - Availability burn (fast/slow)
  - 5xx error spikes
  - p95 latency regressions
  - Dependency failure rate spikes (Amazon RDS for SQL Server, downstream APIs)
- Monitor telemetry health separately from application health:
  - Agent sidecar unavailable/unhealthy
  - FireLens delivery failures and CloudWatch ingestion errors
  - Datadog Forwarder failures, dead-letter queue depth, and forwarding lag
  - trace-ingestion, log-index, and custom-metric volume anomalies
- Manage monitor ownership, escalation policy, warning/critical thresholds, notification channels, and evaluation windows as environment-specific configuration.
- Alert on SLO burn rates rather than only static thresholds. Alerts must identify service, environment, version, and affected endpoint without requiring a unique tag per request.

### 9.5 Operational configuration and resource isolation
**Must**
- Use a pinned, approved Datadog Agent image and pin its CPU/memory reservation in the task definition. Include a health check and collect its container logs.
- Set `DD_SITE` explicitly for the selected Datadog site on the Agent and forwarding component. Do not rely on the default.
- Set `ECS_FARGATE=true` on the Agent. Application containers receive `DD_*` tracer settings but no Datadog API key and no direct log-submission integration settings.
- Keep log transport independent from APM: the Datadog Agent receives traces, DogStatsD, runtime, and container metrics; FireLens/CloudWatch/Forwarder receives logs. A failure in either path must not silently disable the other.
- Mark the FireLens log router essential. Mark the APM Agent non-essential unless product reliability policy explicitly requires application shutdown when APM is unavailable; agent failure must produce an operational alert.
- Configure container dependency ordering so the application waits for the required log router and, if needed, a healthy Agent. Do not introduce a hard application startup dependency on Datadog network availability.
- Define graceful-shutdown behavior so the application flushes tracing data, stops accepting work, and allows the log router/Agent time to drain before the task exits.
- Validate the complete path in non-production: console JSON → FireLens → CloudWatch → Forwarder → Datadog, including trace/log correlation and failure recovery.

### 9.6 Local and AI-assisted diagnosis
**Must**
- The application must use the same canonical event schema locally and in deployed environments. Local execution may mirror console JSON to a rolling `.jsonl` file for repository-local or AI-assisted analysis; this file sink is development-only and disabled in production.
- Store local telemetry under a git-ignored, configurable directory such as `.telemetry/`, with daily files, size caps, and short retention (recommended: 7 days and ≤ 500 MiB). It must never be committed.
- Apply the same redaction and PII policy before writing local or deployed logs. Disabling Datadog must not disable redaction.
- Local files should be one-record-per-line JSON so agents and standard tools can filter by fields and correlate events by `traceId`. Include a small manifest with service, environment, version, start time, retention, and active exporters.
- For local trace analysis, provide a documented development-only OpenTelemetry/OTLP configuration that targets a local collector. The collector may write OTLP JSON to `.telemetry/` or expose a local trace UI.
- Do not run local OpenTelemetry tracing and production-style dd-trace injection simultaneously unless explicitly validated; use a configuration switch to select one tracing mode and avoid duplicate spans.
- Local metrics should be exported as bounded JSON or Prometheus/OTLP files and use the same service, endpoint, and dependency names as production.

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
- Logging contract tests:
  - One console line parses as one valid JSON event.
  - Required canonical fields are present with correct types and no secret/PII fixture values.
  - W3C trace/span IDs and Datadog correlation fields survive through the request log middleware.
  - Durations are emitted using the documented units.
- Observability infrastructure tests:
  - The application container has exactly one log driver and cannot receive `DD_API_KEY`.
  - Datadog API keys are present only in approved sidecar/Forwarder secret injection.
  - The task uses pinned FireLens and Datadog images, matching unified service labels, bounded retention, and the expected CloudWatch/Forwarder resources.

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
  - Datadog Agent unavailability and APM/DogStatsD delivery interruption
  - FireLens, CloudWatch, or Datadog Forwarder delivery failures; duplicate-ingestion or correlation failures
  - Missing, malformed, redaction-violating, or high-volume telemetry
  - Rollback in AWS Fargate: redeploy the prior image tag via the CDK stack/profile

---

## 15. Definition of Done (DoD)
A feature is done only when:
- OpenAPI updated and validated
- Validation + error mapping implemented
- Canonical structured logs, metrics, and traces added and verified end to end from the application through Datadog
- Trace/log correlation verified with the canonical W3C and Datadog fields
- Log redaction and metric tag cardinality reviewed with tests
- FireLens/CloudWatch/Forwarder routing tested without exposing a Datadog API key to the application
- Local JSONL/OTLP diagnostic path verified for development and confirmed disabled in production
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
- Default log transport: console JSON → FireLens → CloudWatch Logs → Datadog Forwarder
- Default local telemetry retention: 7 days, maximum 500 MiB, git-ignored and development-only

## Appendix B: Datadog observability references
- [Correlating .NET Logs and Traces](https://docs.datadoghq.com/tracing/other_telemetry/connect_logs_and_traces/dotnet/)
- [Log Attributes and Aliasing](https://docs.datadoghq.com/logs/log_configuration/attributes_naming_convention/)
- [Default Standard Attributes](https://docs.datadoghq.com/standard-attributes/?product=log+management)
- [Unified Service Tagging](https://docs.datadoghq.com/getting_started/tagging/unified_service_tagging/)
- [Amazon ECS on AWS Fargate: Log Collection](https://docs.datadoghq.com/integrations/ecs_fargate/#log-collection)
- [Trace Remapper](https://docs.datadoghq.com/logs/log_configuration/processors/trace_remapper/)

