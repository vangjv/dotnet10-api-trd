# AWS Fargate deployment notes

Operational defaults for this service on AWS Fargate (Amazon ECS), per the infrastructure patterns in `deploy/cdk`:

- Ingress port: 8080 (behind an Application Load Balancer)
- Readiness probe (ALB/ECS health check): `GET /health/ready`
- Liveness probe: `GET /health/live`
- IAM Task Role for:
  - AWS Secrets Manager (API keys, DB connection secrets)
  - Amazon S3 (if the service needs bucket access)
- Datadog Agent sidecar container for APM traces, runtime metrics, and log correlation (`DD_APM_ENABLED`, `DD_ENV`, `DD_SERVICE`, `DD_VERSION`, `DD_LOGS_INJECTION`, `DD_RUNTIME_METRICS_ENABLED`)

Recommended environment variables:
- `ASPNETCORE_ENVIRONMENT=Production`
- `DD_API_KEY=<set via AWS Secrets Manager>`
- `DD_SERVICE=company-product-service`
- `DD_ENV=<environment name, e.g. prod/uat/staging/sandbox>`
