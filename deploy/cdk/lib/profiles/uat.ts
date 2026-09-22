import Settings from '../settings';
import * as cdk from 'aws-cdk-lib';

export default function (app: cdk.App): Settings {
  const appName = "fmg-sidekick";
  const environmentName = "uat";
  const stackName = `${appName}-${environmentName}`;

  const imageTag = app.node.tryGetContext("tag") ?? "";
  const workerImageTag = app.node.tryGetContext("workerTag") ?? imageTag;

  const secretName = "sidekick/uat";
  const workerSecretName = secretName;

  return {
    scalableConfig: {
      minCapacity: 1,
      maxCapacity: 5,
      targetCpuPercent: 80,
      targetMemPercent: 80
    },
    containerCpu: 1024,
    serviceCpu: 1024,
    containerMemoryLimitMiB: 2048,
    serviceMemoryLimitMiB: 2048,
    memoryReservationMiB: 1024,
    ecrRepoName: appName,
    workerEcrRepoName: `${appName}-knowledge-base`,
    vpcName: "UAT-VPC",
    imageTag: imageTag,
    workerImageTag: workerImageTag,
    stackName,
    domainName: "fmguat.com",
    subdomain: "sidekick-api",
    dbSecurityGroupId: "sg-07079f29a6ee976c0",
    secretName,
    workerSecretName,
    apiEnvVars: {
      "SECRET_NAME": secretName,
      "ASPNETCORE_ENVIRONMENT": "Uat",
      "ArOptions__BaseUrl": "https://staging-api.agencyrevolution.com/api/",
      "DD_ENV": "uat"
    },
    workerEnvVars: {
      "SECRET_NAME": secretName,
      "DOTNET_ENVIRONMENT": "Uat",
      "IngestionOptions__MaxRuntimeMinutes": "60",
      "DD_ENV": "uat"
    }
  };
};
