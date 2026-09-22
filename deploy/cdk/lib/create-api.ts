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

  settings.apiEnvVars["AWS__BucketName"] = bucket.bucketName;

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

  applicationTaskDef.addContainer("web", {
    image,
    environment: {
      ...settings.apiEnvVars,
      "DD_APM_ENABLED": "true",
      "DD_SERVICE": "sidekick-api",
      "DD_LOGS_INJECTION": "true",
      "DD_LOGS_DIRECT_SUBMISSION_INTEGRATIONS": 'Serilog',
      "DD_VERSION": settings.imageTag,
      "DD_RUNTIME_METRICS_ENABLED": "true",
      "DD_API_KEY": `{{resolve:secretsmanager:SidekickAPI_DD_API_KEY:SecretString}}`,
    },
    memoryReservationMiB: settings.memoryReservationMiB, // remaining memory for the buffer
    containerName: "web",
    portMappings: [
      {
        containerPort: 8080,
      },
    ],
    logging: new ecs.AwsLogDriver({
      streamPrefix: "application",
    }),
  });

  applicationTaskDef.addContainer("dd-agent", {
    containerName: 'datadog-agent',
    image: ecs.ContainerImage.fromRegistry('public.ecr.aws/datadog/agent:latest'),
    memoryReservationMiB: 512,
    environment: {
      'DD_API_KEY': `{{resolve:secretsmanager:SidekickAPI_DD_API_KEY:SecretString}}`,
      'ECS_FARGATE': 'true'
    },
  });

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
    path: "/swagger/index.html",
    port: "8080",
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
