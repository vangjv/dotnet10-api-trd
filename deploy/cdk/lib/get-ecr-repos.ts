import { Construct } from "constructs";
import Settings from "../lib/settings";

import * as ecr from "aws-cdk-lib/aws-ecr";

export default function (scope: Construct, settings: Settings) {
  return {
    api: getApiRepo(scope, settings),
    worker: getWorkerRepo(scope, settings),
  };
}

export function getApiRepo(scope: Construct, settings: Settings) {
  return getRepo(scope, "ecr-repo-api", settings.ecrRepoName);
}

export function getWorkerRepo(scope: Construct, settings: Settings) {
  return getRepo(scope, "ecr-repo-worker", settings.workerEcrRepoName);
}

function getRepo(
  scope: Construct,
  resourceName: string,
  repositoryName: string
) {
  return ecr.Repository.fromRepositoryName(scope, resourceName, repositoryName);
}
