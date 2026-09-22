# Welcome to your CDK TypeScript project

This is a blank project for CDK development with TypeScript.

The `cdk.json` file tells the CDK Toolkit how to execute your app.

## Useful commands

* `npm run build`   compile typescript to js
* `npm run watch`   watch for changes and compile
* `npm run test`    perform the jest unit tests
* `npx cdk deploy`  deploy this stack to your default AWS account/region
* `npx cdk diff`    compare deployed stack with current state
* `npx cdk synth`   emits the synthesized CloudFormation template

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
