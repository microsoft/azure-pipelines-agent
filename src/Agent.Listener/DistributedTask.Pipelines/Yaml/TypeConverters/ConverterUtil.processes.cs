// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.Yaml.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.Yaml.TypeConverters
{
    internal static partial class ConverterUtil
    {
        internal static IList<ProcessResource> ReadProcessResources(IParser parser)
        {
            var result = new List<ProcessResource>();
            
            // Check if this is a sequence (flat structure) or mapping (nested structure)
            if (parser.Accept<SequenceStart>())
            {
                // Flat structure: resources: - repo: self
                parser.Expect<SequenceStart>();
                while (parser.Allow<SequenceEnd>() == null)
                {
                    parser.Expect<MappingStart>();
                    Scalar scalar = parser.Expect<Scalar>();
                    switch (scalar.Value ?? String.Empty)
                    {
                        case YamlConstants.Endpoint:
                        case YamlConstants.Repo:
                            break;
                        case YamlConstants.Pipeline:
                            throw new SyntaxErrorException(scalar.Start, scalar.End, 
                                $"Pipeline resources must be defined under 'pipelines:' section, not as flat resources");
                        default:
                            throw new SyntaxErrorException(scalar.Start, scalar.End, $"Unexpected resource type: '{scalar.Value}'");
                    }

                    var resource = new ProcessResource { Type = scalar.Value };
                    resource.Name = ReadNonEmptyString(parser);
                    while (parser.Allow<MappingEnd>() == null)
                    {
                        string dataKey = ReadNonEmptyString(parser);
                        if (parser.Accept<MappingStart>())
                        {
                            resource.Data[dataKey] = ReadMapping(parser);
                        }
                        else if (parser.Accept<SequenceStart>())
                        {
                            resource.Data[dataKey] = ReadSequence(parser);
                        }
                        else
                        {
                            resource.Data[dataKey] = parser.Expect<Scalar>().Value ?? String.Empty;
                        }
                    }

                    result.Add(resource);
                }
            }
            else if (parser.Accept<MappingStart>())
            {
                // Nested structure: resources: pipelines: - pipeline: my
                parser.Expect<MappingStart>();
                while (parser.Allow<MappingEnd>() == null)
                {
                    Scalar sectionScalar = parser.Expect<Scalar>();
                    switch (sectionScalar.Value ?? String.Empty)
                    {
                        case YamlConstants.Pipelines:
                            // Read pipeline resources
                            parser.Expect<SequenceStart>();
                            while (parser.Allow<SequenceEnd>() == null)
                            {
                                parser.Expect<MappingStart>();
                                Scalar pipelineTypeScalar = parser.Expect<Scalar>();
                                
                                if (pipelineTypeScalar.Value != YamlConstants.Pipeline)
                                {
                                    throw new SyntaxErrorException(pipelineTypeScalar.Start, pipelineTypeScalar.End, 
                                        $"Expected 'pipeline' but found: '{pipelineTypeScalar.Value}'");
                                }

                                var pipelineResource = new ProcessResource { Type = YamlConstants.Pipeline };
                                pipelineResource.Name = ReadNonEmptyString(parser);
                                
                                while (parser.Allow<MappingEnd>() == null)
                                {
                                    string dataKey = ReadNonEmptyString(parser);
                                    if (parser.Accept<MappingStart>())
                                    {
                                        pipelineResource.Data[dataKey] = ReadMapping(parser);
                                    }
                                    else if (parser.Accept<SequenceStart>())
                                    {
                                        pipelineResource.Data[dataKey] = ReadSequence(parser);
                                    }
                                    else
                                    {
                                        pipelineResource.Data[dataKey] = parser.Expect<Scalar>().Value ?? String.Empty;
                                    }
                                }

                                result.Add(pipelineResource);
                            }
                            break;
                        // TODO: Add support for other resource sections (repositories, etc.) if needed
                        default:
                            throw new SyntaxErrorException(sectionScalar.Start, sectionScalar.End, 
                                $"Unexpected resource section: '{sectionScalar.Value}'");
                    }
                }
            }
            else
            {
                throw new SyntaxErrorException(parser.Current.Start, parser.Current.End, 
                    "Expected resources to be either a sequence or mapping");
            }

            return result;
        }

        internal static ProcessTemplateReference ReadProcessTemplateReference(IParser parser)
        {
            parser.Expect<MappingStart>();
            ReadExactString(parser, YamlConstants.Name);
            var result = new ProcessTemplateReference { Name = ReadNonEmptyString(parser) };
            while (parser.Allow<MappingEnd>() == null)
            {
                Scalar scalar = parser.Expect<Scalar>();
                SetProperty(parser, result, scalar);
            }

            return result;
        }

        internal static void WriteProcessResources(IEmitter emitter, IList<ProcessResource> resources)
        {
            // Separate pipeline resources from other resources
            var pipelineResources = resources.Where(r => r.Type == YamlConstants.Pipeline).ToList();
            var otherResources = resources.Where(r => r.Type != YamlConstants.Pipeline).ToList();
            
            // If we only have other resources (repo, endpoint), use flat structure
            if (pipelineResources.Count == 0)
            {
                emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Block));
                foreach (ProcessResource resource in otherResources)
                {
                    WriteProcessResource(emitter, resource);
                }
                emitter.Emit(new SequenceEnd());
            }
            // If we only have pipeline resources, use nested structure
            else if (otherResources.Count == 0)
            {
                emitter.Emit(new MappingStart());
                emitter.Emit(new Scalar(YamlConstants.Pipelines));
                emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Block));
                foreach (ProcessResource resource in pipelineResources)
                {
                    WriteProcessResource(emitter, resource);
                }
                emitter.Emit(new SequenceEnd());
                emitter.Emit(new MappingEnd());
            }
            // If we have both, pipeline resources should not exist in flat structure, this is an error
            else
            {
                throw new InvalidOperationException("Pipeline resources cannot be mixed with other resource types. " +
                    "Pipeline resources must be defined under 'pipelines:' section.");
            }
        }

        private static void WriteProcessResource(IEmitter emitter, ProcessResource resource)
        {
            emitter.Emit(new MappingStart());
            emitter.Emit(new Scalar(resource.Type));
            emitter.Emit(new Scalar(resource.Name));
            if (resource.Data != null && resource.Data.Count > 0)
            {
                foreach (KeyValuePair<String, Object> pair in resource.Data)
                {
                    emitter.Emit(new Scalar(pair.Key));
                    if (pair.Value is String)
                    {
                        emitter.Emit(new Scalar(pair.Value as string));
                    }
                    else if (pair.Value is Dictionary<String, Object>)
                    {
                        WriteMapping(emitter, pair.Value as Dictionary<String, Object>);
                    }
                    else
                    {
                        WriteSequence(emitter, pair.Value as List<Object>);
                    }
                }
            }
            emitter.Emit(new MappingEnd());
        }
    }
}
