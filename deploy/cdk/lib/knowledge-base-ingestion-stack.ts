import * as cdk from "aws-cdk-lib";
import * as ecs from "aws-cdk-lib/aws-ecs";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import { Construct } from "constructs";
import createKnowledgeBaseWorker from "./create-knowledge-base-worker";
import { getWorkerRepo } from "./get-ecr-repos";
import getVpc from "./get-vpc";
import Settings from "./settings";

export class KnowledgeBaseIngestionStack extends cdk.Stack {
  constructor(scope: Construct, settings: Settings, props?: cdk.StackProps) {
    super(scope, `${settings.stackName}-knowledge-base-ingestion`, props);

    const workerRepo = getWorkerRepo(this, settings);
    const vpc = getVpc(this, settings);
    const workerConfigSecret = secretsmanager.Secret.fromSecretNameV2(
      this,
      "knowledge-base-config-secret",
      settings.workerSecretName
    );
    const cluster = new ecs.Cluster(this, "knowledgeBaseWorkerCluster", {
      vpc,
      clusterName: `${settings.stackName}-knowledge-base-ingestion`,
    });

    createKnowledgeBaseWorker({
      stack: this,
      vpc,
      cluster,
      settings,
      repo: workerRepo,
      neededSecrets: [workerConfigSecret],
    });
  }
}
