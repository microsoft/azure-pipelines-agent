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
            Pipeline pipeline = await LoadFileAsync<Pipeline>(filePath);
            if (pipeline.Template != null)
            {
                String directoryPath = Path.GetDirectoryName(filePath);
                String templatePath = Path.Combine(directoryPath, pipeline.Template.Name);
                PipelineTemplate template = await LoadFileAsync<PipelineTemplate>(templatePath, mustacheContext: pipeline.Template.Parameters);

                var mergedPipeline = new Pipeline();
                mergedPipeline = new Pipeline();
                mergedPipeline.Resources = new List<PipelineResource>();
                mergedPipeline.Resources.AddRange(pipeline.Resources ?? new List<PipelineResource>());
                mergedPipeline.Resources.AddRange(template.Resources ?? new List<PipelineResource>());
                mergedPipeline.Jobs = template.Jobs;
                pipeline = mergedPipeline;
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
            deserializerBuilder.WithTypeConverter(new PipelineIteratorValueYamlConverter());
            deserializerBuilder.WithTypeConverter(new VariableGroupTemplateYamlConverter());
            Deserializer deserializer = deserializerBuilder.Build();
            T pipeline = deserializer.Deserialize<T>(mustacheReplaced);
            return pipeline;
        }
    }
}
