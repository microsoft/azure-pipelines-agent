// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.VisualStudio.Services.Agent.Worker.NodeVersionStrategies
{
    /// <summary>
    /// Contains glibc compatibility information for different Node.js versions.
    /// </summary>
    public class GlibcCompatibilityInfo
    {
        /// <summary>
        /// True if Node24 has glibc compatibility errors (requires glibc 2.28+).
        /// </summary>
        public bool Node24HasGlibcError { get; set; }

        /// <summary>
        /// True if Node20 has glibc compatibility errors (requires glibc 2.17+).
        /// </summary>
        public bool Node20HasGlibcError { get; set; }

        /// <summary>
        /// Creates a new instance with no glibc errors (compatible system).
        /// </summary>
        public static GlibcCompatibilityInfo Compatible => new GlibcCompatibilityInfo
        {
            Node24HasGlibcError = false,
            Node20HasGlibcError = false
        };

        /// <summary>
        /// Creates a new instance with specific compatibility information.
        /// </summary>
        public static GlibcCompatibilityInfo Create(bool node24HasGlibcError, bool node20HasGlibcError) =>
            new GlibcCompatibilityInfo
            {
                Node24HasGlibcError = node24HasGlibcError,
                Node20HasGlibcError = node20HasGlibcError
            };
    }
}