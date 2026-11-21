import * as cdk from "aws-cdk-lib";
import { Match, Template } from "aws-cdk-lib/assertions";
import * as ec2 from "aws-cdk-lib/aws-ec2";
import * as ecr from "aws-cdk-lib/aws-ecr";
import * as ecs from "aws-cdk-lib/aws-ecs";
import * as secretsmanager from "aws-cdk-lib/aws-secretsmanager";
import createApi from "../lib/create-api";
import createWorker from "../lib/create-knowledge-base-worker";
import staging from "../lib/profiles/staging";

function synthesize(target: "api" | "worker", injectRuntimeSecrets = false) {
  const app = new cdk.App({ context: { tag: "api-sha", workerTag: "worker-sha" } });
  const stack = new cdk.Stack(app, "TestStack", {
    env: { account: "123456789012", region: "us-east-1" },
  });
  const vpc = new ec2.Vpc(stack, "Vpc", { maxAzs: 2, natGateways: 1 });
  const repo = ecr.Repository.fromRepositoryName(stack, "Repo", "test-workload");
  const settings = {
    ...staging(app),
    domainName: undefined,
    dbSecurityGroupId: "",
    datadogSite: "datadoghq.eu",
    datadogAgentSecretName: "telemetry/approved-agent-key",
    apiRuntimeSecrets: injectRuntimeSecrets
      ? { secretName: "sidekick/api-runtime", apiKeyCount: 2 }
      : undefined,
  };
  const neededSecrets = [secretsmanager.Secret.fromSecretNameV2(
    stack, "LegacySecret", settings.secretName
  )];
  for (const env of [settings.apiEnvVars, settings.workerEnvVars]) {
    env.DD_API_KEY = "should-not-be-synthesized";
    env.DD_LOGS_DIRECT_SUBMISSION_ENABLED = "true";
  }
  const options = { stack, vpc, repo, settings, neededSecrets };
  if (target === "api") {
    createApi(options);
  } else {
    createWorker({ ...options, cluster: new ecs.Cluster(stack, "Cluster", { vpc }) });
  }
  const template = Template.fromStack(stack);
  const task = Object.values(template.findResources("AWS::ECS::TaskDefinition"))[0].Properties;
  const containers = Object.fromEntries(
    task.ContainerDefinitions.map((container: { Name: string }) => [container.Name, container])
  );
  return { template, task, containers };
}

test("API routes port 8080 to web, using ALB readiness and ECS liveness separately", () => {
  const { template, containers } = synthesize("api");
  template.hasResourceProperties("AWS::ElasticLoadBalancingV2::TargetGroup", {
    HealthCheckPath: "/health/ready",
    HealthCheckPort: "traffic-port",
    Matcher: { HttpCode: "200" },
  });
  // ECS registers each IP with the container port, overriding the target group's default port.
  template.hasResourceProperties("AWS::ECS::Service", {
    LoadBalancers: Match.arrayWith([Match.objectLike({ ContainerName: "web", ContainerPort: 8080 })]),
  });
  const web = containers.web;
  expect(web.HealthCheck.Command).toEqual(["CMD-SHELL", expect.stringContaining(":8080/health/live")]);
  expect(web.Environment).toContainEqual({ Name: "ASPNETCORE_URLS", Value: "http://+:8080" });
  expect(web.Environment).toContainEqual({ Name: "SECRET_NAME", Value: "sidekick/staging" });
  expect(web.Secrets).toBeUndefined();
});

test("API runtime auth and SQL configuration is optionally injected only into web", () => {
  const { task, template, containers } = synthesize("api", true);
  expect(Object.fromEntries(containers.web.Secrets.map(
    (entry: { Name: string; ValueFrom: unknown }) => [entry.Name, JSON.stringify(entry.ValueFrom)]
  ))).toEqual(Object.fromEntries([
    "ConnectionStrings__Default", "ApiKey__Keys__0", "ApiKey__Keys__1",
  ].map(name => [name, expect.stringContaining(`secret:sidekick/api-runtime:${name}::`)])));

  const executionRoleId = task.ExecutionRoleArn["Fn::GetAtt"][0];
  const taskRoleId = task.TaskRoleArn["Fn::GetAtt"][0];
  const policies = Object.values(template.findResources("AWS::IAM::Policy"));
  const policyFor = (roleId: string) => JSON.stringify(
    policies.filter(policy => policy.Properties.Roles.some(
      (role: { Ref: string }) => role.Ref === roleId
    ))
  );
  expect(policyFor(executionRoleId)).toContain("sidekick/api-runtime");
  expect(policyFor(executionRoleId)).toContain("telemetry/approved-agent-key");
  expect(policyFor(taskRoleId)).not.toContain("sidekick/api-runtime");
  expect(policyFor(taskRoleId)).not.toContain("telemetry/approved-agent-key");
  expect(policyFor(taskRoleId)).toContain("sidekick/staging");
});

test.each([
  ["api", "web", "sidekick-api", "api-sha"],
  ["worker", "knowledge-base-worker", "sidekick-knowledge-base-ingestion", "worker-sha"],
] as const)("%s ships only through FireLens, CloudWatch and the approved Forwarder", (target, name, service, version) => {
  const { template, containers } = synthesize(target);
  const app = containers[name];
  const router = containers["log-router"];
  const agent = containers["datadog-agent"];
  expect(app.Secrets).toBeUndefined();
  expect(app.Environment.map((item: { Name: string }) => item.Name).join(" ")).not.toMatch(/DD_API_KEY|DD_LOGS_DIRECT_SUBMISSION/);
  expect(app.Environment).toContainEqual({ Name: "DD_TRACE_AGENT_URL", Value: "http://127.0.0.1:8126" });
  expect(app.DockerLabels).toMatchObject({
    "com.datadoghq.tags.env": "staging",
    "com.datadoghq.tags.service": service,
    "com.datadoghq.tags.version": version,
  });
  expect(app.DependsOn).toEqual([{ ContainerName: router.Name, Condition: "START" }]);
  expect(app.LogConfiguration).toMatchObject({
    LogDriver: "awsfirelens",
    Options: { Name: "cloudwatch_logs", auto_create_group: "false" },
  });
  expect(router.Image).toMatch(/aws-for-fluent-bit:\d+\.\d+\.\d+@sha256:[a-f0-9]{64}$/);
  expect(router.Essential).toBe(true);
  expect(router.FirelensConfiguration).toEqual({
    Type: "fluentbit",
    Options: {
      "enable-ecs-log-metadata": "true",
      "config-file-type": "file",
      "config-file-value": "/fluent-bit/configs/sidekick-json.conf",
    },
  });
  const config = router.Environment.find((item: { Name: string }) => item.Name === "FIRELENS_EXTRA_CONFIG").Value;
  expect(config).toMatch(/Rename source stream[\s\S]+Key_Name log\s+Parser json\s+Reserve_Data True/);
  expect(router.Secrets).toBeUndefined();
  expect(agent.Image).toMatch(/datadog\/agent:\d+\.\d+\.\d+@sha256:[a-f0-9]{64}$/);
  expect(agent.Essential).toBe(false);
  expect(agent.Environment).toEqual(expect.arrayContaining([
    { Name: "DD_SITE", Value: "datadoghq.eu" },
    { Name: "DD_LOGS_ENABLED", Value: "false" },
  ]));
  expect(agent.Secrets).toEqual([
    { Name: "DD_API_KEY", ValueFrom: expect.anything() },
  ]);
  expect(agent.HealthCheck).toBeDefined();
  expect(agent.LogConfiguration.LogDriver).toBe("awslogs");
  expect(router.LogConfiguration.LogDriver).toBe("awslogs");

  const logGroup = app.LogConfiguration.Options.log_group_name;
  template.hasResource("AWS::Logs::LogGroup", Match.objectLike({
    DeletionPolicy: "Retain",
    Properties: { RetentionInDays: 30 },
  }));
  template.resourceCountIs("AWS::Logs::SubscriptionFilter", 1);
  template.hasParameter("DatadogForwarderArn", { Type: "String", Default: Match.absent() });
  const permissionId = template.getResourceId("AWS::Lambda::Permission", {
    Properties: {
      Action: "lambda:InvokeFunction",
      FunctionName: { Ref: "DatadogForwarderArn" },
      SourceAccount: "123456789012",
      SourceArn: { "Fn::GetAtt": [logGroup.Ref, "Arn"] },
    },
  });
  template.hasResource("AWS::Logs::SubscriptionFilter", {
    DependsOn: [permissionId],
    Properties: {
      DestinationArn: { Ref: "DatadogForwarderArn" },
      FilterPattern: "",
      LogGroupName: logGroup,
    },
  });
  if (target === "worker") {
    expect(app.HealthCheck).toBeUndefined();
    template.hasResourceProperties("AWS::Scheduler::Schedule", {
      ScheduleExpression: "cron(0 3 * * ? *)",
      ScheduleExpressionTimezone: "America/Chicago",
    });
  }
});
