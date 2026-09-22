import Settings from '../settings';
import * as cdk from 'aws-cdk-lib';

export default function (app: cdk.App): Settings {
  const appName = "fmg-sidekick";

  const environmentName = "prod";
  const stackName = `${appName}-${environmentName}`;

  const imageTag = app.node.tryGetContext("tag") ?? "";
  const workerImageTag = app.node.tryGetContext("workerTag") ?? imageTag;

  const secretName = "sidekick/prod";
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
    vpcName: "Production",
    imageTag: imageTag,
    workerImageTag: workerImageTag,
    stackName,
    domainName: "fmgsuite.com",
    subdomain: "sidekick-api",
    dbSecurityGroupId: "sg-08c3d1c982d0be067",
    secretName,
    workerSecretName,
    apiEnvVars: {
      "SECRET_NAME": secretName,
      "ASPNETCORE_ENVIRONMENT": "Production",
      "ArOptions__BaseUrl": "https://api.agencyrevolution.com/api/",
      "DD_ENV": "prod"
    },
    workerEnvVars: {
      "SECRET_NAME": secretName,
      "DOTNET_ENVIRONMENT": "Production",
      "IngestionOptions__MaxRuntimeMinutes": "60",
      "DD_ENV": "prod"
    }
  };
};
