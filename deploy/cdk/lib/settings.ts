export interface ApiEnvVars {
  [key: string]: string;
}

export interface ScalableConfig {
  minCapacity: number;
  maxCapacity: number;
  targetCpuPercent: number;
  targetMemPercent: number;
}

export default interface Settings {
  imageTag: string;
  workerImageTag: string;
  stackName: string;
  ecrRepoName: string;
  workerEcrRepoName: string;
  vpcName: string;
  apiEnvVars: ApiEnvVars;
  workerEnvVars: ApiEnvVars;
  datadogSite?: string;
  datadogAgentSecretName?: string;
  apiRuntimeSecrets?: {
    secretName: string;
    apiKeyCount: number;
  };
  domainName?: string;
  subdomain?: string;
  dbSecurityGroupId: string;
  secretName: string;
  workerSecretName: string;
  containerMemoryLimitMiB: number;
  containerCpu: number;
  serviceCpu: number;
  memoryReservationMiB: number;
  serviceMemoryLimitMiB: number;
  scalableConfig: ScalableConfig;
}
