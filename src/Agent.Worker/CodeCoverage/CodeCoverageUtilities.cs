// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.CodeCoverage
{
    public static class CodeCoverageUtilities
    {
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public static void CopyFilesFromFileListWithDirStructure(List<string> files, ref string destinatonFilePath, List<string> skippedFiles = null)
        {
            string commonPath = null;
            if (files != null)
            {
                files.RemoveAll(q => q == null);

                if (files.Count > 1)
                {
                    files.Sort();
                    commonPath = SharedSubstring(files[0], files[files.Count - 1]);
                }

                var canonicalDest = Path.GetFullPath(destinatonFilePath + Path.DirectorySeparatorChar);

                foreach (var file in files)
                {
                    string newFile = null;

                    // FIX 1: Use Substring instead of Replace to safely remove only the prefix
                    if (!string.IsNullOrEmpty(commonPath) && file.StartsWith(commonPath, PathComparison))
                    {
                        newFile = file.Substring(commonPath.Length);
                    }
                    else
                    {
                        newFile = Path.GetFileName(file);
                    }

                    // FIX 2: Strip leading directory separators to prevent Path.Combine
                    // from treating newFile as an absolute path and ignoring destinatonFilePath
                    newFile = newFile.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    newFile = Path.Combine(destinatonFilePath, newFile);
                    var canonicalNewFile = Path.GetFullPath(newFile);

                    // FIX 3: Canonicalization boundary check - skip files that resolve outside destination
                    if (!canonicalNewFile.StartsWith(canonicalDest, PathComparison))
                    {
                        skippedFiles?.Add(file);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(canonicalNewFile));
                    File.Copy(file, canonicalNewFile, true);
                }
            }
        }

        public static XmlDocument ReadSummaryFile(IExecutionContext context, string summaryXmlLocation)
        {
            string xmlContents = "";

            //read xml contents
            if (!File.Exists(summaryXmlLocation))
            {
                throw new ArgumentException(StringUtil.Loc("FileDoesNotExist", summaryXmlLocation));
            }

            xmlContents = File.ReadAllText(summaryXmlLocation);


            if (string.IsNullOrWhiteSpace(xmlContents))
            {
                return null;
            }

            XmlDocument doc = new XmlDocument();
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore
                };

                using (XmlReader reader = XmlReader.Create(summaryXmlLocation, settings))
                {
                    doc.Load(reader);
                }
            }
            catch (XmlException ex)
            {
                context.Warning(StringUtil.Loc("FailedToReadFile", summaryXmlLocation, ex.Message));
                return null;
            }

            return doc;
        }

        public static int GetPriorityOrder(string coverageUnit)
        {
            if (!string.IsNullOrEmpty(coverageUnit))
            {
                switch (coverageUnit.ToLower())
                {
                    case "instruction":
                        return (int)Priority.Instruction;
                    case "line":
                        return (int)Priority.Line;
                    case "complexity":
                        return (int)Priority.Complexity;
                    case "class":
                        return (int)Priority.Class;
                    case "method":
                        return (int)Priority.Method;
                    default:
                        return (int)Priority.Other;
                }
            }

            return (int)Priority.Other;
        }

        public static string TrimNonEmptyParam(string parameterValue, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterValue))
            {
                throw new ArgumentException(StringUtil.Loc("ArgumentNeeded", parameterName));
            }
            return parameterValue.Trim();
        }

        public static string TrimToEmptyString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            return input.Trim();
        }

        private static string GetFilterSubString(string filter, int startIndex)
        {
            return filter.Substring(startIndex, filter.Length - startIndex);
        }

        private enum Priority
        {
            Class = 1,
            Complexity = 2,
            Method = 3,
            Line = 4,
            Instruction = 5,
            Other = 6
        }

        private static string SharedSubstring(string string1, string string2)
        {
            string ret = string.Empty;

            int index = 1;
            while (string1.Substring(0, index) == string2.Substring(0, index))
            {
                ret = string1.Substring(0, index);
                index++;
            }

            return ret;
        }
    }
}
