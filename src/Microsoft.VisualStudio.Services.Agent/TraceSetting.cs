// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    [DataContract]
    public class TraceSetting
    {
        private static UtilKnobValueContext _knobContext = UtilKnobValueContext.Instance();

        public TraceSetting(HostType hostType, IKnobValueContext knobContext = null)
        {
            if (hostType == HostType.Agent)
            {
                DefaultTraceLevel = TraceLevel.Verbose;
                return;
            }

            DefaultTraceLevel = TraceLevel.Info;

#if DEBUG
            DefaultTraceLevel = TraceLevel.Verbose;
#endif

            if (hostType == HostType.Worker)
            {
                var contextToUse = knobContext ?? _knobContext;
                try
                {
                    bool vstsAgentTrace = AgentKnobs.TraceVerbose.GetValue(contextToUse).AsBoolean();
                    if (vstsAgentTrace)
                    {
                        DefaultTraceLevel = TraceLevel.Verbose;
                    }
                }
                catch (NotSupportedException)
                {
                    // Some knob sources (like RuntimeKnobSource) aren't supported by all contexts
                    // (e.g., UtilKnobValueContext). In that case, ignore and fall back to defaults.
                }
            }
        }

        [DataMember(EmitDefaultValue = false)]
        public TraceLevel DefaultTraceLevel
        {
            get;
            set;
        }

        public Dictionary<String, TraceLevel> DetailTraceSetting
        {
            get
            {
                if (m_detailTraceSetting == null)
                {
                    m_detailTraceSetting = new Dictionary<String, TraceLevel>(StringComparer.OrdinalIgnoreCase);
                }
                return m_detailTraceSetting;
            }
        }

        [DataMember(EmitDefaultValue = false, Name = "DetailTraceSetting")]
        private Dictionary<String, TraceLevel> m_detailTraceSetting;
    }

    [DataContract]
    public enum TraceLevel
    {
        [EnumMember]
        Off = 0,

        [EnumMember]
        Critical = 1,

        [EnumMember]
        Error = 2,

        [EnumMember]
        Warning = 3,

        [EnumMember]
        Info = 4,

        [EnumMember]
        Verbose = 5,
    }

    public static class TraceLevelExtensions
    {
        public static SourceLevels ToSourceLevels(this TraceLevel traceLevel)
        {
            switch (traceLevel)
            {
                case TraceLevel.Off:
                    return SourceLevels.Off;
                case TraceLevel.Critical:
                    return SourceLevels.Critical;
                case TraceLevel.Error:
                    return SourceLevels.Error;
                case TraceLevel.Warning:
                    return SourceLevels.Warning;
                case TraceLevel.Info:
                    return SourceLevels.Information;
                case TraceLevel.Verbose:
                    return SourceLevels.Verbose;
                default:
                    return SourceLevels.Information;
            }
        }
    }
}