using GTA.Math;

namespace CoopControl.Shared;

// .NET Framework 4.8 has no float.IsFinite.
internal static class Finite
{
    internal static bool Is(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Is(Vector3 value) => Is(value.X) && Is(value.Y) && Is(value.Z);
}
