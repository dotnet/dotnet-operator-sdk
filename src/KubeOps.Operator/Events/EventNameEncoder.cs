// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

namespace KubeOps.Operator.Events;

public static class EventNameEncoder
{
    /// <summary>
    /// Encodes the given event name using SHA512 and Hex String encoding.
    /// </summary>
    /// <param name="eventName">The original event name to encode.</param>
    /// <returns>The encoded event name.</returns>
    public static string Encode(string eventName)
        => Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(eventName))).ToLowerInvariant();
}
