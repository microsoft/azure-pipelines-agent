// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Util
{
    public sealed class ArgUtilL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_MatchesObjectEquality()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                string expected = "Some string".ToLower();  // ToLower is required to avoid reference equality
                string actual = "Some string".ToLower();    // due to compile-time string interning.

                // Act/Assert.
                ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_MatchesReferenceEquality()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                object expected = new object();
                object actual = expected;

                // Act/Assert.
                ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_MatchesStructEquality()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                int expected = 123;
                int actual = expected;

                // Act/Assert.
                ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_ThrowsWhenActualObjectIsNull()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                object expected = new object();
                object actual = null;

                // Act/Assert.
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
                });
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_ThrowsWhenExpectedObjectIsNull()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                object expected = null;
                object actual = new object();

                // Act/Assert.
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
                });
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_ThrowsWhenObjectsAreNotEqual()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                object expected = new object();
                object actual = new object();

                // Act/Assert.
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
                });
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Equal_ThrowsWhenStructsAreNotEqual()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Arrange.
                int expected = 123;
                int actual = 456;

                // Act/Assert.
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    ArgUtil.Equal(expected: expected, actual: actual, name: "Some parameter");
                });
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowIfContainsNull_AllowsCleanNameAndValue()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // Act/Assert - none of these contain a NUL, so nothing is thrown.
                ArgUtil.ThrowIfContainsNull("SAFE_QUEUE_LABEL", "safe-label");
                ArgUtil.ThrowIfContainsNull("NAME", null);
                ArgUtil.ThrowIfContainsNull(null, "value");
                // Newlines are legal in environment variable values and must not be rejected.
                ArgUtil.ThrowIfContainsNull("MULTILINE", "line1\r\nline2");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowIfContainsNull_ThrowsWhenValueContainsNull()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // A NUL in the value would split into two native environment variables.
                string value = "safe-label\0NODE_OPTIONS=--max-old-space-size=2048";

                Assert.Throws<ArgumentException>(() =>
                {
                    ArgUtil.ThrowIfContainsNull("SAFE_QUEUE_LABEL", value);
                });
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ThrowIfContainsNull_ThrowsWhenNameContainsNull()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Tracing trace = hc.GetTrace();

                // A NUL in the name is also rejected.
                string name = "SAFE\0INJECTED";

                Assert.Throws<ArgumentException>(() =>
                {
                    ArgUtil.ThrowIfContainsNull(name, "value");
                });
            }
        }
    }
}
