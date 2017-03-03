using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating;
using YamlDotNet.Serialization;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines
{
    public static class PipelineParser
    {
        public static async Task<Pipeline> LoadAsync(String filePath)
        {
            // Load the target file.
            Pipeline pipeline = await LoadFileAsync<Pipeline>(filePath);
            if (pipeline.Template != null)
            {
                // Load the template.
                String directoryPath = Path.GetDirectoryName(filePath);
                String templatePath = Path.Combine(directoryPath, pipeline.Template.Name);
                PipelineTemplate template = await LoadFileAsync<PipelineTemplate>(templatePath, mustacheContext: pipeline.Template.Parameters);

                // Merge the target and template.
                var mergedPipeline = new Pipeline();
                mergedPipeline = new Pipeline();
                mergedPipeline.Resources = new List<PipelineResource>(); // Append resources.
                mergedPipeline.Resources.AddRange(pipeline.Resources ?? new List<PipelineResource>());
                mergedPipeline.Resources.AddRange(template.Resources ?? new List<PipelineResource>());
                mergedPipeline.Jobs = template.Jobs;

                // Overlay the step hooks.
                if (pipeline.Template.StepHooks != null)
                {
                    foreach (PipelineJob job in mergedPipeline.Jobs ?? new List<PipelineJob>(0))
                    {
                        foreach (PipelineJobStep step in job.Steps ?? new List<PipelineJobStep>(0))
                        {
                            List<ISimplePipelineJobStep> replacementSteps;
                            var stepHook = step as StepHook;
                            if (stepHook != null && pipeline.Template.StepHooks.TryGetValue(stepHook.Name, out replacementSteps))
                            {
                                replacementSteps = replacementSteps ?? new List<ISimplePipelineJobStep>(0);
                                stepHook.Steps = new List<ISimplePipelineJobStep>(replacementSteps.Count);
                                foreach (ISimplePipelineJobStep replacementStep in replacementSteps)
                                {
                                    stepHook.Steps.Add(replacementStep.Clone() as ISimplePipelineJobStep);
                                }
                            }
                        }
                    }
                }

                pipeline = mergedPipeline;
                Dump("Merged pipeline", pipeline);
            }

            return pipeline;
        }

        private static async Task<T> LoadFileAsync<T>(String path, IDictionary<String, Object> mustacheContext = null)
        {
            String mustacheReplaced;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
            {
                StreamReader reader = null;
                try
                {
                    // Read front-matter
                    IDictionary<String, Object> frontMatter = null;
                    reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                    String line = await reader.ReadLineAsync();
                    if (!String.Equals(line, "---", StringComparison.Ordinal))
                    {
                        // No front-matter. Reset the stream.
                        reader.Dispose();
                        stream.Seek(0, SeekOrigin.Begin);
                        reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
                    }
                    else
                    {
                        // Deseralize front-matter.
                        StringBuilder frontMatterBuilder = new StringBuilder();
                        while (true)
                        {
                            line = await reader.ReadLineAsync();
                            if (line == null)
                            {
                                // TODO: better error message.
                                throw new Exception("expected end of front-matter section");
                            }
                            else if (String.Equals(line, "---", StringComparison.Ordinal))
                            {
                                break;
                            }
                            else
                            {
                                frontMatterBuilder.AppendLine(line);
                            }
                        }

                        var frontMatterDeserializer = new Deserializer();
                        // todo: try/catch and better error message.
                        frontMatter = frontMatterDeserializer.Deserialize<IDictionary<String, Object>>(frontMatterBuilder.ToString());
                    }

                    // Merge the mustache replace context.
                    frontMatter = frontMatter ?? new Dictionary<String, Object>();
                    if (mustacheContext != null)
                    {
                        foreach (KeyValuePair<String, Object> pair in mustacheContext)
                        {
                            frontMatter[pair.Key] = pair.Value;
                        }
                    }

                    // Mustache-replace
                    var mustacheParser = new MustacheTemplateParser();
                    mustacheReplaced = mustacheParser.ReplaceValues(
                        template: await reader.ReadToEndAsync(),
                        replacementContext: frontMatter);
                    Dump($"{Path.GetFileName(path)} after mustache replacement", mustacheReplaced);
                }
                finally
                {
                    reader?.Dispose();
                    reader = null;
                }
            }

            // Deserialize
            DeserializerBuilder deserializerBuilder = new DeserializerBuilder();
            deserializerBuilder.WithTypeConverter(new PipelineStepYamlConverter());
            deserializerBuilder.WithTypeConverter(new PipelineValueYamlConverter());
            // deserializerBuilder.WithTypeConverter(new PipelineIteratorValueYamlConverter());
            // deserializerBuilder.WithTypeConverter(new VariableGroupTemplateYamlConverter());
            Deserializer deserializer = deserializerBuilder.Build();
            T pipeline = deserializer.Deserialize<T>(mustacheReplaced);
            Dump($"{Path.GetFileName(path)} after deserialization ", pipeline);
            return pipeline;
        }

        private static void Dump(String header, String value)
        {
            Console.WriteLine();
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine($"* {header}");
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine();
            using (StringReader reader = new StringReader(value))
            {
                Int32 lineNumber = 1;
                String line = reader.ReadLine();
                while (line != null)
                {
                    Console.WriteLine($"{lineNumber.ToString().PadLeft(4)}: {line}");
                    line = reader.ReadLine();
                    lineNumber++;
                }
            }
        }

        private static void Dump(String header, Object value)
        {
            Console.WriteLine();
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine($"* {header}");
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine();
            Serializer s = new Serializer();
            Console.WriteLine(s.Serialize(value));
        }
    }
}
