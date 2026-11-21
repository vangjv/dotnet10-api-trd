import { Construct } from "constructs";
import * as cdk from "aws-cdk-lib";
import * as ec2 from "aws-cdk-lib/aws-ec2";
import * as ecr from "aws-cdk-lib/aws-ecr";
import * as ecs from "aws-cdk-lib/aws-ecs";
import * as scheduler from "aws-cdk-lib/aws-scheduler";
import * as schedulerTargets from "aws-cdk-lib/aws-scheduler-targets";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import Settings from "./settings";
import grantSgAccess from "./grant-sg-access";
import { addFargateTelemetry } from "./fargate-telemetry";

interface Request {
  stack: Construct;
  vpc: cdk.aws_ec2.IVpc;
  cluster: ecs.ICluster;
  settings: Settings;
  repo: ecr.IRepository;
  neededSecrets: secretsmanager.ISecret[];
}

export default function ({
  stack,
  vpc,
  cluster,
  settings,
  repo,
  neededSecrets,
}: Request): scheduler.Schedule {
  const image = ecs.ContainerImage.fromEcrRepository(
    repo,
    settings.workerImageTag
  );
  const taskDefinition = new ecs.FargateTaskDefinition(
    stack,
    "knowledgeBaseWorkerTaskDef",
    {
      memoryLimitMiB: settings.containerMemoryLimitMiB,
      cpu: settings.containerCpu,
    }
  );

  const { router, containerOptions } = addFargateTelemetry(stack, {
    task: taskDefinition,
    environment: settings.workerEnvVars,
    env: settings.workerEnvVars.DD_ENV ?? settings.apiEnvVars.DD_ENV ?? "dev",
    service: "sidekick-knowledge-base-ingestion",
    version: settings.workerImageTag,
    datadogSite: settings.datadogSite,
    agentSecretName: settings.datadogAgentSecretName,
    taskCpu: settings.containerCpu,
  });
  const worker = taskDefinition.addContainer("worker", {
    ...containerOptions,
    image,
    containerName: "knowledge-base-worker",
    memoryReservationMiB: settings.memoryReservationMiB,
  });
  worker.addContainerDependencies({ container: router, condition: ecs.ContainerDependencyCondition.START });

  if (neededSecrets) {
    neededSecrets.forEach((secret) => secret.grantRead(taskDefinition.taskRole));
  }

  const securityGroup = new ec2.SecurityGroup(stack, "knowledgeBaseWorkerSg", {
    vpc,
    allowAllOutbound: true,
    description: "Security group for the knowledge base ingestion worker",
  });

  grantSgAccess({
    service: {
      name: "knowledge-base-worker",
      connectionSecurityGroups: [securityGroup],
    },
    stack,
    settings,
  });

  return new scheduler.Schedule(stack, "knowledgeBaseWorkerSchedule", {
    description:
      "Runs the knowledge base ingestion worker daily at 3 AM America/Chicago",
    timeWindow: scheduler.TimeWindow.off(),
    schedule: scheduler.ScheduleExpression.cron({
      minute: "0",
      hour: "3",
      timeZone: cdk.TimeZone.AMERICA_CHICAGO,
    }),
    target: new schedulerTargets.EcsRunFargateTask(cluster, {
      taskDefinition,
      securityGroups: [securityGroup],
      vpcSubnets: { subnetType: ec2.SubnetType.PRIVATE_WITH_EGRESS },
      enableEcsManagedTags: true,
      propagateTags: true,
    }),
  });
}