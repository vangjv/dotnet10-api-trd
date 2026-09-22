import { Construct } from "constructs";

import * as ec2 from "aws-cdk-lib/aws-ec2";
import Settings from "./settings";

interface Request {
  stack: Construct;
  service: IService;
  settings: Settings;
}

export interface IService {
  name: string;
  connectionSecurityGroups: ec2.ISecurityGroup[];
}

export default function ({ stack, service, settings }: Request) {
  // Allow connection from services to the database
  if (settings.dbSecurityGroupId) {
    const dbSecurityGroupConstructId = `db-sg-${service.name}`;
    let dbSg = ec2.SecurityGroup.fromLookupById(
      stack,
      dbSecurityGroupConstructId,
      settings.dbSecurityGroupId
    );

    dbSg.connections.allowFrom(
      new ec2.Connections({
        securityGroups: service.connectionSecurityGroups,
      }),
      ec2.Port.tcp(1433),
      `allow traffic on port 1433 from ${service.name}`
    );
  }
}
