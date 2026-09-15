// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public sealed class ServicingPackageTemplatesL0
    {
        [Theory]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        [InlineData("InstallAgentPackage.template.xml")]
        [InlineData("UpdateAgentPackage.template.xml")]
        public void PackageNamesMatchTypeAndPlatform(string templateName)
        {
            const string testVersion = "0.0.0";
            XElement[] packages = ReadPackages(templateName, testVersion);

            Assert.NotEmpty(packages);

            foreach (XElement package in packages)
            {
                string packageType = package.Attribute("packageType").Value;
                string platform = package.Attribute("platform").Value;
                string filename = package.Attribute("filename").Value;
                string downloadUrl = package.Attribute("downloadUrl").Value;
                string filenamePrefix = packageType == "agent" ? "vsts-agent" : packageType;
                string extension = platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
                string expectedFilename = $"{filenamePrefix}-{platform}-{testVersion}{extension}";

                Assert.Equal(expectedFilename, filename);
                Assert.True(
                    downloadUrl.EndsWith($"/{expectedFilename}", StringComparison.Ordinal),
                    $"Download URL '{downloadUrl}' does not match package type '{packageType}' and platform '{platform}'.");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void UpdateTemplateContainsEveryInstallPackage()
        {
            var installPackages = ReadPackages("InstallAgentPackage.template.xml")
                .Select(GetPackageIdentity)
                .OrderBy(package => package)
                .ToArray();
            var updatePackages = ReadPackages("UpdateAgentPackage.template.xml")
                .Select(GetPackageIdentity)
                .OrderBy(package => package)
                .ToArray();

            Assert.Equal(installPackages, updatePackages);
        }

        private static XElement[] ReadPackages(string templateName, string testVersion = "0.0.0")
        {
            string templatePath = Path.Combine(TestUtil.GetSrcPath(), "Misc", templateName);
            string template = File.ReadAllText(templatePath)
                .Replace("<AGENT_VERSION>", testVersion)
                .Replace("<HASH_VALUE>", "hash");

            return XDocument.Parse(template)
                .Descendants("AddTaskPackageData")
                .ToArray();
        }

        private static string GetPackageIdentity(XElement package)
        {
            return $"{package.Attribute("packageType").Value}/{package.Attribute("platform").Value}";
        }
    }
}
