// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Microsoft.VisualStudio.Services.Agent.Worker.CodeCoverage;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker.CodeCoverage
{
    public class CodeCoverageUtilitiesTests
    {
        private Mock<IExecutionContext> _ec;
        private List<string> _warnings = new List<string>();
        private List<string> _errors = new List<string>();
        private List<string> _outputMessages = new List<string>();

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void GetPriorityOrderTest()
        {
            Assert.Equal(1, CodeCoverageUtilities.GetPriorityOrder("cLaSs"));
            Assert.Equal(2, CodeCoverageUtilities.GetPriorityOrder("ComplexiTy"));
            Assert.Equal(3, CodeCoverageUtilities.GetPriorityOrder("MEthoD"));
            Assert.Equal(4, CodeCoverageUtilities.GetPriorityOrder("line"));
            Assert.Equal(5, CodeCoverageUtilities.GetPriorityOrder("InstruCtion"));
            Assert.Equal(6, CodeCoverageUtilities.GetPriorityOrder("invalid"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesWithDirectoryStructureWhenInputIsNull()
        {
            string destinationFilePath = string.Empty;
            CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(null, ref destinationFilePath);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesWithDirectoryStructureWhenFilesWithSameNamesAreGiven()
        {
            List<string> files = GetAdditionalCodeCoverageFilesWithSameFileName();
            string destinationFilePath = Path.Combine(Path.GetTempPath(), "additional");
            try
            {
                Directory.CreateDirectory(destinationFilePath);
                CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(files, ref destinationFilePath);
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "A/a.xml")));
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "B/a.xml")));
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "C/b.xml")));
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "a.xml")));
            }
            finally
            {
                Directory.Delete(destinationFilePath, true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), "A"), true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), "B"), true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), "C"), true);
                File.Delete(Path.Combine(Path.GetTempPath(), "a.xml"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesWithDirectoryStructureWhenFilesWithDifferentNamesAreGiven()
        {
            List<string> files = GetAdditionalCodeCoverageFilesWithDifferentFileNames();
            string destinationFilePath = Path.Combine(Path.GetTempPath(), "additional");
            try
            {
                Directory.CreateDirectory(destinationFilePath);
                CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(files, ref destinationFilePath);
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "a.xml")));
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "b.xml")));
            }
            finally
            {
                Directory.Delete(destinationFilePath, true);
                Directory.Delete(Path.Combine(Path.GetTempPath(), "A"), true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "EnableCodeCoverage")]
        public void ThrowsIfParameterNull()
        {
            Assert.Throws<ArgumentException>(() => CodeCoverageUtilities.TrimNonEmptyParam(null, "inputName"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "EnableCodeCoverage")]
        public void ThrowsIfParameterIsWhiteSpace()
        {
            Assert.Throws<ArgumentException>(() => CodeCoverageUtilities.TrimNonEmptyParam("       ", "inputName"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesSkipsPathTraversalFiles()
        {
            string destinationFilePath = Path.Combine(Path.GetTempPath(), "cc_dest_traversal1");
            // Nest srcDir 3 levels deep so ../../ traversal stays within temp directory
            var srcDir = Path.Combine(Path.GetTempPath(), "cc_traversal_src", "level1", "level2");
            var srcRoot = Path.Combine(Path.GetTempPath(), "cc_traversal_src");
            try
            {
                Directory.CreateDirectory(destinationFilePath);
                var legitimateFile = Path.Combine(srcDir, "legit.xml");
                Directory.CreateDirectory(srcDir);
                File.WriteAllText(legitimateFile, "Test");

                // Craft a path that traverses above srcDir.
                // srcDir/../../evil.xml resolves to cc_traversal_src/evil.xml (within temp)
                var maliciousFile = Path.Combine(srcDir, "..", "..", "evil.xml");
                var resolvedMalicious = Path.GetFullPath(maliciousFile);
                File.WriteAllText(resolvedMalicious, "Malicious");

                var files = new List<string> { legitimateFile, maliciousFile };
                var skippedFiles = new List<string>();

                CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(files, ref destinationFilePath, skippedFiles);

                // Malicious file should be skipped, not copied
                Assert.True(skippedFiles.Count > 0, "Expected at least one file to be skipped due to path traversal");
                Assert.Contains(maliciousFile, skippedFiles);
            }
            finally
            {
                if (Directory.Exists(destinationFilePath)) Directory.Delete(destinationFilePath, true);
                if (Directory.Exists(srcRoot)) Directory.Delete(srcRoot, true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesHandlesAbsolutePathInjection()
        {
            // Verifies that the Substring+TrimStart fix prevents the Replace trick attack.
            // Attack: craft paths so SharedSubstring = "srcDir\pwn", then old Replace
            // on "srcDir\pwn\<separator><path>" removes prefix, leaving "\<path>"
            // which Path.Combine treats as absolute (ignoring destination).
            // Fix: Substring removes only prefix, TrimStart strips leading separators.
            string destinationFilePath = Path.Combine(Path.GetTempPath(), "cc_dest_abs");
            var srcDir = Path.Combine(Path.GetTempPath(), "cc_abs_src");
            try
            {
                Directory.CreateDirectory(destinationFilePath);

                // Create two files under srcDir that share prefix "srcDir\pwn"
                // File 1: srcDir\pwn\sub\evil.txt
                // File 2: srcDir\pwnX
                var subDir = Path.Combine(srcDir, "pwn", "sub");
                Directory.CreateDirectory(subDir);
                var payloadFile = Path.Combine(subDir, "evil.txt");
                File.WriteAllText(payloadFile, "Malicious");

                var dummyFile = Path.Combine(srcDir, "pwnX");
                File.WriteAllText(dummyFile, "Dummy");

                var files = new List<string> { payloadFile, dummyFile };
                var skippedFiles = new List<string>();

                CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(files, ref destinationFilePath, skippedFiles);

                // With old Replace("srcDir\pwn",""), payloadFile becomes "\sub\evil.txt"
                // Path.Combine(dest, "\sub\evil.txt") → "\sub\evil.txt" (absolute, escapes!)
                // With fix: Substring → "\sub\evil.txt", TrimStart → "sub\evil.txt" (relative, safe)
                
                // Verify: file must NOT exist at root \sub\evil.txt
                var rootEscape = Path.Combine(Path.GetPathRoot(destinationFilePath), "sub", "evil.txt");
                Assert.False(File.Exists(rootEscape),
                    "File must not escape to drive root via leading separator injection");

                // Verify: file should land safely inside destination
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "sub", "evil.txt")),
                    "File should be safely nested inside destination directory");

                Assert.True(skippedFiles.Count == 0, "Legitimate file should not be skipped");
            }
            finally
            {
                if (Directory.Exists(destinationFilePath)) Directory.Delete(destinationFilePath, true);
                if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "PublishCodeCoverage")]
        public void CopyFilesSucceedsWithLegitimateFiles()
        {
            string destinationFilePath = Path.Combine(Path.GetTempPath(), "cc_dest_legit");
            var srcDir = Path.Combine(Path.GetTempPath(), "cc_legit_src");
            try
            {
                Directory.CreateDirectory(destinationFilePath);
                var file1 = Path.Combine(srcDir, "sub1", "a.xml");
                var file2 = Path.Combine(srcDir, "sub2", "b.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(file1));
                Directory.CreateDirectory(Path.GetDirectoryName(file2));
                File.WriteAllText(file1, "Content1");
                File.WriteAllText(file2, "Content2");

                var files = new List<string> { file1, file2 };
                CodeCoverageUtilities.CopyFilesFromFileListWithDirStructure(files, ref destinationFilePath);

                Assert.True(File.Exists(Path.Combine(destinationFilePath, "1", "a.xml")));
                Assert.True(File.Exists(Path.Combine(destinationFilePath, "2", "b.xml")));
            }
            finally
            {
                if (Directory.Exists(destinationFilePath)) Directory.Delete(destinationFilePath, true);
                if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
            }
        }

        private void SetupMocks()
        {
            _ec = new Mock<IExecutionContext>();
            _ec.Setup(x => x.Write(It.IsAny<string>(), It.IsAny<string>(), true))
                .Callback<string, string, bool>
                ((tag, message, canMaskSecrets) =>
                {
                    _outputMessages.Add(message);
                });

            _ec.Setup(x => x.AddIssue(It.IsAny<Issue>()))
            .Callback<Issue>
            ((issue) =>
            {
                if (issue.Type == IssueType.Warning)
                {
                    _warnings.Add(issue.Message);
                }
                else if (issue.Type == IssueType.Error)
                {
                    _errors.Add(issue.Message);
                }
            });
        }

        private List<string> GetAdditionalCodeCoverageFilesWithSameFileName()
        {
            var files = new List<string>();
            files.Add(Path.Combine(Path.GetTempPath(), "A/a.xml"));
            files.Add(Path.Combine(Path.GetTempPath(), "B/a.xml"));
            files.Add(Path.Combine(Path.GetTempPath(), "C/b.xml"));
            files.Add(Path.Combine(Path.GetTempPath(), "a.xml"));
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "Test");
            }
            return files;
        }

        private List<string> GetAdditionalCodeCoverageFilesWithDifferentFileNames()
        {
            var files = new List<string>();
            files.Add(Path.Combine(Path.GetTempPath(), "A/a.xml"));
            files.Add(Path.Combine(Path.GetTempPath(), "A/b.xml"));
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "Test");
            }
            return files;
        }
    }
}
