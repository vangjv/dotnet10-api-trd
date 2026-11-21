# Sidekick CDK

From `deploy/cdk`, run `npm ci`, `npm run build`, and `npm test -- --runInBand` to validate without deploying or looking up AWS resources. The tests synthesize the API and worker task definitions using fixture VPCs.

## API health and runtime configuration

- The load balancer sends traffic to `web` on HTTP port `8080`. The API binds to `http://+:8080`. The target group requires HTTP 200 from `/health/ready` on that same port; this endpoint should report critical dependency failures.
- The ECS container runs `curl` against `http://127.0.0.1:8080/health/live`. The runtime image must contain `curl` and a shell. Both probes must work anonymously without HTTPS redirects. Keep liveness process-only so database failures do not fail the container probe. The ECS service still uses ALB target health for replacement decisions after its grace period. The scheduled worker has no HTTP probe.
- For the reference API, pass `-c apiRuntimeSecretName=<existing-secret-name>` to synth/deploy the API with ECS injection. Store a JSON object in that **separate** Secrets Manager secret with flat string fields `ApiKey__Keys__0` and `ConnectionStrings__Default`. For overlapping keys during rotation, populate consecutive fields starting at zero (e.g. `ApiKey__Keys__1`) and pass `-c apiKeyCount=2` (default is one). ECS resolves the selected fields at task startup using the execution role; replacing tasks is required to pick up changed values. If the secret uses a customer-managed KMS key, grant the execution role `kms:Decrypt` against that key before rollout. Do not supply secret values through CDK context.
- Omitting `apiRuntimeSecretName` preserves the existing Sidekick startup configuration (`SECRET_NAME` and task-role read access to the profile secret), also retained for any other settings a Sidekick image reads. Enable the new injection for an image that expects the reference API settings, and confirm the existing Sidekick secret and application settings still work before migrating. Fargate Linux platform 1.4.0 or newer is required for selecting JSON fields.

## Datadog and FireLens rollout

1. For **each API or ingestion stack** pass `--parameters DatadogForwarderArn=<arn>` on the first deployment of this change. Use the **unqualified ARN** of the approved existing Datadog Forwarder Lambda in the **same account and region**; select it per environment in deployment automation. The `ingestion-repository` target does not need the parameter. The CDK stack owns the subscription and a scoped Lambda invoke permission, not the Forwarder, its API key, or its Datadog configuration. The CloudFormation deployment role needs permissions for the subscription and to add/remove the Lambda invoke permission.
2. Ensure the Agent secret exists (by default `SidekickAPI_DD_API_KEY`, or override with `-c datadogAgentSecretName=<name>`). Store the API key as the whole secret string; only the Agent container gets it through ECS secret injection. `-c datadogSite=<site>` controls the Agent site (default `datadoghq.com`); confirm the separately managed Forwarder uses the approved site and secret. Rotate them independently. The Agent is non-essential, runs its own health check, and accepts APM on task-local `127.0.0.1:8126` without a public port or application startup dependency.
3. API and worker must emit one JSON event per console line. The digest-pinned AWS for Fluent Bit image starts with a CDK-supplied file filter that renames the container's stdout/stderr `source` field to `stream`, then merges valid JSON from `log` into the record while preserving ECS metadata and the application's `source: csharp`. Non-JSON lines remain unparsed. The `cloudwatch_logs` output writes the entire record to a dedicated 30-day application log group; this is the only group subscribed by the stack. Router and Agent diagnostics go to a separate, unsubscribed CloudWatch group. There is no Datadog key or direct submission configuration in either app container. Do not attach another export path (a log group subscription, account-level subscription, direct app sink, or Agent log collection) for these events.
4. Before promoting, verify in a non-production account that a redacted JSON event from each workload appears in CloudWatch with top-level fields and arrives once in Datadog with trace correlation. Confirm the worker image emits JSON, the API probes work with the deployed image, and readiness fails independently of liveness when SQL is unavailable. Monitor router delivery errors/retries, Forwarder errors and lag, and Agent health. Review the sidecar digests and memory/CPU budgets when updating either image.

## Scheduled Worker

The knowledge base ingestion worker deploys through separate CDK targets. The `ingestion-repository` target owns the worker ECR repository, and the `ingestion` target owns the EventBridge Scheduler-triggered ECS Fargate task. The task runs daily at 3:00 AM in the `America/Chicago` timezone and uses the worker image tag supplied through the `workerTag` context value.

## Knowledge Base Ingestion Runbook

### First-time repository setup

Use the `FMG.Sidekick Knowledge Base Repository Setup` GitHub Actions workflow before the first ingestion deployment for a target environment/account.

Checklist:

* Select the target environment: `uat`, `staging`, or `prod`.
* Run the setup workflow.
* If the ECR repository does not exist, the workflow deploys the `ingestion-repository` CDK target and creates it.
* If the repository already exists and is already managed by the `fmg-sidekick-knowledge-base-repository` CloudFormation stack, the workflow converges the repository stack normally.
* If the repository already exists outside CloudFormation, stop and import it with the checklist below.

### Existing repository import

CloudFormation cannot automatically adopt an existing named ECR repository during a normal deploy. If `fmg-sidekick-knowledge-base` already exists outside the repository stack, import it once before using the setup workflow.

Checklist:

* Configure AWS credentials for the target account and region.
* Use a CDK CLI version compatible with the `aws-cdk-lib` version in `package.json`. The setup workflow installs the CLI globally before deploy; for local import, update the CLI if needed:

```bash
npm install -g aws-cdk
```

* Install CDK dependencies if needed:

```bash
cd deploy/cdk
npm ci
```

* Start the CDK import for the environment:

```bash
cdk import -c target=ingestion-repository -c profile=<environment>
```

* When prompted, map the `knowledgeBaseWorkerRepository` ECR repository resource to the physical repository name `fmg-sidekick-knowledge-base`.
* After the import completes, run the `FMG.Sidekick Knowledge Base Repository Setup` workflow for the same environment to verify and converge repository settings.

### Regular ingestion deployment

After repository setup succeeds for an environment, use the `FMG.Sidekick Knowledge Base CI` workflow for normal worker image and ingestion infrastructure deployments. This workflow assumes the ECR repository already exists and only deploys the `ingestion` CDK target.
