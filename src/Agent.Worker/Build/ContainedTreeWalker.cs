// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    internal static class ContainedTreeWalker
    {
        /// <summary>
        /// Lists all files under <paramref name="root"/> like <c>EnumerateFiles(..., AllDirectories)</c>, but runs
        /// <paramref name="validator"/> on every link entry (which throws when a link leaves the allowed directory).
        /// The root must already be validated: regular entries below it cannot escape, so they are not re-resolved.
        /// A directory link is validated before it is descended into.
        /// </summary>
        public static List<string> EnumerateFiles(string root, Func<string, string> validator)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = 0,
            };

            var enumerable = new FileSystemEnumerable<string>(root, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
            {
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                {
                    if (IsLink(ref entry))
                    {
                        validator(entry.ToFullPath());
                    }
                    return true;
                },
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    if (entry.IsDirectory)
                    {
                        return false;
                    }

                    if (IsLink(ref entry))
                    {
                        validator(entry.ToFullPath());
                    }
                    return true;
                },
            };

            return enumerable.ToList();
        }

        private static bool IsLink(ref FileSystemEntry entry)
        {
            return (entry.Attributes & FileAttributes.ReparsePoint) != 0;
        }
    }
}
