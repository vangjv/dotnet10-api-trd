import { Construct } from "constructs";
import * as acm from "aws-cdk-lib/aws-certificatemanager";
import * as cloudwatch from "aws-cdk-lib/aws-cloudwatch";
import { IHostedZone } from "aws-cdk-lib/aws-route53";
import Settings from "./settings";

export interface Request {
  stack: Construct;
  settings: Settings;
  domainName: string;
  hostedZone: IHostedZone;
}

export interface Response {
  certificate: acm.Certificate;
}

export default (
  {
    stack,
    settings,
    domainName,
    hostedZone
  }: Request): Response => {
  const certName = `${settings.stackName}-cert`;

  const validation = acm.CertificateValidation.fromDnsMultiZone({
    [domainName]: hostedZone,
  });
  const certificate = new acm.Certificate(stack, certName, {
    domainName,
    validation,
  });

  // Create Cloudwatch alert for cert expiration
  const _certMetric = certificate
    .metricDaysToExpiry()
    .createAlarm(stack, `${certName}-expiry-alarm`, {
      comparisonOperator: cloudwatch.ComparisonOperator.LESS_THAN_THRESHOLD,
      evaluationPeriods: 1,
      threshold: 45,
    });

  return {
    certificate,
  };
};
