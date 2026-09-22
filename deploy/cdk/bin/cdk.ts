#!/usr/bin/env node
import "source-map-support/register";
import * as cdk from "aws-cdk-lib";
import { SidekickStack } from "../lib/sidekick-stack";
import { KnowledgeBaseIngestionStack } from "../lib/knowledge-base-ingestion-stack";
import { KnowledgeBaseIngestionRepositoryStack } from "../lib/knowledge-base-ingestion-repository-stack";

const app = new cdk.App();
const profile = app.node.tryGetContext("profile");
if (!profile) throw new Error("Missing profile context key");
const target = app.node.tryGetContext("target") ?? "api";

if (target === "api" && !app.node.tryGetContext("tag")) {
  throw new Error("Missing tag context key");
}

if (target === "ingestion" && !app.node.tryGetContext("workerTag")) {
  throw new Error("Missing workerTag context key");
}

console.log('profile: ' + profile);
console.log('target: ' + target);

var profileModuleUrl = `../lib/profiles/${profile}`;
import(profileModuleUrl).then((module) => {
  const settings = module.default(app);
  console.log(settings);
  const stackProps = {
    env: {
      account: process.env.CDK_DEFAULT_ACCOUNT,
      region: process.env.CDK_DEFAULT_REGION,
    },
  };

  switch (target) {
    case "api":
      new SidekickStack(app, settings, stackProps);
      break;
    case "ingestion-repository":
      new KnowledgeBaseIngestionRepositoryStack(app, settings, stackProps);
      break;
    case "ingestion":
      new KnowledgeBaseIngestionStack(app, settings, stackProps);
      break;
    default:
      throw new Error(`Unsupported CDK target '${target}'`);
  }
});
