// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.Knob
{
    public class RuntimeKnobSource : IKnobSource
    {
        private string _runTimeVar;
        private string _defaultValue = null;

        public string DefaultValue
        {
            get => _defaultValue;
            set
            {
                _defaultValue ??= value;
            }
        }

        public RuntimeKnobSource(string runTimeVar)
        {
            _runTimeVar = runTimeVar;
        }

        public RuntimeKnobSource(string runTimeVar, string defaultValue)
        {
            _runTimeVar = runTimeVar;
            _defaultValue = defaultValue;
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            string value = null;
            try
            {
                value = context.GetVariableValueOrDefault(_runTimeVar);
            }
            catch (NotSupportedException)
            {
                throw new NotSupportedException($"{nameof(RuntimeKnobSource)} not supported for context type {context.GetType()}");
            }

            if (!string.IsNullOrEmpty(value))
            {
                if (_defaultValue != null)
                {
                    return new KnobValue(value, _defaultValue, KnobSourceType.Runtime);
                }

                return new KnobValue(value, KnobSourceType.Runtime);
            }
            return null;
        }

        public string GetDisplayString()
        {
            return $"$({_runTimeVar})";
        }
    }
}
