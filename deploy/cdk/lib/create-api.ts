import { Construct } from "constructs";
import * as cdk from "aws-cdk-lib";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import * as ecr from "aws-cdk-lib/aws-ecr";
import Settings from "./settings";
import createCert from "./create-cert";
import * as ecs from "aws-cdk-lib/aws-ecs";
import * as ecsp from "aws-cdk-lib/aws-ecs-patterns";
import grantSgAccess from "./grant-sg-access";
import { HostedZone } from "aws-cdk-lib/aws-route53";
import { CfnLoadBalancer, SslPolicy } from "aws-cdk-lib/aws-elasticloadbalancingv2";
import * as s3 from 'aws-cdk-lib/aws-s3'
import { SubnetType } from "aws-cdk-lib/aws-ec2";
import { addFargateTelemetry } from "./fargate-telemetry";

const API_PORT = 8080;

interface Request {
  stack: Construct;
  vpc: cdk.aws_ec2.IVpc;
  settings: Settings;
  repo: ecr.IRepository;
  neededSecrets: secretsmanager.ISecret[];
}

export default function ({
  stack,
  vpc,
  settings,
  repo,
  neededSecrets,
}: Request): ecsp.ApplicationLoadBalancedFargateService {
  let domainZone: cdk.aws_route53.IHostedZone | undefined = undefined;
  let domainName: string | undefined = undefined;

  if (settings.domainName && settings.subdomain) {
    domainZone = HostedZone.fromLookup(stack, "zone", {
      domainName: settings.domainName,
    });
    domainName = `${settings.subdomain}.${settings.domainName}`;
  }

  let certificate: cdk.aws_certificatemanager.ICertificate | undefined =
    undefined;
  let sslPolicy: cdk.aws_elasticloadbalancingv2.SslPolicy | undefined =
    undefined;
  let redirectHTTP = false;

  if (domainZone && domainName) {
    certificate = createCert({
      stack,
      settings,
      domainName,
      hostedZone: domainZone,
    }).certificate;
    sslPolicy = SslPolicy.TLS12_EXT;
    redirectHTTP = true;
  }

  const bucket = new s3.Bucket(stack, 'bucket', {
    removalPolicy: cdk.RemovalPolicy.RETAIN,
    blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
    encryption: s3.BucketEncryption.S3_MANAGED,
    enforceSSL: true,
    versioned: true
  });

  const image = ecs.ContainerImage.fromEcrRepository(repo, settings.imageTag);

  const applicationTaskDef = new ecs.FargateTaskDefinition(
    stack,
    "applicationTaskDef",
    {
      memoryLimitMiB: settings.containerMemoryLimitMiB,
      cpu: settings.containerCpu,
    }
  );
  bucket.grantReadWrite(applicationTaskDef.taskRole);

  const { router, containerOptions } = addFargateTelemetry(stack, {
    task: applicationTaskDef,
    environment: {
      ...settings.apiEnvVars,
      AWS__BucketName: bucket.bucketName,
      ASPNETCORE_URLS: `http://+:${API_PORT}`,
    },
    env: settings.apiEnvVars.DD_ENV ?? "dev",
    service: "sidekick-api",
    version: settings.imageTag,
    datadogSite: settings.datadogSite,
    agentSecretName: settings.datadogAgentSecretName,
    taskCpu: settings.containerCpu,
  });

  const secrets: Record<string, ecs.Secret> = {};
  if (settings.apiRuntimeSecrets) {
    const { secretName, apiKeyCount } = settings.apiRuntimeSecrets;
    const runtimeSecret = secretsmanager.Secret.fromSecretNameV2(
      stack, "ApiRuntimeSecret", secretName
    );
    for (const name of [
      "ConnectionStrings__Default",
      ...Array.from({ length: apiKeyCount }, (_, index) => `ApiKey__Keys__${index}`),
    ]) {
      secrets[name] = ecs.Secret.fromSecretsManager(runtimeSecret, name);
      delete containerOptions.environment[name];
    }
  }

  const web = applicationTaskDef.addContainer("web", {
    ...containerOptions,
    image,
    secrets,
    memoryReservationMiB: settings.memoryReservationMiB, // remaining memory for the buffer
    containerName: "web",
    portMappings: [{ containerPort: API_PORT }],
    healthCheck: {
      command: ["CMD-SHELL", `curl -fsS --max-time 4 http://127.0.0.1:${API_PORT}/health/live > /dev/null`],
      interval: cdk.Duration.seconds(30),
      timeout: cdk.Duration.seconds(5),
      retries: 3,
      startPeriod: cdk.Duration.seconds(60),
    },
  });
  web.addContainerDependencies({ container: router, condition: ecs.ContainerDependencyCondition.START });
  applicationTaskDef.defaultContainer = web;

  const api = new ecsp.ApplicationLoadBalancedFargateService(
    stack,
    `application`,
    {
      taskDefinition: applicationTaskDef,
      vpc,
      cpu: settings.serviceCpu,
      memoryLimitMiB: settings.serviceMemoryLimitMiB,
      certificate,
      sslPolicy,
      domainName,
      domainZone,
      redirectHTTP,
      healthCheckGracePeriod: cdk.Duration.minutes(3), // Buffer time for service to by ready,
    }
  );

  const cfnLoadBalancer = api.loadBalancer.node.defaultChild as CfnLoadBalancer
  cfnLoadBalancer.subnets = vpc.selectSubnets({ onePerAz: true, subnetType: SubnetType.PUBLIC}).subnetIds

  api.loadBalancer.setAttribute('idle_timeout.timeout_seconds', '180');

  api.targetGroup.configureHealthCheck({
    path: "/health/ready",
    port: "traffic-port",
    healthyHttpCodes: "200",
  }); 

  // metric based auto scale
  const scalableTarget = api.service.autoScaleTaskCount({
    minCapacity: settings.scalableConfig.minCapacity,
    maxCapacity: settings.scalableConfig.maxCapacity,
  });

  scalableTarget.scaleOnCpuUtilization("CpuScaling", {
    targetUtilizationPercent: settings.scalableConfig.targetCpuPercent,
  });

  scalableTarget.scaleOnMemoryUtilization("MemoryScaling", {
    targetUtilizationPercent: settings.scalableConfig.targetMemPercent,
  });

  if (neededSecrets) {
    neededSecrets.forEach((secret) =>
      secret.grantRead(api.taskDefinition.taskRole)
    );
  }

  let service = {
    name: "api",
    connectionSecurityGroups: api.service.connections.securityGroups,
  };
  grantSgAccess({
    service,
    stack,
    settings,
  });

  return api;
}
