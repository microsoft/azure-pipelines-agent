// using System;
// using System.Collections.Generic;

// namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines
// {
//     /// <summary>
//     /// Represents a context for realizing a template into an expanded and executable form.
//     /// </summary>
//     public class PipelineTemplateContext
//     {
//         public PipelineTemplateContext()
//         {
//         }

//         /// <summary>
//         /// Gets the current set of inputs which are used as the default context for template replacement.
//         /// </summary>
//         public IDictionary<String, Object> Inputs
//         {
//             get
//             {
//                 return m_inputs;
//             }
//         }

//         /// <summary>
//         /// Gets the current set of groups which are used for injecting behaviors into named marker points.
//         /// </summary>
//         public IDictionary<String, TaskGroup> Groups
//         {
//             get
//             {
//                 return m_groups;
//             }
//         }

//         /// <summary>
//         /// Given a template for evaluation or replacement this function evaluates the current context and returns
//         /// the current value for the specified template. If the resolved value does not map to the expected type an
//         /// exception is raised.
//         /// </summary>
//         /// <typeparam name="T">The expected type of the resolved value</typeparam>
//         /// <param name="template">The value which should be resolved</param>
//         /// <returns>The resolved type from the current context</returns>
//         public T ResolveValue<T>(String template)
//         {
//             return default(T);
//         }

//         private readonly Dictionary<String, Object> m_inputs = new Dictionary<String, Object>(StringComparer.OrdinalIgnoreCase);
//         private readonly Dictionary<String, TaskGroup> m_groups = new Dictionary<String, TaskGroup>(StringComparer.OrdinalIgnoreCase);
//     }
// }
