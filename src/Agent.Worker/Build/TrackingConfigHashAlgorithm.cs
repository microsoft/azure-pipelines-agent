// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Cryptography", "CA5350: Do Not Use Weak Cryptographic Algorithms")]
    public class TrackingConfigHashAlgorithm
    {
        /// <summary>
        /// This method returns the hash key that combines repository hash keys.
        /// </summary>
        public static string ComputeHash(string collectionId, string definitionId, IList<RepositoryTrackingInfo> repositories)
        {
            // Validate parameters.
            ArgUtil.NotNull(collectionId, nameof(collectionId));
            ArgUtil.NotNull(definitionId, nameof(definitionId));
            ArgUtil.ListNotNullOrEmpty(repositories, nameof(repositories));
            ArgUtil.NotNull(repositories[0].RepositoryUrl, "repositoryUrl");

            string hashInput = null;
            if (repositories.Count == 0)
            {
                return null;
            }
            else if (repositories.Count == 1)
            {
                // For backwards compatibility, we need to maintain the old hash format for single repos
                hashInput = string.Format(
                    CultureInfo.InvariantCulture,
                    "{{{{ \r\n    \"system\" : \"build\", \r\n    \"collectionId\" = \"{0}\", \r\n    \"definitionId\" = \"{1}\", \r\n    \"repositoryUrl\" = \"{2}\", \r\n    \"sourceFolder\" = \"{{0}}\",\r\n    \"hashKey\" = \"{{1}}\"\r\n}}}}",
                    collectionId,
                    definitionId,
                    repositories[0].RepositoryUrl);
            }
            else
            {
                // For multiple repos, we use a similar format combining all the repo identifiers into one string.
                // Since you may want to clone the same repo into 2 different folders we need to include the id of the repo as well as the url.
                hashInput = string.Format(
                    CultureInfo.InvariantCulture,
                    "{{{{\"system\":\"build\",\"collectionId\"=\"{0}\",\"definitionId\"=\"{1}\",\"repositories\"=\"{2}\"}}}}",
                    collectionId,
                    definitionId,
                    string.Join(';', repositories.OrderBy(x => x.Identifier).Select(x => $"{x.Identifier}:{x.RepositoryUrl}")));
            }
            return CreateHash(hashInput);
        }

        private static string CreateHash(string hashInput)
        {
            //CodeQL [SM02196] Supress - Suppressing this warning as the hash is used only in the directory name and not for security purposes.
            using (SHA1 sha1Hash = SHA1.Create())
            {
                byte[] data = sha1Hash.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
                StringBuilder hexString = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    hexString.Append(data[i].ToString("x2"));
                }

                return hexString.ToString();
            }
        }
    }
}
