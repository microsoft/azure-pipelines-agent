// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Listener.Configuration;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class MessageListenerL0 : IDisposable
    {
        private AgentSettings _settings;
        private Mock<IConfigurationManager> _config;
        private Mock<IAgentServer> _agentServer;
        private Mock<ICredentialManager> _credMgr;
        private Mock<ICapabilitiesManager> _capabilitiesManager;
        private Mock<IFeatureFlagProvider> _featureFlagProvider;
        private Mock<IRSAKeyManager> _rsaKeyManager;
        private readonly RSACryptoServiceProvider rsa;

        public MessageListenerL0()
        {
            _settings = new AgentSettings { AgentId = 1, AgentName = "myagent", PoolId = 123, PoolName = "default", ServerUrl = "http://myserver", WorkFolder = "_work" };
            _config = new Mock<IConfigurationManager>();
            _config.Setup(x => x.LoadSettings()).Returns(_settings);
            _agentServer = new Mock<IAgentServer>();
            _credMgr = new Mock<ICredentialManager>();
            _capabilitiesManager = new Mock<ICapabilitiesManager>();
            _featureFlagProvider = new Mock<IFeatureFlagProvider>();
            _rsaKeyManager = new Mock<IRSAKeyManager>();

            _featureFlagProvider.Setup(x => x.GetFeatureFlagAsync(It.IsAny<IHostContext>(), It.IsAny<string>(), It.IsAny<ITraceWriter>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(new FeatureAvailability.FeatureFlag("", "", "", "Off", "Off")));
            _featureFlagProvider.Setup(x => x.GetFeatureFlagWithCred(It.IsAny<IHostContext>(), It.IsAny<string>(), It.IsAny<ITraceWriter>(), It.IsAny<AgentSettings>(), It.IsAny<VssCredentials>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(new FeatureAvailability.FeatureFlag("", "", "", "Off", "Off")));

            rsa = new RSACryptoServiceProvider(2048);
            _rsaKeyManager.Setup(x => x.CreateKey(It.IsAny<bool>(), It.IsAny<bool>())).Returns(rsa);
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            TestHostContext tc = new TestHostContext(this, testName);
            tc.SetSingleton<IConfigurationManager>(_config.Object);
            tc.SetSingleton<IAgentServer>(_agentServer.Object);
            tc.SetSingleton<ICredentialManager>(_credMgr.Object);
            tc.SetSingleton<ICapabilitiesManager>(_capabilitiesManager.Object);
            tc.SetSingleton<IFeatureFlagProvider>(_featureFlagProvider.Object);
            tc.SetSingleton<IRSAKeyManager>(_rsaKeyManager.Object);
            return tc;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void CreatesSession()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                trace.Info($"result: {result}");

                // Assert.
                Assert.True(result);
                _agentServer
                    .Verify(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void DeleteSession()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                PropertyInfo sessionIdProperty = expectedSession.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(sessionIdProperty);
                sessionIdProperty.SetValue(expectedSession, Guid.NewGuid());

                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                Assert.True(result);

                _agentServer
                    .Setup(x => x.DeleteAgentSessionAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                await listener.DeleteSessionAsync();

                //Assert
                _agentServer
                    .Verify(x => x.DeleteAgentSessionAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<CancellationToken>()), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void GetNextMessage()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                PropertyInfo sessionIdProperty = expectedSession.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(sessionIdProperty);
                sessionIdProperty.SetValue(expectedSession, Guid.NewGuid());

                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                Assert.True(result);

                var arMessages = new TaskAgentMessage[]
                {
                        new TaskAgentMessage
                        {
                            Body = "somebody1",
                            MessageId = 4234,
                            MessageType = JobRequestMessageTypes.AgentJobRequest
                        },
                        new TaskAgentMessage
                        {
                            Body = "somebody2",
                            MessageId = 4235,
                            MessageType = JobCancelMessage.MessageType
                        },
                        null,  //should be skipped by GetNextMessageAsync implementation
                        null,
                        new TaskAgentMessage
                        {
                            Body = "somebody3",
                            MessageId = 4236,
                            MessageType = JobRequestMessageTypes.AgentJobRequest
                        }
                };
                var messages = new Queue<TaskAgentMessage>(arMessages);

                _agentServer
                    .Setup(x => x.GetAgentMessageAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<long?>(), tokenSource.Token))
                    .Returns(async (Int32 poolId, Guid sessionId, Int64? lastMessageId, CancellationToken cancellationToken) =>
                    {
                        await Task.Yield();
                        return messages.Dequeue();
                    });
                TaskAgentMessage message1 = await listener.GetNextMessageAsync(tokenSource.Token);
                TaskAgentMessage message2 = await listener.GetNextMessageAsync(tokenSource.Token);
                TaskAgentMessage message3 = await listener.GetNextMessageAsync(tokenSource.Token);
                Assert.Equal(arMessages[0], message1);
                Assert.Equal(arMessages[1], message2);
                Assert.Equal(arMessages[4], message3);

                //Assert
                _agentServer
                    .Verify(x => x.GetAgentMessageAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<long?>(), tokenSource.Token), Times.Exactly(arMessages.Length));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void CreateSessionUsesExponentialBackoffWhenFlagEnabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Set environment variable (simulating Agent.cs setting it after fetching FF)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "true");
                try
                {
                    int callCount = 0;

                    _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                    .Returns(() =>
                    {
                        callCount++;
                        // Fail first 5 attempts to check delay at attempt 5
                        if (callCount <= 5)
                        {
                            throw new Exception("Temporary failure");
                        }
                        return Task.FromResult(new TaskAgentSession());
                    });

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);

                    bool result = await listener.CreateSessionAsync(tokenSource.Token);
                    trace.Info($"result: {result}");

                    // Assert
                    Assert.True(result);
                    Assert.True(tc.CapturedDelays.Count >= 5, $"Should have at least 5 delays, got {tc.CapturedDelays.Count}");

                    // Check the 5th delay (index 4)
                    var delayAtAttempt5 = tc.CapturedDelays[4].TotalSeconds;
                    trace.Info($"Delay at attempt 5: {delayAtAttempt5:F1}s (expected >30s for exponential backoff)");

                    // Exponential should be > 30s (constant is 30s)
                    Assert.True(delayAtAttempt5 > 30,
                        $"Expected exponential (>30s), got {delayAtAttempt5:F1}s. This means the FF codepath was not executed even though the FF is enabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void CreateSessionUsesConstantBackoffWhenFlagDisabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Ensure environment variable is not set (simulating FF being off)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "false");

                try
                {
                    int callCount = 0;

                    _agentServer
                        .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                        .Returns(() =>
                        {
                            callCount++;
                            // Fail first 5 attempts
                            if (callCount <= 5)
                            {
                                throw new Exception("Temporary failure");
                            }
                            return Task.FromResult(new TaskAgentSession());
                        });

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);
                    bool result = await listener.CreateSessionAsync(tokenSource.Token);

                    // Assert
                    Assert.True(result);
                    Assert.True(tc.CapturedDelays.Count >= 5, $"Should have at least 5 delays, got {tc.CapturedDelays.Count}");

                    // Check the 5th delay (index 4)
                    var delayAtAttempt5 = tc.CapturedDelays[4].TotalSeconds;
                    trace.Info($"Delay at attempt 5: {delayAtAttempt5:F1}s (expected ~30s for constant backoff)");

                    // Constant should be exactly 30s
                    Assert.True(delayAtAttempt5 >= 29 && delayAtAttempt5 <= 31,
                        $"Expected ~30s (constant), got {delayAtAttempt5:F1}s. This proves FF codepath was executed even though the FF is disabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void GetNextMessageUsesExponentialBackoffWhenFlagEnabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Set environment variable (simulating Agent.cs setting it after fetching FF)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "true");

                try
                {
                    // Create session first
                    var session = new TaskAgentSession();
                    PropertyInfo sessionIdProperty = session.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Assert.NotNull(sessionIdProperty);
                    sessionIdProperty.SetValue(session, Guid.NewGuid());

                    _agentServer
                        .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                        .Returns(Task.FromResult(session));

                    int callCount = 0;

                    _agentServer
                        .Setup(x => x.GetAgentMessageAsync(_settings.PoolId, session.SessionId, It.IsAny<long?>(), tokenSource.Token))
                        .Returns(() =>
                        {
                            callCount++;
                            // Fail first 6 attempts to check delay at attempt 6
                            if (callCount <= 6)
                            {
                                throw new Exception("Temporary failure");
                            }
                            return Task.FromResult(new TaskAgentMessage 
                            { 
                                MessageId = 123,
                                MessageType = JobRequestMessageTypes.AgentJobRequest,
                                Body = "test"
                            });
                        });

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);
                    await listener.CreateSessionAsync(tokenSource.Token);

                    // Clear delays from CreateSession - we only want GetNextMessage delays
                    tc.CapturedDelays.Clear();

                    TaskAgentMessage message = await listener.GetNextMessageAsync(tokenSource.Token);

                    // Assert - Check captured delays
                    Assert.NotNull(message);

                    Assert.True(tc.CapturedDelays.Count >= 12, $"Should have at least 12 delays (6 backoffs + 6 random), got {tc.CapturedDelays.Count}");

                    // Check the 6th delay (index 10)
                    var delayAtAttempt6 = tc.CapturedDelays[10].TotalSeconds;
                    trace.Info($"Delay at attempt 6: {delayAtAttempt6:F1}s (expected >60s for exponential backoff)");

                    // Exponential should be > 60s (random is [30,60]s)
                    Assert.True(delayAtAttempt6 > 60, 
                        $"Expected exponential (>60s), got {delayAtAttempt6:F1}s. This means the FF codepath was not executed even though the FF is enabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }


            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void GetNextMessageUsesRandomBackoffWhenFlagDisabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Ensure environment variable is not set (simulating FF being off)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "false");

                try
                {
                    //create session first
                    var session = new TaskAgentSession();
                    PropertyInfo sessionIdProperty = session.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Assert.NotNull(sessionIdProperty);
                    sessionIdProperty.SetValue(session, Guid.NewGuid());

                    _agentServer
                        .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                        .Returns(Task.FromResult(session));

                    int callCount = 0;

                    _agentServer
                        .Setup(x => x.GetAgentMessageAsync(_settings.PoolId, session.SessionId, It.IsAny<long?>(), tokenSource.Token))
                        .Returns(() =>
                        {
                            callCount++;
                            // Fail first 6 attempts
                            if (callCount <= 6)
                            {
                                throw new Exception("Temporary failure");
                            }
                            return Task.FromResult(new TaskAgentMessage 
                            { 
                                MessageId = 456,
                                MessageType = JobRequestMessageTypes.AgentJobRequest,
                                Body = "test"
                            });
                        });

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);
                    await listener.CreateSessionAsync(tokenSource.Token);
                    
                    // Clear delays from CreateSession - we only want GetNextMessage delays
                    tc.CapturedDelays.Clear();
                    
                    TaskAgentMessage message = await listener.GetNextMessageAsync(tokenSource.Token);

                    // Assert - Check captured delays
                    Assert.NotNull(message);

                    Assert.True(tc.CapturedDelays.Count >= 12, $"Should have at least 12 delays (6 backoffs + 6 random), got {tc.CapturedDelays.Count}");

                    // Check the 6th delay (index 10)
                    var delayAtAttempt6 = tc.CapturedDelays[10].TotalSeconds;
                    trace.Info($"Delay at attempt 6: {delayAtAttempt6:F1}s (expected [30,60]s for random backoff)");
                    
                    // Random should be in [30,60]s range
                    Assert.True(delayAtAttempt6 >= 30 && delayAtAttempt6 <= 60, 
                        $"Expected [30,60]s (random), got {delayAtAttempt6:F1}s. This proves FF codepath was executed even though the FF is disabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void KeepAliveUsesExponentialBackoffWhenFlagEnabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Set environment variable (simulating Agent.cs setting it after fetching FF)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "true");

                try
                {
                    // Create session first
                    var session = new TaskAgentSession();
                    PropertyInfo sessionIdProperty = session.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Assert.NotNull(sessionIdProperty);
                    sessionIdProperty.SetValue(session, Guid.NewGuid());

                    _agentServer
                        .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                        .Returns(Task.FromResult(session));

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    int callCount = 0;

                    // Setup GetAgentMessageAsync to track KeepAlive calls
                    _agentServer
                        .Setup(x => x.GetAgentMessageAsync(_settings.PoolId, session.SessionId, null, tokenSource.Token))
                        .Returns(() =>
                        {
                            callCount++;
                            // Fail first 5 attempts to check delay at attempt 5
                            if (callCount <= 5)
                            {
                                throw new Exception("KeepAlive failure");
                            }
                            // Cancel after success to stop the infinite loop
                            tokenSource.Cancel();
                            return Task.FromResult<TaskAgentMessage>(null);
                        });

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);
                    await listener.CreateSessionAsync(tokenSource.Token);

                    // Clear delays from CreateSession - we only want GetNextMessage delays
                    tc.CapturedDelays.Clear();

                    // Start KeepAlive in a task and let it run until cancellation
                    var keepAliveTask = listener.KeepAlive(tokenSource.Token);

                    try
                    {
                        await keepAliveTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when token is cancelled
                    }

                    // Assert - Check captured delays
                    Assert.True(tc.CapturedDelays.Count >= 5, $"Should have at least 5 delays, got {tc.CapturedDelays.Count}");

                    // Check the 5th delay (index 4)
                    var delayAtAttempt5 = tc.CapturedDelays[4].TotalSeconds;
                    trace.Info($"KeepAlive delay at attempt 5: {delayAtAttempt5:F1}s (expected >30s for exponential backoff)");

                    // Exponential should be > 30s (constant is 30s)
                    Assert.True(delayAtAttempt5 > 30, 
                        $"Expected exponential (>30s), got {delayAtAttempt5:F1}s. This means the FF codepath was not executed even though the FF is enabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void KeepAliveUsesConstantBackoffWhenFlagDisabled()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange - Ensure environment variable is not set (simulating FF being off)
                Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", "false");

                try
                {
                    var session = new TaskAgentSession();
                    var sessionIdProperty = session.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    sessionIdProperty.SetValue(session, Guid.NewGuid());

                    _agentServer
                        .Setup(x => x.CreateAgentSessionAsync(_settings.PoolId, It.Is<TaskAgentSession>(y => y != null), tokenSource.Token))
                        .Returns(Task.FromResult(session));

                    _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));
                    _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                    var callTimes = new List<DateTime>();
                    int callCount = 0;

                    // Setup GetAgentMessageAsync to track KeepAlive calls
                    _agentServer
                        .Setup(x => x.GetAgentMessageAsync(_settings.PoolId, session.SessionId, null, tokenSource.Token))
                        .Returns(() =>
                        {
                            callTimes.Add(DateTime.UtcNow);
                            callCount++;
                            // Fail first 5 attempts
                            if (callCount <= 5)
                            {
                                throw new Exception("KeepAlive failure");
                            }
                            // Cancel after success to stop the infinite loop
                            tokenSource.Cancel();
                            return Task.FromResult<TaskAgentMessage>(null);
                        });

                    // Act
                    MessageListener listener = new MessageListener();
                    listener.Initialize(tc);
                    await listener.CreateSessionAsync(tokenSource.Token);

                    // Clear delays from CreateSession - we only want GetNextMessage delays
                    tc.CapturedDelays.Clear();

                    // Start KeepAlive in a task and let it run until cancellation
                    var keepAliveTask = listener.KeepAlive(tokenSource.Token);

                    try
                    {
                        await keepAliveTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when token is cancelled
                    }

                    // Assert - Check captured delays
                    Assert.True(tc.CapturedDelays.Count >= 5, $"Should have at least 5 delays, got {tc.CapturedDelays.Count}");

                    // Check the 5th delay (index 4)
                    var delayAtAttempt5 = tc.CapturedDelays[4].TotalSeconds;
                    trace.Info($"KeepAlive delay at attempt 5: {delayAtAttempt5:F1}s (expected ~30s for constant backoff)");

                    // Constant should be exactly 30s
                    Assert.True(delayAtAttempt5 >= 29 && delayAtAttempt5 <= 31, 
                        $"Expected ~30s (constant), got {delayAtAttempt5:F1}s. This proves FF codepath was executed even though the FF is disabled.");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AGENT_ENABLE_PROGRESSIVE_RETRY_BACKOFF", null);
                }
            }
        }

        public void Dispose()
        {
            rsa.Dispose();
        }
    }
}
