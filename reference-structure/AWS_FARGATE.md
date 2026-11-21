# AWS Fargate deployment notes

- Run the Linux x64 container on private Fargate subnets. The ALB terminates HTTPS and redirects external HTTP; Kestrel listens on HTTP port 8080 behind that boundary. Use `/health/ready` for ALB routing and `/health/live` for ECS task replacement. Neither probe requires a key.
- Provide `ConnectionStrings__Default` and one or more `ApiKey__Keys__N` entries via ECS Secrets Manager injection. Use validated SQL Server TLS in deployed environments. Add new and old keys together, replace tasks, migrate clients, then retire the old keys and replace tasks again. The task execution role resolves injected secrets; grant runtime permissions to the task role only for actual AWS SDK calls.
- The API writes redacted NDJSON to console; FireLens sends it to CloudWatch and the Datadog Forwarder relays it. The task-local Datadog Agent receives traces and metrics, not application logs. Do not inject `DD_API_KEY` into the API or log router.
- Apply SQL migrations through a separate approved deployment step. Keep the schema compatible with the prior image through the rollback window. Do not run migrations at API startup.
- See [the CDK instructions](../deploy/cdk/README.md) for environment-specific image, secret, and telemetry settings. Validate forwarding and trace correlation in a non-production AWS environment before rollout.
