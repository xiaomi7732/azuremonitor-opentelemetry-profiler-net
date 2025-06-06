//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{
    public class TraceProviderTests
    {
        [Fact]
        public void ShouldHaveAParameterlessCtor()
        {
            EventSourceSpec traceProvider = new EventSourceSpec();
            // Pass if there is no exception thrown.
        }

        [Fact]
        public void ShouldHaveACtorForShortHand()
        {
            string testName = Path.GetTempFileName();
            long keyword = (new Random()).Next(0, int.MaxValue);
            uint level = (uint)(new Random()).Next(0, int.MaxValue);
            Guid providerGuid = Guid.NewGuid();
            EventSourceSpec traceProvider = new EventSourceSpec(testName, providerGuid, keyword, level);

            Assert.Equal(testName, traceProvider.Name);
            Assert.Equal(keyword, traceProvider.Keyword);
            Assert.Equal(level, traceProvider.Level);
            Assert.Equal(providerGuid, traceProvider.ProviderGuid);
        }

        [Fact]
        public void ShouldTreatEqualWhenPropertiesEqual()
        {
            Guid guid1 = Guid.NewGuid();
            Guid guid2 = Guid.NewGuid();
            EventSourceSpec provider1 = new EventSourceSpec("Name1", guid1, 2, 3);
            EventSourceSpec provider2 = new EventSourceSpec("Name1", guid1, 2, 3);
            EventSourceSpec provider3 = new EventSourceSpec("Name2", guid2, 2, 3);

            Assert.True(provider1.Equals(provider2));
            Assert.False(provider1.Equals(provider3));
        }
    }
}
