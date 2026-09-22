import * as cdk from "aws-cdk-lib";
import { Construct } from "constructs";
import { getApiRepo } from "./get-ecr-repos";
import Settings from "./settings";
import getVpc from "./get-vpc";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import createApi from "./create-api";

export class SidekickStack extends cdk.Stack {
  constructor(
    scope: Construct,
    settings: Settings,
    props?: cdk.StackProps
  ) {
    super(scope, settings.stackName, props);
    const apiRepo = getApiRepo(this, settings);
    const vpc = getVpc(this, settings);

    const configSecret = secretsmanager.Secret.fromSecretNameV2(
      this,
      "config-secret",
      settings.secretName
    );

    createApi({
      stack: this,
      vpc,
      settings,
      repo: apiRepo,
      neededSecrets: [configSecret],
    });
  }
}
