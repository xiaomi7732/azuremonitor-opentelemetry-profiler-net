//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration.MetricsProviders
{
    /// <summary>
    /// A line parser to parse /proc/meminfo for Linux.
    /// </summary>
    internal sealed class MemInfoItemParser
    {
        public bool TryParse(string line, out (string name, ulong value, string unit) metric)
        {
            string name = string.Empty;
            ulong value = 0;
            string unit = null;

            string[] tokens = line.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens?.Length == 2)
            {
                name = tokens[0].Trim();
                bool result = TryParseMemInfoValue(tokens[1], out value, out unit);
                metric = (name, value, unit);
                return result;
            }
            else
            {
                metric = default;
                return false;
            }
        }

        internal bool TryParseMemInfoValue(string input, out ulong value, out string unit)
        {
            input = input.Trim();
            value = default;
            unit = default;
            string[] tokens = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens?.Length == 2)
            {
                if(ulong.TryParse(tokens[0], out value))
                {
                    unit = tokens[1].Trim();
                    return true;
                }
            }

            return false;
        }
    }
}