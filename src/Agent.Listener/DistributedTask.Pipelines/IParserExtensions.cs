using System;
using System.Collections.Generic;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Microsoft.TeamFoundation.DistributedTask.Orchestration.Server.Pipelines
{
    internal static class IParserExtensions
    {
        /// <summary>
        /// Reads a mapping(string, string) from start to end using <c>StringComparer.OrdinalIgnoreCase</c>.
        /// </summary>
        /// <param name="parser">The parser instance from which to read</param>
        /// <returns>A dictionary instance with the specified comparer</returns>
        public static Dictionary<String, String> ReadMappingOfStringString(this IParser parser)
        {
            return parser.ReadMappingOfStringString(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a mapping(string, string) from start to end using the specified <c>StringComparer</c>.
        /// </summary>
        /// <param name="parser">The parser instance from which to read</param>
        /// <returns>A dictionary instance with the specified comparer</returns>
        public static Dictionary<String, String> ReadMappingOfStringString(
            this IParser parser,
            StringComparer comparer)
        {
            parser.Expect<MappingStart>();

            var mappingValue = new Dictionary<String, String>(comparer);

            while (!parser.Accept<MappingEnd>())
            {
                mappingValue.Add(parser.Expect<Scalar>().Value, parser.Expect<Scalar>().Value);
            }

            parser.Expect<MappingEnd>();
            return mappingValue;
        }

        public static Dictionary<String, Object> ReadMapping(this IParser parser)
        {
            parser.Expect<MappingStart>();
            var mapping = new Dictionary<String, Object>();
            while (!parser.Accept<MappingEnd>())
            {
                String key = parser.Expect<Scalar>().Value;
                Object value;
                if (parser.Accept<Scalar>())
                {
                    value = parser.Expect<Scalar>().Value;
                }
                else if (parser.Accept<SequenceStart>())
                {
                    value = parser.ReadSequence();
                }
                else
                {
                    value = parser.ReadMapping();
                }

                mapping.Add(key, value);
            }

            parser.Expect<MappingEnd>();
            return mapping;
        }

        public static List<Object> ReadSequence(this IParser parser)
        {
            parser.Expect<SequenceStart>();
            var sequence = new List<Object>();
            while (!parser.Accept<SequenceEnd>())
            {
                if (parser.Accept<Scalar>())
                {
                    sequence.Add(parser.Expect<Scalar>());
                }
                else if (parser.Accept<SequenceStart>())
                {
                    sequence.Add(parser.ReadSequence());
                }
                else
                {
                    sequence.Add(parser.ReadMapping());
                }
            }

            parser.Expect<SequenceEnd>();
            return sequence;
        }
    }
}
