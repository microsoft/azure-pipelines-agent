// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.Services.PipelineCache.WebApi;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.PipelineCache
{
    public class FingerprintTests
    {
        private static void AssertBothOrders<T>(Action<T, T> assert, T t1, T t2)
        {
            assert(t1, t2);
            assert(t2, t1);
        }

        private void FingerprintEqualityWorksHelper(Fingerprint same1, Fingerprint same2, Fingerprint different)
        {
            AssertBothOrders((a, b) => Assert.False(a != null && b != null && object.ReferenceEquals(a, b)), same1, same2);
            AssertBothOrders((a, b) => Assert.False(a != null && b != null && object.ReferenceEquals(a, b)), same1, different);
            AssertBothOrders((a, b) => Assert.False(a != null && b != null && object.ReferenceEquals(a, b)), same2, different);

            AssertBothOrders(Assert.Equal, same1, same2);
            AssertBothOrders(Assert.NotEqual, same1, different);
            AssertBothOrders(Assert.NotEqual, same2, different);

            AssertBothOrders((a, b) => Assert.True(a == b), same1, same2);
            AssertBothOrders((a, b) => Assert.False(a == b), same1, different);
            AssertBothOrders((a, b) => Assert.False(a == b), same2, different);

            AssertBothOrders((a, b) => Assert.False(a != b), same1, same2);
            AssertBothOrders((a, b) => Assert.True(a != b), same1, different);
            AssertBothOrders((a, b) => Assert.True(a != b), same2, different);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Plugin")]
        public void FingerprintEqualityWorks()
        {
            FingerprintEqualityWorksHelper(
                same1: new Fingerprint("same"),
                same2: new Fingerprint("same"),
                different: new Fingerprint("different"));

            FingerprintEqualityWorksHelper(
                same1: new Fingerprint("same"),
                same2: new Fingerprint("same"),
                different: null);

            FingerprintEqualityWorksHelper(
                same1: null,
                same2: null,
                different: new Fingerprint("different"));

            FingerprintEqualityWorksHelper(
                same1: new Fingerprint("a", "b"),
                same2: new Fingerprint("a", "b"),
                different: new Fingerprint("a"));
        }
    }
}