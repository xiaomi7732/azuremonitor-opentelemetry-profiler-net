//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.IO;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal class SystemFile : IFile
    {
        public bool Exists(string path)
        {
            return File.Exists(path);
        }
    }
}
