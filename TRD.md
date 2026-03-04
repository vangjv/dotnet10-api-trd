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

**Reference Dockerfile**
```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish ./src/Company.Product.Service.Api/Company.Product.Service.Api.csproj \
    -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Run as non-root user (TRD §3.1)
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Company.Product.Service.Api.dll"]
```

**Must**
- Use **Managed Identity** for Azure resources where supported (Key Vault, Azure SQL with AAD, App Configuration).
- Configure **ingress** to HTTPS only; TLS terminated at platform edge.
- Include a **`.dockerignore`** file to prevent tests, docs, `.git`, `bin/`, and `obj/` directories from entering the build context. Without this, `COPY . .` adds unnecessary files that bust layer cache and may include sensitive artifacts.

  **Minimum `.dockerignore`:**
  ```
  **/.git
  **/.vs
  **/bin
  **/obj
  **/TestResults
  tests/
  docs/
  **.md
  ```

### 3.2 Configuration and secrets
**Must**
- Use **Azure App Configuration** for non-secret configuration (recommended).
- Use **Azure Key Vault** for secrets. No secrets in code, images, or plain environment variables outside approved secret mounts.
- Validate required configuration at startup; fail fast with clear error.
- **Never commit placeholder values** (e.g., `YOUR_TENANT_ID`, `YOUR_CLIENT_ID`) in checked-in configuration files. Use:
  - Empty strings in `appsettings.json` for required Entra ID values.
  - `.NET User Secrets` (`dotnet user-secrets set "AzureAd:TenantId" "<guid>"`) for local development overrides.
  - Environment variables (e.g., `AzureAd__TenantId`) for containers, CI, and staging.

  Placeholder text in committed files is a stepping stone to accidentally committing real credentials.

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
- **FluentValidation** + **FluentValidation.DependencyInjectionExtensions** (request validation)
  > ⚠️ `FluentValidation.AspNetCore` is **deprecated** — do **not** reference it.
  - Register validators in the **Application layer's `AddApplication()` extension method** via `AddValidatorsFromAssemblyContaining<T>()` where `T` is a type in the Application assembly.
  - **Never** call `AddFluentValidationAutoValidation()` — it conflicts with manual validation and produces inconsistent `ValidationProblemDetails` shapes.
  - **Never** call `AddValidatorsFromAssemblyContaining<Program>()` from `Program.cs` — all validators live in the Application assembly, making this a no-op that silently leaves validators unregistered.
  - Use **manual validation** in each controller action: inject `IValidator<T>`, call `ValidateAsync`, and return `ValidationProblemDetails` on failure.

  **Correct pattern (Application layer `DependencyInjection.cs`):**
  ```csharp
  // Application/DependencyInjection.cs
  public static IServiceCollection AddApplication(this IServiceCollection services)
  {
      services.AddScoped<IMyService, MyService>();
      // Register all validators from this assembly — the APPLICATION assembly.
      services.AddValidatorsFromAssemblyContaining<MyService>();
      return services;
  }
  ```

  **Correct manual validation pattern (controller action):**
  ```csharp
  public async Task<IActionResult> Create(
      [FromBody] CreateTodoItemRequest request,
      CancellationToken cancellationToken)
  {
      var validation = await _validator.ValidateAsync(request, cancellationToken);
      if (!validation.IsValid)
          return ValidationProblem(validation.ToDictionary());
      // ...
  }
  ```
- Mapping:
  - **Manual mapping via extension methods** (preferred for simple models — compile-time safe, no reflection)
  - No third-party mapping library (e.g., AutoMapper, Mapster) required. If one is adopted per service, ensure it is consistent throughout that service.

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
- **Microsoft.Extensions.Http.Resilience** (wraps **Polly v8** for `HttpClient` resilience pipelines)
  - Provides `AddStandardResilienceHandler()` with built-in retry, circuit breaker, and timeout
  - Configure per-attempt timeout, total timeout, retry count, backoff strategy, and jitter
- For non-HTTP resilience (e.g., custom pipelines), use **Polly** v8+ directly (`ResiliencePipelineBuilder`)

### 4.7 Testing
- **xUnit**
- **FluentAssertions**
- **Microsoft.AspNetCore.Mvc.Testing** (API integration tests)
- **Testcontainers for .NET** (recommended for Azure SQL / SQL Server integration testing)
- **coverlet.collector** or built-in coverage tool per org CI

### 4.8 Analyzers and style
**Must**
- Enable .NET analyzers + treat warnings as errors in CI.
- Use a **`Directory.Build.props`** file at the repository root to enforce analyzer settings across all projects from a single place.

  **Reference `Directory.Build.props`:**
  ```xml
  <Project>
    <PropertyGroup>
      <Nullable>enable</Nullable>
      <ImplicitUsings>enable</ImplicitUsings>
      <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
      <AnalysisLevel>latest</AnalysisLevel>
      <GenerateDocumentationFile>true</GenerateDocumentationFile>
      <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="x.x.x" PrivateAssets="all" />
      <PackageReference Include="StyleCop.Analyzers" Version="x.x.x" PrivateAssets="all" />
    </ItemGroup>
  </Project>
  ```

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

**Required layer responsibilities:**
| Layer | External packages allowed | Notes |
|-------|--------------------------|-------|
| Domain | _(none)_ | Pure C# only; no framework or ORM references |
| Application | FluentValidation, DI/Logging abstractions | Owns interfaces for infra concerns |
| Infrastructure | EF Core, provider, resilience | Implements Application interfaces |
| Api | ASP.NET Core, Identity, OTel, Serilog | Composition root only |

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
- **`Program.cs` must not reference any infrastructure types directly** (e.g., `AppDbContext`, concrete repositories). All infrastructure setup — including health checks and database initialization — must be encapsulated in `AddInfrastructure()` and a companion `UseInfrastructure()` / `InitializeDatabaseAsync()` extension on `IHost` or `WebApplication` defined in the Infrastructure layer.
- **`DevAuthHandler` must live in a dedicated file** (e.g., `Auth/DevAuthHandler.cs` in the API project), not inline in `Program.cs`. Embedding a 30+ line class in the composition root adds noise and obscures startup logic.
- **Swagger/OpenAPI service registration must be gated behind `IsDevelopment()`** to avoid unnecessary overhead in production environments.

  ```csharp
  // ✅ Correct composition root — no infrastructure types referenced directly
  builder.Services.AddApplication();
  builder.Services.AddInfrastructure(builder.Configuration); // health checks registered inside
  // ...
  await app.InitializeDatabaseAsync(); // defined in Infrastructure; handles dev EnsureCreated
  ```

### 5.4 Application layer rules
**Must**
- Define use cases (commands/queries/services) that coordinate domain + infrastructure.
- Own transaction boundaries (unit of work) if needed.
- **Define interfaces for infrastructure concerns (repositories, external clients) in an `Interfaces/` folder** within the Application project. Concrete implementations live in Infrastructure.
- **Register all layer services (including validators) in a single `AddApplication()` extension method** on `IServiceCollection`. The composition root should call only this method — never register individual application services from `Program.cs`.
- **Use `sealed record` with `init`-only required properties for all request/response DTOs** to prevent mutation and ensure immutability:
  ```csharp
  public sealed record CreateTodoItemRequest
  {
      public required string Title { get; init; }
      public required Priority Priority { get; init; }
  }
  ```
- **Service interfaces** (`IMyService`) should be co-located with their DTOs/models or placed in `Services/`, and **must not** be in the same folder as `ITodoItemRepository`-style repository interfaces (which live in `Interfaces/`). This is an **intentional distinction** — not an inconsistency:
  ```
  Application/
    Interfaces/
      ITodoItemRepository.cs   ← infrastructure abstraction (implemented in Infrastructure)
    Services/
      ITodoItemService.cs      ← application service interface (co-located with implementation)
      TodoItemService.cs
  ```

### 5.5 Domain layer rules
**Must**
- Encode invariants; avoid anemic domain.
- All mutation must go through **behavioral methods** (`SetTitle`, `TransitionTo`, etc.). Entity properties must use `private set`; never expose public setters on mutable state.
- Constructors must **validate all preconditions** before assigning state.
- Throw typed domain exceptions for invariant violations — see §7.2 for the full exception hierarchy.

**State machines**
- Use an **explicit static `IsValidTransition(from, to)`** helper with a pattern-matching switch expression. Document valid and terminal transitions in XML `<remarks>`. Idempotent transitions (same state → same state) must be no-ops, not errors.
  ```csharp
  internal static bool IsValidTransition(MyStatus from, MyStatus to) =>
      (from, to) switch
      {
          (MyStatus.Pending, MyStatus.InProgress) => true,
          (MyStatus.Completed, _) => false, // terminal
          _ => false,
      };
  ```

**Constants — single source of truth across layers**
- Define field-length and other limits (`MaxTitleLength`, `MaxDescriptionLength`) as `public const` on the domain entity. Reference them from EF Core `IEntityTypeConfiguration<T>` and FluentValidation validators — **never duplicate literal values**.

**XML documentation**
- Every public member, constructor parameter, domain event, and interface method must have XML documentation. State machines must document valid transitions in `<remarks>`.

**`TimeProvider` — clock abstraction**
- **Never call `DateTimeOffset.UtcNow` directly** inside a domain entity. Inject `TimeProvider` to make time-dependent behaviour testable without `BeCloseTo` workarounds.
- Accept `TimeProvider? timeProvider = null` in the constructor (defaulting to `TimeProvider.System`).
  ```csharp
  private readonly TimeProvider _timeProvider;

  public MyEntity(Guid id, string name, TimeProvider? timeProvider = null)
  {
      _timeProvider = timeProvider ?? TimeProvider.System;
      CreatedAtUtc = _timeProvider.GetUtcNow();
      UpdatedAtUtc = CreatedAtUtc;
  }

  // ❌ Wrong — couples to wall clock; tests must use BeCloseTo(UtcNow, 5s)
  // CreatedAtUtc = DateTimeOffset.UtcNow;
  ```
- Register `TimeProvider.System` in DI inside the Application `AddApplication()` extension method:
  ```csharp
  services.AddSingleton(TimeProvider.System);
  ```
- Services that construct domain entities receive `TimeProvider` as a constructor dependency and pass it through.
- In tests, use `FakeTimeProvider` from the `Microsoft.Extensions.TimeProvider.Testing` package for exact time assertions:
  ```csharp
  var clock = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
  var entity = new MyEntity(Guid.NewGuid(), "Test", clock);
  entity.CreatedAtUtc.Should().Be(clock.GetUtcNow()); // exact — no tolerance needed
  ```

**`UpdatedAtUtc` audit timestamp**
- All entities that support mutation **must carry an `UpdatedAtUtc` property** for audit trails and cache-invalidation support. Set it equal to `CreatedAtUtc` in the constructor; update it in every mutating method via a private `Touch()` helper:
  ```csharp
  public DateTimeOffset UpdatedAtUtc { get; private set; }

  private void Touch() => UpdatedAtUtc = _timeProvider.GetUtcNow();
  ```

**`EnsureMutable()` guard**
- Entities with terminal states (Completed, Cancelled, etc.) **must expose a public `EnsureMutable()` method** that throws `DomainException` when the entity can no longer be mutated. Call it at the top of every behavioral method that modifies non-status state:
  ```csharp
  /// <summary>Throws if the item is in a terminal state and cannot be mutated.</summary>
  /// <exception cref="DomainException">Thrown with code <c>ITEM_NOT_MUTABLE</c>.</exception>
  public void EnsureMutable()
  {
      if (Status is MyStatus.Completed or MyStatus.Cancelled)
          throw new DomainException(
              "ITEM_NOT_MUTABLE",
              $"Cannot modify an item in '{Status}' status. Re-open it first.");
  }

  public void SetTitle(string newTitle)
  {
      EnsureMutable(); // guard first
      // ... validation and assignment
      Touch();
  }
  ```

**Optimistic concurrency control**
- Entities that can be mutated concurrently **must carry a `RowVersion` concurrency token** to prevent lost-update anomalies (e.g. two clients both reading `Pending` and both transitioning it simultaneously).
- Declare the property on the domain entity; configure it in EF Core:
  ```csharp
  // Domain entity
  public byte[]? RowVersion { get; private set; }

  // IEntityTypeConfiguration<T>
  builder.Property(x => x.RowVersion).IsRowVersion();
  ```
- Services should catch `DbUpdateConcurrencyException` and translate it to HTTP 409 Conflict.

### 5.6 Infrastructure layer rules
**Must**
- EF Core DbContext, configurations, migrations.
- External service clients with resilience policies and timeouts.
- No business logic beyond persistence and integration concerns.
- **Expose a single `AddInfrastructure(IServiceCollection, IConfiguration)` extension method** that registers all infrastructure services including: DbContext, repositories, health checks.
- **Expose an `InitializeDatabaseAsync(this IHost)` extension method** for development database initialization. This keeps infrastructure concerns out of the composition root:
  ```csharp
  public static async Task InitializeDatabaseAsync(this IHost host)
  {
      using var scope = host.Services.CreateScope();
      var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
      if (env.IsDevelopment())
      {
          var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
          await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
      }
  }
  ```
- **Use `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`** in `OnModelCreating` to auto-discover all `IEntityTypeConfiguration<T>` implementations. This avoids having to manually register each entity configuration:
  ```csharp
  protected override void OnModelCreating(ModelBuilder modelBuilder)
      => modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
  ```
- Keep entity constants (`MaxTitleLength`, etc.) on the **domain entity**, referenced by both in EF configuration and FluentValidation validators — single source of truth.

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
- **For MVC controller applications, configure JSON options on `AddControllers()`** via `.AddJsonOptions()`. Do **not** use `ConfigureHttpJsonOptions()` — this applies to minimal API endpoints only and has no effect on MVC controllers.

  ```csharp
  // ✅ Correct for MVC controller applications
  builder.Services
      .AddControllers()
      .AddJsonOptions(options =>
      {
          options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
          options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
          options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
      });

  // ❌ Wrong — only affects minimal APIs, not MVC controllers
  // builder.Services.ConfigureHttpJsonOptions(...);
  ```
- All **mutation endpoints** (POST, PUT, PATCH) **must** declare `[Consumes("application/json")]`. Without this, the framework accepts requests with any `Content-Type`, producing unpredictable binding behaviour and misleading OpenAPI docs.

  ```csharp
  // ✅ Correct — declares accepted content type explicitly
  [HttpPost]
  [Produces("application/json")]
  [Consumes("application/json")]
  [ProducesResponseType(typeof(MyResponse), StatusCodes.Status201Created)]
  [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Create([FromBody] CreateRequest request, ...) { ... }
  ```

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
- Map domain exceptions using a **two-level hierarchy** (subclass must be checked first):

  | Exception type | HTTP status | Use for |
  |---|---|---|
  | `DomainValidationException` (subclass) | 422 Unprocessable Entity | Input violated a domain validation constraint (field length, format, due-date in the past) |
  | `DomainException` (base) | 409 Conflict | Business/lifecycle rule violated (invalid state transition, item not mutable) |

  ```csharp
  // ✅ DomainValidationException — field/format constraint → 422
  throw new DomainValidationException("TITLE_TOO_LONG",
      $"Title must not exceed {MaxTitleLength} characters.");

  // ✅ DomainException — business/lifecycle rule → 409
  throw new DomainException("INVALID_STATUS_TRANSITION",
      $"Cannot transition from '{from}' to '{to}'.");
  ```

  Exception handler (evaluate subclass **before** base class):
  ```csharp
  if (exception is DomainValidationException validationEx)
  {
      problem = new ProblemDetails
      {
          Title = "A domain validation rule was violated.",
          Status = StatusCodes.Status422UnprocessableEntity,
          Detail = validationEx.Message,
      };
      problem.Extensions["errorCode"] = validationEx.ErrorCode;
  }
  else if (exception is DomainException domainEx)
  {
      problem = new ProblemDetails
      {
          Title = "A domain rule was violated.",
          Status = StatusCodes.Status409Conflict,
          Detail = domainEx.Message,
      };
      problem.Extensions["errorCode"] = domainEx.ErrorCode;
  }
  ```

  > ⚠️ **Never** use `ArgumentException` for domain validation — it carries no `ErrorCode` and the global handler maps it to 500. Always use `DomainValidationException` instead.

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
- **Scope claim format:** Entra ID may emit scopes as either individual claims or as a **single space-delimited string** (e.g., `"scp": "api.read api.write"`). `RequireClaim("scp", "api.read")` performs an exact-value match and **silently fails** when scopes are space-delimited. Use `RequireAssertion` to handle both formats:

  ```csharp
  // ✅ Handles both individual claims and space-delimited single claims
  options.AddPolicy("Api.Read", policy =>
      policy.RequireAssertion(ctx =>
          ctx.User.FindAll("scp").Any(c =>
              c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Contains("api.read", StringComparer.OrdinalIgnoreCase))));

  // ❌ Wrong — silently fails when Entra emits space-delimited scopes
  // options.AddPolicy("Api.Read", policy => policy.RequireClaim("scp", "api.read"));
  ```

### 8.2 Authorization
**Must**
- Policy-based authorization with scope/role checks.
- Default deny; endpoints explicitly require a policy.
- Register policies in **all environments** (including Development) so `[Authorize(Policy = "...")]` attributes are always evaluated.

### 8.2.1 Development authentication
**Must**
- In Development, use a synthetic `AuthenticationHandler` that:
  - Auto-succeeds authentication (no token required).
  - Issues claims matching all defined authorization policies (e.g., individual `scp` claims for `api.read`, `api.write`).
- **Never** use `[AllowAnonymous]` on production-secured endpoints as a dev convenience.
- This ensures the authorization pipeline is exercised identically in dev and prod.

### 8.3 Data protection and secrets
**Must**
- TLS 1.2+ (prefer 1.3).
- Use Key Vault for secrets; rotate regularly.
- Never log access tokens, secrets, or raw PII.

### 8.4 PII and compliance
**Must**
- Maintain a PII field inventory and logging redaction rules.
- Audit log security-relevant events (permission changes, admin actions).

### 8.5 Rate limiting
**Must**
- Apply rate limiting to protect write endpoints from abuse (flooding, credential-stuffed tokens).
- Use `Microsoft.AspNetCore.RateLimiting` (built-in since .NET 7 — no additional NuGet package needed).
- Return **HTTP 429 Too Many Requests** when the limit is exceeded.
- Define at minimum a **fixed-window** policy for write operations; tune limits per service capacity.

  ```csharp
  // Program.cs — service registration
  builder.Services.AddRateLimiter(options =>
  {
      options.AddFixedWindowLimiter("ApiWrite", o =>
      {
          o.PermitLimit = 60;
          o.Window = TimeSpan.FromMinutes(1);
          o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
          o.QueueLimit = 0; // no queuing — reject immediately
      });
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
  });

  // Program.cs — pipeline (before UseAuthentication)
  app.UseRateLimiter();
  ```

  Apply to write controllers or actions:
  ```csharp
  [HttpPost]
  [EnableRateLimiting("ApiWrite")]
  public async Task<IActionResult> Create(...) { ... }
  ```

### 8.6 Security headers
**Must**
- Return the following HTTP response headers on all API responses:
  - `X-Content-Type-Options: nosniff` — prevents MIME-type sniffing.
  - `X-Frame-Options: DENY` — prevents clickjacking via iframes.
  - `Cache-Control: no-store` — prevents sensitive API responses from being cached.
- Add as early-pipeline middleware so headers are present on all responses including errors.

  ```csharp
  // Program.cs — early in pipeline, before UseSerilogRequestLogging
  app.Use(async (context, next) =>
  {
      context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
      context.Response.Headers.Append("X-Frame-Options", "DENY");
      context.Response.Headers.Append("Cache-Control", "no-store");
      await next(context);
  });
  ```

  > **Note:** HSTS is handled at the Azure Container Apps ingress (TLS terminated at platform edge — TRD §3.1). Do not add it to middleware when running behind an ACA-managed reverse proxy.

### 8.7 CORS
**Must**
- Configure an explicit CORS policy — **never** use `AllowAnyOrigin()` in production.
- Default to **deny-all** (empty origins list) if no browser clients are known.
- When browser clients are required, serve an **allowlist from configuration** — not hard-coded strings.

  ```csharp
  // Program.cs — service registration
  builder.Services.AddCors(options =>
  {
      options.AddPolicy("AllowedOrigins", policy =>
          policy
              .WithOrigins(
                  builder.Configuration.GetSection("Cors:AllowedOrigins")
                         .Get<string[]>() ?? [])
              .AllowAnyHeader()
              .AllowAnyMethod());
  });

  // Program.cs — pipeline (before UseAuthentication)
  app.UseCors("AllowedOrigins");
  ```

  ```json
  // appsettings.json — safe default: no origins allowed
  {
    "Cors": {
      "AllowedOrigins": []
    }
  }
  ```

  > ⚠️ An empty `AllowedOrigins` array means **no browser origins are allowed** — this is the safe default for API-only services. Override per environment via environment variables: `Cors__AllowedOrigins__0=https://app.example.com`.

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

**Development/testing exception:**
- `EnsureCreated()` or `EnsureCreatedAsync()` is acceptable in **Development** and **test** environments for rapid iteration.
- Guard with an environment check: only call `EnsureCreated` when `IsDevelopment()` is true.
- The migration infrastructure (EF Core Design package, migrations folder) must still exist in source control even if not used at dev time.

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

