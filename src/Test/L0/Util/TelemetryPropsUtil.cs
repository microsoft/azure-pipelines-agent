// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Test.L0.Util
{
    class TelemetryPropsUtil
    {
        public static void AssertPipelineData(Dictionary<string, Object> telemetryProps)
        {
            Assert.True((string)(telemetryProps["StageName"]) == "Stage1");
            Assert.True((string)(telemetryProps["PhaseName"]) == "Phase1");
            Assert.True((string)(telemetryProps["JobName"]) == "Job1");
            Assert.True((int)(telemetryProps["StageAttempt"]) == 1);
            Assert.True((int)(telemetryProps["PhaseAttempt"]) == 1);
            Assert.True((int)(telemetryProps["JobAttempt"]) == 1);
        }

        public static void AddPipelineDataIntoAgentContext(Dictionary<string, VariableValue> agentContextVariable)
        {
            agentContextVariable.Add("system.stageName", new VariableValue("Stage1"));
            agentContextVariable.Add("system.stageAttempt", new VariableValue("1"));
            agentContextVariable.Add("system.phaseName", new VariableValue("Phase1"));
            agentContextVariable.Add("system.phaseAttempt", new VariableValue("1"));
            agentContextVariable.Add("system.jobName", new VariableValue("Job1"));
            agentContextVariable.Add("system.jobAttempt", new VariableValue("1"));
        }
    }
}
