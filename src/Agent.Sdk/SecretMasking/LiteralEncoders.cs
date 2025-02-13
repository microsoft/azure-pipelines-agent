// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//using Microsoft.TeamFoundation.DistributedTask.Logging;
using Newtonsoft.Json;

using System;
using System.Text;

namespace Agent.Sdk.SecretMasking
{
    public static class LiteralEncoders
    {
        public static String JsonStringEscape(String value)
        {
            // Convert to a JSON string and then remove the leading/trailing double-quote.
            String jsonString = JsonConvert.ToString(value);
            String jsonEscapedValue = jsonString.Substring(startIndex: 1, length: jsonString.Length - 2);
            return jsonEscapedValue;
        }

        public static String BackslashEscape(String value)
        {
            return value.Replace(@"\\", @"\").Replace(@"\'", @"'").Replace(@"\""", @"""").Replace(@"\t", "\t");
        }

        public static String UriDataEscape(String value)
        {
            return UriDataEscape(value, 65519);
        }

        internal static String UriDataEscape(
            String value,
            Int32 maxSegmentSize)
        {
            if (value.Length <= maxSegmentSize)
            {
                return Uri.EscapeDataString(value);
            }

            // Workaround size limitation in Uri.EscapeDataString
            var result = new StringBuilder();
            var i = 0;
            do
            {
                var length = Math.Min(value.Length - i, maxSegmentSize);

                if (Char.IsHighSurrogate(value[i + length - 1]) && length > 1)
                {
                    length--;
                }

                result.Append(Uri.EscapeDataString(value.Substring(i, length)));
                i += length;
            }
            while (i < value.Length);

            return result.ToString();
        }
    }
}
