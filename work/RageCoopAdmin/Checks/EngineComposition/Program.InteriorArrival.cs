using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using GTA.Math;
using HarmonyLib;

// Arriving in an Online interior: the real arrival search runs; only the
// game's collision probe is replaced by a list of floor and roof heights.
internal static partial class Program
{
    private static float[] _arrivalSurfaces = Array.Empty<float>();

    private static void CheckInteriorArrival()
    {
        var boundary = new Harmony("friendslegacy.check.interiorarrival");
        Type script = Resource.GetType("RageCoopAdmin.Client.AdminMenuScript", true)!;
        Type pendingType = script.GetNestedType("PendingTeleport", BindingFlags.NonPublic)!;
        ConstructorInfo create = pendingType.GetConstructors(Instance).Single();
        MethodInfo find = script.GetMethod("TryFindArrival", Static)!;
        boundary.Patch(script.GetMethod("TrySurfaceRay", Static)!, prefix: new HarmonyMethod(typeof(Program), nameof(ArrivalSurfaceRay)));
        try
        {
            float? Arrive(float x, float y, float z, params float[] surfaces)
            {
                _arrivalSurfaces = surfaces;
                object pending = create.Invoke(new object?[] { x, y, z, "interior", Stopwatch.GetTimestamp(), false, 30.0 });
                pendingType.GetProperty("FloorKnown", Instance)!.SetValue(pending, true);
                object?[] arguments = { pending, false, null };
                return (bool)find.Invoke(null, arguments)! ? ((Vector3)arguments[2]!).Z : null;
            }

            Assert(Arrive(976.636f, 70.295f, 115.164f, 122.4f, 114.16f) == 114.16f,
                "Entering the casino penthouse lands on its floor, not on the tower roof above it.");
            Assert(Arrive(1105.483f, 220.212f, -48.99f, -38.5f, -49.99f) == -49.99f,
                "Entering the underground casino hall lands on its floor, not on top of the hall under the street.");
            Assert(Arrive(976.636f, 70.295f, 115.164f, 122.4f) == null,
                "Until the interior's floor has collision the arrival waits instead of using the roof.");
            Assert(Arrive(924.021f, 45.981f, 81.1f, 86.5f, 80.1f) == 80.1f,
                "Leaving through a covered door lands on the street, not on the awning above it.");
        }
        finally { boundary.UnpatchAll(boundary.Id); }
    }

    // The highest upward surface between the probe's ends, as the game reports it.
    private static bool ArrivalSurfaceRay(Vector3 target, float top, float bottom, ref Vector3 surface, ref bool __result)
    {
        float[] hits = _arrivalSurfaces.Where(z => z <= top && z >= bottom).ToArray();
        __result = hits.Length > 0;
        surface = __result ? new Vector3(target.X, target.Y, hits.Max()) : default;
        return false;
    }
}
