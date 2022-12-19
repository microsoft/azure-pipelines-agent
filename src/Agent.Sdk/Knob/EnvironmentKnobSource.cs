// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.Knob
{

    public class EnvironmentKnobSource : IEnvironmentKnobSource
    {
        private string _envVar;

        public EnvironmentKnobSource(string envVar)
        {
            _envVar = envVar;
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            var scopedEnvironment = context.GetScopedEnvironment();
            var value = scopedEnvironment.GetEnvironmentVariable(_envVar);
            if (!string.IsNullOrEmpty(value))
            {
                return new KnobValue(value, this);
            }
            return null;
        }

        public string GetDisplayString()
        {
            return $"${{{_envVar}}}";
        }

        public string GetEnvironmentVariableName()
        {
            return _envVar;
        }
    }

}
