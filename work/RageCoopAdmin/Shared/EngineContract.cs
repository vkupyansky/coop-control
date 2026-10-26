using System;
using System.Reflection;
using System.Security.Cryptography;

namespace OGCoop.Shared;

// Guards patch or depend on exact installed RageCoop method bodies: an unknown
// build is rejected instead of being rewritten by guesswork.
internal static class EngineContract
{
    internal static bool HasBody(MethodBase method, string sha256)
    {
        byte[]? body = method.GetMethodBody()?.GetILAsByteArray();
        if (body == null) return false;
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(body)).Replace("-", string.Empty).ToLowerInvariant() == sha256;
    }

    internal static int LocalIndex(object? operand) => operand switch
    {
        LocalVariableInfo local => local.LocalIndex,
        byte index => index,
        int index => index,
        _ => -1
    };
}
