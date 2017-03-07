using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines.TextTemplating;
using YamlDotNet.Serialization;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines
{
    public static class PipelineParser
    {
        public static async Task<Process> LoadAsync(String filePath)
        {
            // Load the target file.
            filePath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
            Process process = await LoadFileAsync<Process, ProcessConverter>(filePath);
            await ResolveTemplatesAsync(process, rootDirectory: Path.GetDirectoryName(filePath));

            // Create implied levels for the process.
            if (process.Jobs != null)
            {
                var newPhase = new Phase { Jobs = process.Jobs, Name = process.Name };
                process.Phases = new List<IPhase>();
                process.Phases.Add(newPhase);
                process.Jobs = null;
            }
            else if (process.Steps != null)
            {
                var newJob = new Job { Steps = process.Steps, Name = process.Name };
                var newPhase = new Phase { Jobs = new List<IJob>() };
                newPhase.Jobs.Add(newJob);
                process.Phases = new List<IPhase>();
                process.Phases.Add(newPhase);
                process.Steps = null;
            }

            // Create implied levels for each phase.
            foreach (Phase phase in process.Phases ?? new List<IPhase>(0))
            {
                if (phase.Steps != null)
                {
                    var newJob = new Job { Steps = phase.Steps };
                    phase.Jobs = new List<IJob>(new IJob[] { newJob });
                    phase.Steps = null;
                }
            }

            // Record all known phase/job names.
            var knownPhaseNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var knownJobNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (Phase phase in process.Phases ?? new List<IPhase>(0))
            {
                knownPhaseNames.Add(phase.Name);
                foreach (Job job in phase.Jobs ?? new List<IJob>(0))
                {
                    knownJobNames.Add(job.Name);
                }
            }

            // Generate missing names.
            Int32? nextPhase = null;
            Int32? nextJob = null;
            foreach (Phase phase in process.Phases ?? new List<IPhase>(0))
            {
                if (String.IsNullOrEmpty(phase.Name))
                {
                    String candidateName = String.Format(CultureInfo.InvariantCulture, "Phase{0}", nextPhase);
                    while (!knownPhaseNames.Add(candidateName))
                    {
                        nextPhase = (nextPhase ?? 1) + 1;
                        candidateName = String.Format(CultureInfo.InvariantCulture, "Phase{0}", nextPhase);
                    }

                    phase.Name = candidateName;
                }

                foreach (Job job in phase.Jobs ?? new List<IJob>(0))
                {
                    if (String.IsNullOrEmpty(job.Name))
                    {
                        String candidateName = String.Format(CultureInfo.InvariantCulture, "Build{0}", nextJob);
                        while (!knownPhaseNames.Add(candidateName))
                        {
                            nextJob = (nextJob ?? 1) + 1;
                            candidateName = String.Format(CultureInfo.InvariantCulture, "Build{0}", nextJob);
                        }

                        job.Name = candidateName;
                    }
                }
            }

            Dump<Process, ProcessConverter>("After resolution", process);
            return process;
        }

        private static async Task ResolveTemplatesAsync(Process process, String rootDirectory)
        {
            if (process.Template != null)
            {
                // Load the template.
                String templateFilePath = Path.Combine(rootDirectory, process.Template.Name);
                ProcessTemplate template = await LoadFileAsync<ProcessTemplate, ProcessTemplateConverter>(templateFilePath, process.Template.Parameters);

                // Resolve template references within the template.
                if (template.Phases != null)
                {
                    await ResolveTemplatesAsync(template.Phases, rootDirectory: Path.GetDirectoryName(templateFilePath));
                }
                else if (template.Jobs != null)
                {
                    await ResolveTemplatesAsync(template.Jobs, rootDirectory: Path.GetDirectoryName(templateFilePath));
                }
                else if (template.Steps != null)
                {
                    await ResolveTemplatesAsync(template.Steps, rootDirectory: Path.GetDirectoryName(templateFilePath));
                }

                // Merge the template.
                ApplyStepOverrides(process.Template, template);
                process.Phases = template.Phases;
                process.Jobs = template.Jobs;
                process.Steps = template.Steps;
                process.Resources = MergeResources(process.Resources, template.Resources);
                process.Template = null;
            }
            // Resolve nested template references.
            else if (process.Phases != null)
            {
                await ResolveTemplatesAsync(process.Phases, rootDirectory);
            }
            else if (process.Jobs != null)
            {
                await ResolveTemplatesAsync(process.Jobs, rootDirectory);
            }
            else if (process.Steps != null)
            {
                await ResolveTemplatesAsync(process.Steps, rootDirectory);
            }
        }

        private static async Task ResolveTemplatesAsync(List<IPhase> phases, String rootDirectory)
        {
            phases = phases ?? new List<IPhase>(0);
            for (int i = 0 ; i < phases.Count ; )
            {
                if (phases[i] is PhasesTemplateReference)
                {
                    // Load the template.
                    var reference = phases[i] as PhasesTemplateReference;
                    String templateFilePath = Path.Combine(rootDirectory, reference.Name);
                    PhasesTemplate template = await LoadFileAsync<PhasesTemplate, PhasesTemplateConverter>(templateFilePath, reference.Parameters);

                    // Resolve template references within the template.
                    if (template.Jobs != null)
                    {
                        await ResolveTemplatesAsync(template.Jobs, rootDirectory: Path.GetDirectoryName(templateFilePath));
                    }
                    else if (template.Steps != null)
                    {
                        await ResolveTemplatesAsync(template.Steps, rootDirectory: Path.GetDirectoryName(templateFilePath));
                    }

                    // Merge the template.
                    ApplyStepOverrides(reference, template);
                    phases.RemoveAt(i);
                    if (template.Phases != null)
                    {
                        phases.InsertRange(i, template.Phases);
                        i += template.Phases.Count;
                    }
                    else if (template.Jobs != null)
                    {
                        var newPhase = new Phase { Jobs = template.Jobs };
                        phases.Insert(i, newPhase);
                        i++;
                    }
                    else if (template.Steps != null)
                    {
                        var newJob = new Job { Steps = template.Steps };
                        var newPhase = new Phase { Jobs = new List<IJob>(new IJob[] { newJob }) };
                        phases.Insert(i, newPhase);
                        i++;
                    }
                }
                else
                {
                    // Resolve nested template references.
                    var phase = phases[i] as Phase;
                    if (phase.Jobs != null)
                    {
                        await ResolveTemplatesAsync(phase.Jobs, rootDirectory);
                    }
                    else if (phase.Steps != null)
                    {
                        await ResolveTemplatesAsync(phase.Steps, rootDirectory);
                    }

                    i++;
                }
            }
        }

        private static async Task ResolveTemplatesAsync(List<IJob> jobs, String rootDirectory)
        {
            jobs = jobs ?? new List<IJob>(0);
            for (int i = 0 ; i < jobs.Count ; )
            {
                if (jobs[i] is JobsTemplateReference)
                {
                    // Load the template.
                    var reference = jobs[i] as JobsTemplateReference;
                    String templateFilePath = Path.Combine(rootDirectory, reference.Name);
                    JobsTemplate template = await LoadFileAsync<JobsTemplate, JobsTemplateConverter>(templateFilePath, reference.Parameters);

                    // Resolve template references within the template.
                    if (template.Steps != null)
                    {
                        await ResolveTemplatesAsync(template.Steps, rootDirectory: Path.GetDirectoryName(templateFilePath));
                    }

                    // Merge the template.
                    ApplyStepOverrides(reference, template);
                    jobs.RemoveAt(i);
                    if (template.Jobs != null)
                    {
                        jobs.InsertRange(i, template.Jobs);
                        i += template.Jobs.Count;
                    }
                    else if (template.Steps != null)
                    {
                        var newJob = new Job { Steps = template.Steps };
                        jobs.Insert(i, newJob);
                        i++;
                    }
                }
                else
                {
                    // Resolve nested template references.
                    var job = jobs[i] as Job;
                    if (job.Steps != null)
                    {
                        await ResolveTemplatesAsync(job.Steps, rootDirectory);
                    }

                    i++;
                }
            }
        }

        private static async Task ResolveTemplatesAsync(List<IStep> steps, String rootDirectory)
        {
            steps = steps ?? new List<IStep>(0);
            for (int i = 0 ; i < steps.Count ; )
            {
                if (steps[i] is StepsTemplateReference)
                {
                    // Load the template.
                    var reference = steps[i] as StepsTemplateReference;
                    String templateFilePath = Path.Combine(rootDirectory, reference.Name);
                    StepsTemplate template = await LoadFileAsync<StepsTemplate, StepsTemplateConverter>(templateFilePath, reference.Parameters);

                    // Merge the template.
                    ApplyStepOverrides(reference.StepOverrides, template.Steps);
                    steps.RemoveAt(i);
                    if (template.Steps != null)
                    {
                        steps.InsertRange(i, template.Steps);
                        i += template.Steps.Count;
                    }
                }
                else
                {
                    i++;
                }
            }
        }

        private static void ApplyStepOverrides(PhasesTemplateReference reference, PhasesTemplate template)
        {
            // Select by phase name.
            var byPhaseNames =
                (from PhaseSelector phaseSelector in reference.PhaseSelectors ?? new List<PhaseSelector>(0)
                join Phase phase in template.Phases ?? new List<IPhase>(0)
                on phaseSelector.Name equals phase.Name
                select new { Selector = phaseSelector, Phase = phase })
                .ToArray();
            foreach (var byPhaseName in byPhaseNames)
            {
                // Select by phase name + job name.
                var byPhaseNamesAndJobNames =
                    (from JobSelector jobSelector in byPhaseName.Selector.JobSelectors ?? new List<JobSelector>(0)
                    join Job job in byPhaseName.Phase.Jobs ?? new List<IJob>(0)
                    on jobSelector.Name equals job.Name
                    select new { Selector = jobSelector, Job = job })
                    .ToArray();
                foreach (var byPhaseNameAndJobName in byPhaseNamesAndJobNames)
                {
                    // Apply overrides from phase + job selectors.
                    ApplyStepOverrides(byPhaseNameAndJobName.Selector.StepOverrides, byPhaseNameAndJobName.Job.Steps);
                }
            }

            // Select by job name.
            var allJobs =
                (template.Phases ?? new List<IPhase>(0))
                .Cast<Phase>()
                .SelectMany((Phase phase) => phase.Jobs ?? new List<IJob>(0))
                .Concat(template.Jobs ?? new List<IJob>(0))
                .ToArray();
            var byJobNames =
                (from JobSelector jobSelector in reference.JobSelectors ?? new List<JobSelector>(0)
                join Job job in allJobs
                on jobSelector.Name equals job.Name
                select new { Selector = jobSelector, Job = job })
                .ToArray();

            // Apply overrides from job selectors.
            foreach (var byJobName in byJobNames)
            {
                ApplyStepOverrides(byJobName.Selector.StepOverrides, byJobName.Job.Steps);
            }

            // Apply overrides from phase selectors.
            foreach (var byPhaseName in byPhaseNames)
            {
                foreach (Job job in byPhaseName.Phase.Jobs ?? new List<IJob>(0))
                {
                    ApplyStepOverrides(byPhaseName.Selector.StepOverrides, job.Steps);
                }
            }

            // Apply unqualified overrides.
            var allStepLists =
                allJobs
                .Cast<Job>()
                .Select((Job job) => job.Steps ?? new List<IStep>(0))
                .Append(template.Steps ?? new List<IStep>(0))
                .ToArray();
            foreach (List<IStep> stepList in allStepLists)
            {
                ApplyStepOverrides(reference.StepOverrides, stepList);
            }
        }

        private static void ApplyStepOverrides(JobsTemplateReference reference, JobsTemplate template)
        {
            // Select by job name.
            var byJobNames =
                (from JobSelector jobSelector in reference.JobSelectors ?? new List<JobSelector>(0)
                join Job job in template.Jobs ?? new List<IJob>(0)
                on jobSelector.Name equals job.Name
                select new { Selector = jobSelector, Job = job })
                .ToArray();

            // Apply overrides from job selectors.
            foreach (var byJobName in byJobNames)
            {
                ApplyStepOverrides(byJobName.Selector.StepOverrides, byJobName.Job.Steps);
            }

            // Apply unqualified overrides.
            var allStepLists =
                (template.Jobs ?? new List<IJob>(0))
                .Cast<Job>()
                .Select((Job job) => job.Steps ?? new List<IStep>(0))
                .Append(template.Steps ?? new List<IStep>(0))
                .ToArray();
            foreach (List<IStep> stepList in allStepLists)
            {
                ApplyStepOverrides(reference.StepOverrides, stepList);
            }
        }

        private static void ApplyStepOverrides(IDictionary<String, List<ISimpleStep>> stepOverrides, List<IStep> steps)
        {
            stepOverrides = stepOverrides ?? new Dictionary<String, List<ISimpleStep>>(0);
            steps = steps ?? new List<IStep>(0);
            for (int i = 0 ; i < steps.Count ; )
            {
                if (steps[i] is StepsPhase)
                {
                    var stepsPhase = steps[i] as StepsPhase;
                    List<ISimpleStep> overrides;
                    if (stepOverrides.TryGetValue(stepsPhase.Name, out overrides))
                    {
                        steps.RemoveAt(i);
                        overrides = overrides ?? new List<ISimpleStep>(0);
                        steps.InsertRange(i, overrides.Select(x => x.Clone()));
                        i += overrides.Count;
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }
        }

        private static List<ProcessResource> MergeResources(List<ProcessResource> overrides, List<ProcessResource> imports)
        {
            overrides = overrides ?? new List<ProcessResource>(0);
            imports = imports ?? new List<ProcessResource>(0);
            var result = new List<ProcessResource>(overrides);
            var knownOverrides = new HashSet<String>(overrides.Select(x => x.Name));
            result.AddRange(imports.Where(x => !knownOverrides.Contains(x.Name)));
            return result;
        }

        private static async Task<TResult> LoadFileAsync<TResult, TConverter>(String path, IDictionary<String, Object> mustacheContext = null)
            where TConverter : YamlTypeConverter, new() 
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
            deserializerBuilder.WithTypeConverter(new TConverter());
            Deserializer deserializer = deserializerBuilder.Build();
            TResult result = deserializer.Deserialize<TResult>(mustacheReplaced);
            Dump<TResult, TConverter>($"{Path.GetFileName(path)} after deserialization ", result);
            return result;
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

        private static void Dump<TObject, TConverter>(String header, TObject value)
            where TConverter : YamlTypeConverter, new() 
        {
            Console.WriteLine();
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine($"* {header}");
            Console.WriteLine(String.Empty.PadRight(80, '*'));
            Console.WriteLine();
            SerializerBuilder serializerBuilder = new SerializerBuilder();
            serializerBuilder.WithTypeConverter(new TConverter());
            Serializer serializer = serializerBuilder.Build();
            Console.WriteLine(serializer.Serialize(value));
        }
    }
}
