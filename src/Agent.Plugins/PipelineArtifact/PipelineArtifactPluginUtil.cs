using System;
using System.Collections.Generic;

namespace Agent.Plugins.PipelineArtifact
{
    public static class PipelineArtifactPathHelper
    {
        // This collection of invalid characters is based on the characters that are illegal in Windows/NTFS filenames.
        // Included are the 5 wildcard characters. Excluded are path separators (forward slash and backslash).
        private static readonly char[] ForbiddenPathChars =
                    new char[] {
                        (char) 0, (char) 1, (char) 2, (char) 3, (char) 4, (char) 5, (char) 6,
                        (char) 7, (char) 8, (char) 9, (char) 10, (char) 11, (char) 12, (char) 13,
                        (char) 14, (char) 15, (char) 16, (char) 17, (char) 18, (char) 19, (char) 20,
                        (char) 21, (char) 22, (char) 23, (char) 24, (char) 25, (char) 26, (char) 27,
                        (char) 28, (char) 29, (char) 30, (char) 31,
                        '"', ':', '<', '>', '|', '*', '?', '/', '\\' };
        private static readonly HashSet<Char> ForbiddenPAthCharsSet = new HashSet<Char>(ForbiddenPathChars);

        public static bool IsValidPath(string path){
            foreach(char c in path)
            {
                if(ForbiddenPAthCharsSet.Contains(c)) {
                    return false;
                }
            }
            return true;
        }
    }
}