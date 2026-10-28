using GTA.Math;
using OGCoop.Shared;

namespace RageCoopAdmin.Client;

internal static class VehicleBulletRange
{
    internal const float StandardRangeSquared = 50f * 50f;

    internal static bool NeedsVehicleCheck(float distanceSquared) =>
        Finite.Is(distanceSquared) &&
        distanceSquared > StandardRangeSquared;

    internal static bool IsVehicleHit(Vector3 localHit, Vector3 minimum, Vector3 maximum)
    {
        if (!Finite.Is(localHit) || !Finite.Is(minimum) || !Finite.Is(maximum) ||
            minimum.X >= maximum.X || minimum.Y >= maximum.Y || minimum.Z >= maximum.Z)
        {
            return false;
        }

        // GTA reports impact points on the surface, so allow small interpolation error.
        const float margin = 1.5f;
        return localHit.X >= minimum.X - margin && localHit.X <= maximum.X + margin &&
            localHit.Y >= minimum.Y - margin && localHit.Y <= maximum.Y + margin &&
            localHit.Z >= minimum.Z - margin && localHit.Z <= maximum.Z + margin;
    }

}
