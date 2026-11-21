import * as cdk from "aws-cdk-lib";
import * as ecs from "aws-cdk-lib/aws-ecs";
import * as lambda from "aws-cdk-lib/aws-lambda";
import * as logs from "aws-cdk-lib/aws-logs";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import { Construct } from "constructs";

const FIRELENS_CPU = 64;
const AGENT_CPU = 128;
const FIRELENS_CONFIG_PATH = "/fluent-bit/configs/sidekick-json.conf";
const FIRELENS_CONFIG = `
[SERVICE]
    Parsers_File /fluent-bit/parsers/parsers.conf
    Flush 1
    Grace 30

[FILTER]
    Name modify
    Match *
    Rename source stream

[FILTER]
    Name parser
    Match *
    Key_Name log
    Parser json
    Reserve_Data True
`;

interface Options {
  task: ecs.FargateTaskDefinition;
  environment: Record<string, string>;
  env: string;
  service: string;
  version: string;
  datadogSite?: string;
  agentSecretName?: string;
  taskCpu: number;
}

export function addFargateTelemetry(scope: Construct, options: Options) {
  const { task, env, service, version } = options;
  const stack = cdk.Stack.of(scope);
  const applicationLogs = new logs.LogGroup(scope, "ApplicationLogs", {
    retention: logs.RetentionDays.ONE_MONTH,
    removalPolicy: cdk.RemovalPolicy.RETAIN,
  });
  applicationLogs.grantWrite(task.taskRole);

  const forwarder = new cdk.CfnParameter(scope, "DatadogForwarderArn", {
    type: "String",
    description: "ARN of the approved existing Datadog Forwarder in this account and region",
    allowedPattern: "arn:[a-z0-9-]+:lambda:[a-z0-9-]+:[0-9]{12}:function:[a-zA-Z0-9_-]+",
  });
  const invokePermission = new lambda.CfnPermission(scope, "AllowCloudWatchToInvokeForwarder", {
    action: "lambda:InvokeFunction",
    functionName: forwarder.valueAsString,
    principal: `logs.${cdk.Aws.URL_SUFFIX}`,
    sourceAccount: stack.account,
    sourceArn: applicationLogs.logGroupArn,
  });
  const subscription = new logs.CfnSubscriptionFilter(scope, "DatadogLogSubscription", {
    logGroupName: applicationLogs.logGroupName,
    destinationArn: forwarder.valueAsString,
    filterPattern: "",
  });
  subscription.addResourceDependency(invokePermission);

  const diagnostics = new logs.LogGroup(scope, "TelemetryDiagnostics", {
    retention: logs.RetentionDays.ONE_MONTH,
  });
  const router = task.addFirelensLogRouter("log-router", {
    image: ecs.ContainerImage.fromRegistry(
      "public.ecr.aws/aws-observability/aws-for-fluent-bit:3.5.10@sha256:b1491753f6bf4c2d0c302abd33911ed2869f2275151d05e0549264938211f2c4"
    ),
    essential: true,
    cpu: FIRELENS_CPU,
    memoryReservationMiB: 128,
    stopTimeout: cdk.Duration.seconds(60),
    // Preserve the stdout/stderr indicator without colliding with the app's Datadog `source` field.
    entryPoint: ["/bin/sh", "-ec"],
    command: [`printf '%s\\n' "$FIRELENS_EXTRA_CONFIG" > '${FIRELENS_CONFIG_PATH}'; exec /entrypoint.sh`],
    environment: { FIRELENS_EXTRA_CONFIG: FIRELENS_CONFIG },
    logging: ecs.LogDrivers.awsLogs({ logGroup: diagnostics, streamPrefix: "firelens" }),
    firelensConfig: {
      type: ecs.FirelensLogRouterType.FLUENTBIT,
      options: {
        enableECSLogMetadata: true,
        configFileType: ecs.FirelensConfigFileType.FILE,
        configFileValue: FIRELENS_CONFIG_PATH,
      },
    },
  });

  const agentSecret = secretsmanager.Secret.fromSecretNameV2(
    scope, "DatadogAgentSecret", options.agentSecretName ?? "SidekickAPI_DD_API_KEY"
  );
  task.addContainer("dd-agent", {
    containerName: "datadog-agent",
    image: ecs.ContainerImage.fromRegistry(
      "public.ecr.aws/datadog/agent:7.85.5@sha256:109bb519784f413a3520ebb582010a273fb30cd4169a39ea29a949c3e566452a"
    ),
    essential: false,
    cpu: AGENT_CPU,
    memoryReservationMiB: 512,
    environment: {
      ECS_FARGATE: "true",
      DD_SITE: options.datadogSite ?? "datadoghq.com",
      DD_APM_ENABLED: "true",
      DD_LOGS_ENABLED: "false",
      DD_ENV: env,
    },
    secrets: { DD_API_KEY: ecs.Secret.fromSecretsManager(agentSecret) },
    healthCheck: {
      command: ["CMD-SHELL", "agent health"],
      interval: cdk.Duration.seconds(30),
      timeout: cdk.Duration.seconds(5),
      retries: 3,
      startPeriod: cdk.Duration.seconds(60),
    },
    logging: ecs.LogDrivers.awsLogs({ logGroup: diagnostics, streamPrefix: "agent" }),
  });

  const environment: Record<string, string> = {
    ...Object.fromEntries(
      Object.entries(options.environment).filter(([name]) =>
        !/^DD_(API_KEY$|LOGS_DIRECT_SUBMISSION(?:_|$))/i.test(name)
      )
    ),
    DD_APM_ENABLED: "true",
    DD_ENV: env,
    DD_SERVICE: service,
    DD_VERSION: version,
    DD_TRACE_AGENT_URL: "http://127.0.0.1:8126",
    DD_LOGS_INJECTION: "true",
    DD_RUNTIME_METRICS_ENABLED: "true",
  };
  return {
    router,
    containerOptions: {
      cpu: options.taskCpu - FIRELENS_CPU - AGENT_CPU,
      environment,
      dockerLabels: {
        "com.datadoghq.tags.env": env,
        "com.datadoghq.tags.service": service,
        "com.datadoghq.tags.version": version,
      },
      logging: ecs.LogDrivers.firelens({
        options: {
          Name: "cloudwatch_logs",
          region: stack.region,
          log_group_name: applicationLogs.logGroupName,
          log_stream_prefix: "application/",
          auto_create_group: "false",
          auto_retry_requests: "true",
          retry_limit: "5",
        },
      }),
    } satisfies Omit<ecs.ContainerDefinitionOptions, "image">,
  };
}
