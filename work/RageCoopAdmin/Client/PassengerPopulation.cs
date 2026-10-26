using GTA;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Riding in another player's car, both games stood at one spot and each filled
// it with its own pedestrians. RageCoop limits how many of another game's
// pedestrians are shown, so the passenger saw people on the road that the
// driver never had. While riding with a player this game adds no pedestrians
// of its own and shows all of the driver's nearby.
internal static class PassengerPopulation
{
    private static volatile int _ridingWith;

    // RageCoop number of the player whose car this player rides in, 0 for none.
    // Read on the network thread.
    internal static int RidingWith => _ridingWith;

    // Every frame on the game thread.
    internal static void Tick()
    {
        int driver = RemoteDriver();
        _ridingWith = driver;
        if (driver == 0) return;
        Function.Call((Hash)0x95E3D6257B166CF2UL, 0f); // SET_PED_DENSITY_MULTIPLIER_THIS_FRAME
        Function.Call((Hash)0x7A556143A1C03898UL, 0f, 0f); // SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME
    }

    internal static void Reset() => _ridingWith = 0;

    private static int RemoteDriver()
    {
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists()) return 0;
        Vehicle? vehicle = player.CurrentVehicle;
        if (vehicle == null || !vehicle.Exists() || vehicle.IsSeatFree(VehicleSeat.Driver)) return 0;
        Ped? driver = vehicle.GetPedOnSeat(VehicleSeat.Driver);
        if (driver == null || !driver.Exists() || driver.Handle == player.Handle || !RuntimeEntityLookup.IsCurrentPlayerPed(driver.Handle)) return 0;
        return RuntimeEntityLookup.GetPedByHandle(driver.Handle)?.ID ?? 0;
    }
}
