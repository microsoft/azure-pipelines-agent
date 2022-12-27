// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;

namespace Microsoft.VisualStudio.Services.Agent.Blob
{
    public class BlobFileInfo
    {
        public DedupNode Node { get; set; }
        public string Path { get; set; }
        public DedupIdentifier DedupId
        {
            get
            {
                return Node.GetDedupIdentifier(HashType.Dedup64K);
            }
        }
        public bool Success { get; set; }
    }
}