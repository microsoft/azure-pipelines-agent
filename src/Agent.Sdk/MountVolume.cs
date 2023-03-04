// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk
{
    public class MountVolume
    {
        public MountVolume()
        {

        }

        public MountVolume(string sourceVolumePath, string targetVolumePath, bool readOnly = false)
        {
            this.SourceVolumePath = sourceVolumePath;
            this.TargetVolumePath = targetVolumePath;
            this.ReadOnly = readOnly;
        }

        public MountVolume(string fromString)
        {
            ArgUtil.NotNull(fromString, nameof(fromString));
            ParseVolumeString(fromString);
        }

        private static Regex _autoEscapeWindowsDriveRegex = new Regex(@"(^|:)([a-zA-Z]):(\\|/)", RegexOptions.Compiled);
        private string AutoEscapeWindowsDriveInPath(string path)
        {
            return _autoEscapeWindowsDriveRegex.Replace(path, @"$1$2\:$3");
        }

        private void ParseVolumeString(string volume)
        {
            ReadOnly = false;
            SourceVolumePath = null;

            string readonlyToken = ":ro";
            if (volume.EndsWith(readonlyToken, System.StringComparison.OrdinalIgnoreCase))
            {
                ReadOnly = true;
                volume = volume.Remove(volume.Length - readonlyToken.Length);
            }
            // for completeness, in case someone explicitly added :rw in the volume mapping, we should strip it as well
            string readWriteToken = ":rw";
            if (volume.EndsWith(readWriteToken, System.StringComparison.OrdinalIgnoreCase))
            {
                ReadOnly = false;
                volume = volume.Remove(volume.Length - readWriteToken.Length);
            }

            // if volume starts with a ":", this is the same as having only a single path
            // so just strip it so we don't have to deal with an empty source volume path
            if (volume.StartsWith(":"))
            {
                volume = volume.Substring(1);
            }

            var volumes = new List<string>();
            // split by colon, but honor escaping of colons
            var volumeSplit = AutoEscapeWindowsDriveInPath(volume).Split(':');
            var appendNextIteration = false;
            foreach (var fragment in volumeSplit)
            {
                if (appendNextIteration)
                {
                    var orig = volumes[volumes.Count - 1];
                    orig = orig.Remove(orig.Length - 1); // remove the trailing backslash
                    volumes[volumes.Count - 1] = orig + ":" + fragment;
                    appendNextIteration = false;
                }
                else
                {
                    volumes.Add(fragment);
                }
                // if this fragment ends with backslash, then the : was escaped
                if (fragment.EndsWith(@"\"))
                {
                    appendNextIteration = true;
                }
            }

            if (volumes.Count >= 2)
            {
                // source:target
                SourceVolumePath = volumes[0];
                TargetVolumePath = volumes[1];
                // if volumes.Count > 2 here, we should log something that says we ignored options passed in.
                // for now, do nothing in order to remain backwards compatable.
            }
            else
            {
                // target - or, default to passing straight through
                TargetVolumePath = volume;
            }
        }

        public string SourceVolumePath { get; set; }
        public string TargetVolumePath { get; set; }
        public bool ReadOnly { get; set; }
    }
}