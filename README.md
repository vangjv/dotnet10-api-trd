# Reference .NET 10 Enterprise API Skeleton

This folder provides a **reference project structure** aligned to the TRD:
- ASP.NET Core API + OpenAPI
- API Key auth (interim standard) via a custom `AuthenticationHandler`
- EF Core persistence (SQL Server/Amazon RDS for SQL Server)
- Observability via Datadog (Datadog Agent sidecar + dd-trace-dotnet auto-instrumentation)
- Serilog structured logging (console sink, collected by the Datadog Agent)
- Polly resilience policies
- Health checks for AWS Fargate (Amazon ECS)

> This is a *starter skeleton* (not a full implementation). Use as a template.

## Quick start (local)
1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`

## Next steps
- Wire up API key validation in `appsettings.json` and AWS Secrets Manager for deployed envs.
- Configure connection string via AWS Secrets Manager / IAM task role.
- Add first feature vertical slice under `Api/Features/...`.

