//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    /// <summary>
    /// A thin wrapper around <see cref="System.IO.File" /> class to make it mockable.
    /// </summary>
    [Obsolete("Stop using this interface. This will be reserved for internal use only and it will be removed in the future version.", error: false)]
    public interface IFileWrapper
    {
        /// <summary>
        /// Check if the file in the given path exists or not.
        /// </summary>
        /// <param name="path">Target for existence testing.</param>
        /// <returns>Returns true when the target exists. False otherwise.</returns>
        bool Exist(string path);
    }
}
