// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Sdk.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class ArgUtil
    {
        public static IArgUtilInstanced ArgUtilInstance = new ArgUtilInstanced();
        public static void Directory([ValidatedNotNull] string directory, string name)
        {
            ArgUtil.ArgUtilInstance.Directory(directory, name);
        }

        public static void Equal<T>(T expected, T actual, string name)
        {
            ArgUtil.ArgUtilInstance.Equal(expected, actual, name);
        }

        public static void File(string fileName, string name)
        {
            ArgUtil.ArgUtilInstance.File(fileName, name);
        }

        public static void NotNull([ValidatedNotNull] object value, string name)
        {
            ArgUtil.ArgUtilInstance.NotNull(value, name);
        }

        public static void NotNullOrEmpty([ValidatedNotNull] string value, string name)
        {
            ArgUtil.ArgUtilInstance.NotNullOrEmpty(value, name);
        }

        public static void ThrowIfContainsNull(string name, string value)
        {
            // A NUL (U+0000) is the entry separator in the Windows native environment block and the
            // C-string terminator in POSIX envp. It is never valid in an environment variable name or
            // value on any OS; allowing it lets one approved variable split into two native variables
            // (environment variable injection).
            string field = (name != null && name.IndexOf('\0') >= 0) ? "name"
                         : (value != null && value.IndexOf('\0') >= 0) ? "value"
                         : null;
            if (field != null)
            {
                throw new ArgumentException(StringUtil.Loc("EnvironmentVariableContainsNullCharacter", name ?? string.Empty, field));
            }
        }

        public static void ListNotNullOrEmpty<T>([ValidatedNotNull] IEnumerable<T> value, string name)
        {
            ArgUtil.ArgUtilInstance.ListNotNullOrEmpty(value, name);
        }

        public static void NotEmpty(Guid value, string name)
        {
            ArgUtil.ArgUtilInstance.NotEmpty(value, name);
        }

        public static void Null(object value, string name)
        {
            ArgUtil.ArgUtilInstance.Null(value, name);
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    sealed class ValidatedNotNullAttribute : Attribute
    {
    }
}