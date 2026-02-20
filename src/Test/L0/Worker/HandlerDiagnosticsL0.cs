// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Worker;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Worker
{
    public sealed class HandlerDiagnosticsL0 : IDisposable
    {
        private string _testDirectory;

        private string CreateTestTaskDirectory(string taskJsonContent, [CallerMemberName] string testName = "")
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"HandlerDiagnosticsTest_{testName}_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            File.WriteAllText(Path.Combine(_testDirectory, "task.json"), taskJsonContent);
            return _testDirectory;
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(_testDirectory) && Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        #region Operating System Incompatibility Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        [Trait("SkipOn", "windows")] // This test only makes sense on non-Windows
        public void Analyze_WindowsOnlyHandlers_OnNonWindows_ReturnsOSIncompatible()
        {
            // Arrange - Task with only Windows handlers (PowerShell3, Process)
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""TestTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""PowerShell3"": {
                        ""target"": ""script.ps1""
                    },
                    ""Process"": {
                        ""target"": ""tool.exe"",
                        ""argumentFormat"": """"
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // Create empty ExecutionData (simulating Linux where Windows handlers are filtered out)
            var loadedExecution = new ExecutionData();
            // On Linux, PowerShell3 and Process would not be added to All

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "TestTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.OperatingSystemIncompatible, result.Reason);
            Assert.Equal(2, result.DeclaredHandlers.Count);
            Assert.Contains(result.DeclaredHandlers, h => h.HandlerName == "PowerShell3" && h.IsWindowsOnly);
            Assert.Contains(result.DeclaredHandlers, h => h.HandlerName == "Process" && h.IsWindowsOnly);
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            Assert.Contains("Windows", result.SupportedOperatingSystems);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_WindowsOnlyHandlers_CorrectlyIdentifiesWindowsOnlyFlag()
        {
            // Arrange - Task with only Windows handlers (PowerShell3, Process)
            // This test verifies that handlers are correctly marked as Windows-only
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""TestTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""PowerShell3"": {
                        ""target"": ""script.ps1""
                    },
                    ""Process"": {
                        ""target"": ""tool.exe"",
                        ""argumentFormat"": """"
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // Create empty ExecutionData (no handlers loaded)
            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "TestTask",
                "1.0.0");

            // Assert - Verify that the handlers are correctly identified as Windows-only
            Assert.Equal(2, result.DeclaredHandlers.Count);
            Assert.All(result.DeclaredHandlers, h => Assert.True(h.IsWindowsOnly, $"Handler {h.HandlerName} should be marked as Windows-only"));
        }

        #endregion

        #region Deprecated Handlers Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_OnlyDeprecatedNodeHandlers_ReturnsOnlyDeprecatedHandlers()
        {
            // Arrange - Task with only deprecated Node handlers (Node, Node10)
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""LegacyTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node"": {
                        ""target"": ""index.js""
                    },
                    ""Node10"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // Create ExecutionData with the deprecated handlers loaded
            var loadedExecution = new ExecutionData();
            loadedExecution.Node = new NodeHandlerData { Target = "index.js" };
            loadedExecution.Node10 = new Node10HandlerData { Target = "index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "LegacyTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.OnlyDeprecatedHandlers, result.Reason);
            Assert.Equal(2, result.DeclaredHandlers.Count);
            Assert.All(result.DeclaredHandlers, h => Assert.True(h.IsDeprecated));
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            // Just verify the result properties are correct
            Assert.Equal("LegacyTask", result.TaskName);
            Assert.Equal("1.0.0", result.TaskVersion);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_OnlyNode10Handler_ReturnsOnlyDeprecatedHandlers()
        {
            // Arrange - Task with only Node10 handler
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""Node10OnlyTask"",
                ""version"": { ""Major"": 2, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node10"": {
                        ""target"": ""dist/index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();
            loadedExecution.Node10 = new Node10HandlerData { Target = "dist/index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "Node10OnlyTask",
                "2.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.OnlyDeprecatedHandlers, result.Reason);
            Assert.Single(result.DeclaredHandlers);
            Assert.True(result.DeclaredHandlers[0].IsDeprecated);
            Assert.Contains("Node10", result.DeclaredHandlers[0].HandlerName);
        }

        #endregion

        #region Unknown/Newer Handlers Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_UnknownFutureHandler_ReturnsHandlersNewerThanAgent()
        {
            // Arrange - Task with a future Node version not recognized by this agent
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""FutureTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node26"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // ExecutionData would not load unknown handlers
            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "FutureTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
            Assert.Single(result.DeclaredHandlers);
            Assert.True(result.DeclaredHandlers[0].IsUnknownToAgent);
            Assert.Contains("Node26", result.DeclaredHandlers[0].HandlerName);
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            // Just verify the result properties are correct
            Assert.Equal("FutureTask", result.TaskName);
            Assert.Equal("1.0.0", result.TaskVersion);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_MultipleUnknownHandlers_ReturnsHandlersNewerThanAgent()
        {
            // Arrange - Task with multiple future Node versions
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""VeryFutureTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node26"": {
                        ""target"": ""index.js""
                    },
                    ""Node28"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "VeryFutureTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
            Assert.Equal(2, result.DeclaredHandlers.Count);
            Assert.All(result.DeclaredHandlers, h => Assert.True(h.IsUnknownToAgent));
        }

        #endregion

        #region No Execution Section Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_NoExecutionSection_ReturnsNoExecutionSection()
        {
            // Arrange - Task with no execution section at all
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""EmptyTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""inputs"": []
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "EmptyTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.NoExecutionSection, result.Reason);
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            Assert.Equal("EmptyTask", result.TaskName);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_EmptyExecutionSection_ReturnsNoExecutionSection()
        {
            // Arrange - Task with empty execution section
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""EmptyExecutionTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {}
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "EmptyExecutionTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.NoExecutionSection, result.Reason);
        }

        #endregion

        #region Pre/Post Execution Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_PreJobExecution_OnlyDeprecatedHandlers_ReturnsOnlyDeprecatedHandlers()
        {
            // Arrange - Task with deprecated handlers only in prejobexecution
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""PreJobTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20_1"": {
                        ""target"": ""main.js""
                    }
                },
                ""prejobexecution"": {
                    ""Node10"": {
                        ""target"": ""prejob.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // For PreJob stage, only prejobexecution handlers would be loaded
            var loadedPreJobExecution = new ExecutionData();
            loadedPreJobExecution.Node10 = new Node10HandlerData { Target = "prejob.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedPreJobExecution,
                JobRunStage.PreJob,
                "PreJobTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.OnlyDeprecatedHandlers, result.Reason);
            Assert.Single(result.DeclaredHandlers);
            Assert.Equal("Node10", result.DeclaredHandlers[0].HandlerName);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_PostJobExecution_UnknownHandler_ReturnsHandlersNewerThanAgent()
        {
            // Arrange - Task with unknown handler only in postjobexecution
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""PostJobTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20_1"": {
                        ""target"": ""main.js""
                    }
                },
                ""postjobexecution"": {
                    ""Node26"": {
                        ""target"": ""cleanup.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // For PostJob stage, only postjobexecution handlers would be loaded
            var loadedPostJobExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedPostJobExecution,
                JobRunStage.PostJob,
                "PostJobTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
            Assert.Single(result.DeclaredHandlers);
            Assert.Equal("Node26", result.DeclaredHandlers[0].HandlerName);
            Assert.True(result.DeclaredHandlers[0].IsUnknownToAgent);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_PreJobExecution_NoPreJobSection_ReturnsNoExecutionSection()
        {
            // Arrange - Task without prejobexecution section
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""MainOnlyTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20_1"": {
                        ""target"": ""main.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.PreJob,
                "MainOnlyTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.NoExecutionSection, result.Reason);
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            Assert.Equal("MainOnlyTask", result.TaskName);
            Assert.Equal(JobRunStage.PreJob, result.Stage);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_PostJobExecution_NoPostJobSection_ReturnsNoExecutionSection()
        {
            // Arrange - Task without postjobexecution section
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""MainOnlyTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20_1"": {
                        ""target"": ""main.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.PostJob,
                "MainOnlyTask",
                "1.0.0");

            // Assert
            Assert.Equal(HandlerIncompatibilityReason.NoExecutionSection, result.Reason);
            // Error message is generated using StringUtil.Loc which is not available in unit tests
            Assert.Equal("MainOnlyTask", result.TaskName);
            Assert.Equal(JobRunStage.PostJob, result.Stage);
        }

        #endregion

        #region Handler Loading Comparison Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_LoadedHandlersTracked_ShowsCorrectLoadedHandlers()
        {
            // Arrange - Task with multiple handlers, some loaded
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""MixedTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20_1"": {
                        ""target"": ""index.js""
                    },
                    ""Node24"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // Simulate that both handlers were loaded
            var loadedExecution = new ExecutionData();
            loadedExecution.Node20_1 = new Node20_1HandlerData { Target = "index.js" };
            loadedExecution.Node24 = new Node24HandlerData { Target = "index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "MixedTask",
                "1.0.0");

            // Assert - Both handlers should be marked as loaded
            Assert.Equal(2, result.LoadedHandlers.Count);
            Assert.Contains("Node20_1", result.LoadedHandlers);
            Assert.Contains("Node24", result.LoadedHandlers);
            Assert.All(result.DeclaredHandlers, h => Assert.True(h.IsLoadedByAgent));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_Node20Normalization_HandlesNode20ToNode20_1Mapping()
        {
            // Arrange - Task uses "Node20" but agent uses "Node20_1"
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""Node20Task"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node20"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            // Agent loads Node20 as Node20_1
            var loadedExecution = new ExecutionData();
            loadedExecution.Node20_1 = new Node20_1HandlerData { Target = "index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "Node20Task",
                "1.0.0");

            // Assert - Should recognize Node20 as loaded (mapped to Node20_1)
            Assert.Single(result.DeclaredHandlers);
            Assert.True(result.DeclaredHandlers[0].IsLoadedByAgent);
            Assert.Single(result.LoadedHandlers);
        }

        #endregion

        #region Error Message Content Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_DeprecatedHandlers_ResultContainsAllRequiredInfo()
        {
            // Arrange
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""OldTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();
            loadedExecution.Node = new NodeHandlerData { Target = "index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "OldTask",
                "1.0.0");

            // Assert - Verify result properties contain helpful information
            Assert.Equal(HandlerIncompatibilityReason.OnlyDeprecatedHandlers, result.Reason);
            Assert.Equal("OldTask", result.TaskName);
            Assert.Equal("1.0.0", result.TaskVersion);
            Assert.Single(result.DeclaredHandlers);
            Assert.True(result.DeclaredHandlers[0].IsDeprecated);
            Assert.Equal("Node", result.DeclaredHandlers[0].HandlerName);
            Assert.NotEmpty(result.SupportedHandlersByAgent);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_NewerHandlers_ResultContainsAgentUpdateInfo()
        {
            // Arrange
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""NewTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node30"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "NewTask",
                "1.0.0");

            // Assert - Verify result properties contain agent update information
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
            Assert.Equal("NewTask", result.TaskName);
            Assert.Equal("1.0.0", result.TaskVersion);
            Assert.Single(result.DeclaredHandlers);
            Assert.True(result.DeclaredHandlers[0].IsUnknownToAgent);
            Assert.Equal("Node30", result.DeclaredHandlers[0].HandlerName);
            Assert.NotEmpty(result.SupportedHandlersByAgent);
        }

        #endregion

        #region Case Insensitivity Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_ExecutionSectionCaseInsensitive_FindsSection()
        {
            // Arrange - Task with different casing for execution section
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""CaseTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""Execution"": {
                    ""Node20_1"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);

            var loadedExecution = new ExecutionData();
            loadedExecution.Node20_1 = new Node20_1HandlerData { Target = "index.js" };

            // Act
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "CaseTask",
                "1.0.0");

            // Assert - Should find the execution section despite different casing
            Assert.Single(result.DeclaredHandlers);
            Assert.NotEqual(HandlerIncompatibilityReason.NoExecutionSection, result.Reason);
        }

        #endregion

        #region Self-Hosted vs Microsoft-Hosted Tests

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_SelfHostedAgent_IsSelfHostedPropertySetTrue()
        {
            // Arrange - Task with unknown handler
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""FutureTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node26"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);
            var loadedExecution = new ExecutionData();

            // Act - Explicitly pass isSelfHosted = true
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "FutureTask",
                "1.0.0",
                isSelfHosted: true);

            // Assert
            Assert.True(result.IsSelfHosted);
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_MicrosoftHostedAgent_IsSelfHostedPropertySetFalse()
        {
            // Arrange - Task with unknown handler
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""FutureTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node26"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);
            var loadedExecution = new ExecutionData();

            // Act - Explicitly pass isSelfHosted = false (Microsoft-hosted)
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "FutureTask",
                "1.0.0",
                isSelfHosted: false);

            // Assert
            Assert.False(result.IsSelfHosted);
            Assert.Equal(HandlerIncompatibilityReason.HandlersNewerThanAgent, result.Reason);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Worker")]
        public void Analyze_DefaultIsSelfHostedTrue_WhenNotSpecified()
        {
            // Arrange - Task with unknown handler
            var taskJson = @"{
                ""id"": ""test-task"",
                ""name"": ""FutureTask"",
                ""version"": { ""Major"": 1, ""Minor"": 0, ""Patch"": 0 },
                ""execution"": {
                    ""Node26"": {
                        ""target"": ""index.js""
                    }
                }
            }";
            var taskDir = CreateTestTaskDirectory(taskJson);
            var loadedExecution = new ExecutionData();

            // Act - Don't pass isSelfHosted parameter (should default to true)
            var result = HandlerDiagnostics.Analyze(
                taskDir,
                loadedExecution,
                JobRunStage.Main,
                "FutureTask",
                "1.0.0");

            // Assert - Default should be true (most common case)
            Assert.True(result.IsSelfHosted);
        }

        #endregion
    }
}
