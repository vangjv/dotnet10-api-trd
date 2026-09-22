import * as cdk from "aws-cdk-lib";
import * as ecr from "aws-cdk-lib/aws-ecr";
import { Construct } from "constructs";
import Settings from "./settings";

export class KnowledgeBaseIngestionRepositoryStack extends cdk.Stack {
  constructor(scope: Construct, settings: Settings, props?: cdk.StackProps) {
    super(scope, `${settings.workerEcrRepoName}-repository`, props);

    const repository = new ecr.Repository(this, "knowledgeBaseWorkerRepository", {
      repositoryName: settings.workerEcrRepoName,
      imageScanOnPush: true,
      encryption: ecr.RepositoryEncryption.AES_256,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    repository.addLifecycleRule({
      tagStatus: ecr.TagStatus.UNTAGGED,
      maxImageAge: cdk.Duration.days(30),
    });
  }
}
