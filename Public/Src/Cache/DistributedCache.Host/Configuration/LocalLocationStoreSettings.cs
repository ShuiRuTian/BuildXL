// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Pass through settings used by local location store. Settings specified here
    /// can appear directly in configuration file and be used in LocalLocationStore without
    /// the need to do explicit passing through configuration layers.
    /// </summary>
    public record LocalLocationStoreSettings
    {
        /// <summary>
        /// Controls delay for RegisterLocation operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? RegisterLocationDelay { get; set; }

        /// <summary>
        /// Controls delay for GetBulk operation to allow for throttling
        /// </summary>
        public TimeSpanSetting? GlobalGetBulkLocationDelay { get; set; }
    }
}