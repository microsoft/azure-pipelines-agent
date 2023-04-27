// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class ChunkerTests
    {
        // This test relies on the DedupNodeHashAlgorithm which is from aspnetcidev. This package is apparently not being published anymore.
        // We should either fix this test or remove it soon.
        // [Theory]
        // [InlineData(0, "A7B5F4F67CDA9A678DE6DCBFDE1BE2902407CA2E6E899F843D4EFD1E62778D63")]
        // [InlineData(1, "266CCDBB8509CCADDDD739F1F0751141D154667E9C4754604EB66B1DEE133961")]
        // [InlineData(32 * 1024 - 1, "E697ED9F1250A079DC60AF3FD53793064E020231E96D69554028DD7C2E69D476")]
        // [InlineData(32 * 1024 + 0, "02BB285FBEF36871C6B7694BD684822F5A36104801379B2D225B34A6739946A0")]
        // [InlineData(32 * 1024 + 1, "41D54465B526473D36808AA1B1884CE98278FF1EC4BD83A84CA99590F8809818")]
        // [InlineData(64 * 1024 + 0, "E347F2D06AFA55AE4F928EA70A8180B37447F55B87E784EE2B31FE90B97718B0")]
        // [InlineData(2 * 64 * 1024 - 1, "540770B3F5DF9DD459319164D2AFCAD1B942CB24B41985AA1E0F081D6AC16639")]
        // [InlineData(2 * 64 * 1024 + 0, "3175B5C2595B419DBE5BDA9554208A4E39EFDBCE1FC6F7C7CB959E5B39DF2DF0")]
        // [InlineData(2 * 64 * 1024 + 1, "B39D401B85748FDFC41980A0ABE838BA05805BFFAE16344CE74EA638EE42DEA5")]
        // [Trait("Level", "L0")]
        // [Trait("Category", "Plugin")]
        // public void ChunkerIsStable(int byteCount, string expectedHash)
        // {
        //     var bytes = new byte[byteCount];
        //     FillBufferWithTestContent(seed: 0, bytes);

        //     using (var hasher = new DedupNodeHashAlgorithm())
        //     {
        //         hasher.ComputeHash(bytes, 0, bytes.Length);
        //         var node = hasher.GetNode();
        //         Assert.Equal<string>(expectedHash, node.Hash.ToHex());
        //     }
        // }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA5394: Do not use insecure randomness")]
        private static void FillBufferWithTestContent(int seed, byte[] bytes)
        {
            var r = new Random(seed);
            r.NextBytes(bytes);
            int startZeroes = r.Next(bytes.Length);
            int endZeroes = r.Next(startZeroes, bytes.Length);
            for (int i = startZeroes; i < endZeroes; i++)
            {
                bytes[i] = 0;
            }
        }
    }
}