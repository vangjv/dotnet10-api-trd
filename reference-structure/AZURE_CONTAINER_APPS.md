# Azure Container Apps notes (operational defaults)

- Configure ingress port: 8080
- Readiness: GET /health/ready
- Liveness: GET /health/live
- Use Managed Identity for:
  - Azure Key Vault secret retrieval
  - Azure App Configuration (optional)
  - Azure SQL with Entra ID (recommended) or connection string secret
- Enable Dapr only if you need sidecar features (service invocation, pub/sub).

## Recommended env vars
- ASPNETCORE_ENVIRONMENT=Production
- OTEL_SERVICE_NAME=company-product-service
- APPLICATIONINSIGHTS_CONNECTION_STRING=<set via secret>
