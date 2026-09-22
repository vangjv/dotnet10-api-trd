import * as ec2 from "aws-cdk-lib/aws-ec2";
import { Construct } from "constructs";
import Settings from "../lib/settings";

export default function getVpc(construct: Construct, settings: Settings) {
  return ec2.Vpc.fromLookup(construct, "vpc", {
    vpcName: settings.vpcName,
  });
}
