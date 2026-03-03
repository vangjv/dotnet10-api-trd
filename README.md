# Reference .NET 10 Enterprise API Skeleton

This folder provides a **reference project structure** aligned to the TRD:
- ASP.NET Core API + OpenAPI
- Entra ID auth via Microsoft.Identity.Web
- EF Core persistence (SQL Server/Azure SQL)
- Observability via OpenTelemetry + Azure Monitor exporter
- Serilog structured logging
- Polly resilience policies
- Health checks for Azure Container Apps

> This is a *starter skeleton* (not a full implementation). Use as a template.

## Quick start (local)
1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`

## Next steps
- Wire up Entra ID settings in `appsettings.json` and secret store for deployed envs.
- Configure connection string via Key Vault / managed identity.
- Add first feature vertical slice under `Api/Features/...`.

