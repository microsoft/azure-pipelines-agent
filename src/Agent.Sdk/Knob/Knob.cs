// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.Knob
{
    public class DeprecatedKnob : Knob
    {
        public override bool IsDeprecated => true;
        public string DeprecationInfo;
        public DeprecatedKnob(string name, string description, params IKnobSource[] sources) : base(name, description, sources)
        {
            DeprecationInfo = "";
        }

        public DeprecatedKnob(string name, string description, string deprecationInfo, params IKnobSource[] sources) : base(name, description, sources)
        {
            DeprecationInfo = deprecationInfo;
        }
    }

    public class ExperimentalKnob : Knob
    {
        public override bool IsExperimental => true;
        public ExperimentalKnob(string name, string description, params IKnobSource[] sources) : base(name, description, sources)
        {
        }
    }

    public class SecretKnob : Knob
    {
        public SecretKnob(string name, string description, params IKnobSource[] sources) : base(name, description, sources)
        {
        }
    }

    public class Knob
    {
        public string Name { get; private set; }
        public ICompositeKnobSource Source { get; private set; }
        public string Description { get; private set; }
        public virtual bool IsDeprecated => false;  // is going away at a future date
        public virtual bool IsExperimental => false; // may go away at a future date

        public Knob(string name, string description, params IKnobSource[] sources)
        {
            Name = name;
            Description = description;
            Source = new CompositeKnobSource(sources);
        }

        public Knob()
        {
        }

        public KnobValue GetValue(IKnobValueContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(Source, nameof(Source));

            return Source.GetValue(context);
        }

        public KnobValue GetValue<T>(IKnobValueContext context)
        {
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(Source, nameof(Source));

            return Source.GetValue<T>(context);
        }

        public static List<Knob> GetAllKnobsFor<T>()
        {
            Type type = typeof(T);
            List<Knob> allKnobs = new List<Knob>();
            foreach (var info in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var instance = new Knob();
                var locatedValue = info.GetValue(instance) as Knob;

                if (locatedValue != null)
                {
                    allKnobs.Add(locatedValue);
                }
            }
            return allKnobs;
        }
    }
}
