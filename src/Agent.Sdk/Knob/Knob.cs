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
        public DeprecatedKnob(string name, string description, string defaultValue, params IKnobSource[] sources) : base(name, description, defaultValue, sources)
        {
            DeprecationInfo = "";
        }

        public DeprecatedKnob(string name, string description, string defaultValue, string deprecationInfo, params IKnobSource[] sources) : base(name, description, defaultValue, sources)
        {
            DeprecationInfo = deprecationInfo;
        }
    }

    public class ExperimentalKnob : Knob
    {
        public override bool IsExperimental => true;
        public ExperimentalKnob(string name, string description, string defaultValue, params IKnobSource[] sources) : base(name, description, defaultValue, sources)
        {
        }
    }

    public class SecretKnob : Knob
    {
        public SecretKnob(string name, string defaultValue, string description, params IKnobSource[] sources) : base(name, defaultValue, description, sources)
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
        public string DefaultValue { get; }

        private Knob(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Knob(
            string name,
            string description,
            string defaultValue,
            params IKnobSource[] sources) : this(name, description)
        {
            DefaultValue = defaultValue;
            Source = new CompositeKnobSource(DefaultValue, sources);
        }

        public Knob(
            string name,
            string description,
            int defaultValue,
            params IKnobSource[] sources) : this(name, description)
        {
            DefaultValue = defaultValue.ToString();
            Source = new CompositeKnobSource(DefaultValue, sources);
        }

        public Knob(
            string name,
            string description,
            bool defaultValue,
            params IKnobSource[] sources) : this(name, description)
        {
            DefaultValue = defaultValue.ToString();
            Source = new CompositeKnobSource(DefaultValue, sources);
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
