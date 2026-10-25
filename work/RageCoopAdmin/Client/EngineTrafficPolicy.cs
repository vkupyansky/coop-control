using System;
using CoopControl.Shared;

namespace RageCoopAdmin.Client;

internal static class EngineTrafficPolicy
{
    // Keep enough road ahead to cover fast driving while GTA streams new traffic.
    internal const float ProtectedDistance = 150f;

    // GTA creates traffic out of its own player's view. A new car that is
    // clearly closer to another player can be right in front of him, where
    // it would pop in; his own game fills his surroundings instead.
    internal const float PopInDistance = 150f;
    internal const float PopInMargin = 20f;

    internal static bool MayPopIn(float remoteDistanceSquared) =>
        Finite.Is(remoteDistanceSquared) && remoteDistanceSquared <= PopInDistance * PopInDistance;

    internal static bool SuppressSpawn(bool onScreen, bool persistent, bool playerOccupant, float localDistanceSquared, float remoteDistanceSquared) =>
        !onScreen && !persistent && !playerOccupant && MayPopIn(remoteDistanceSquared) && Finite.Is(localDistanceSquared) &&
        Math.Sqrt(remoteDistanceSquared) + PopInMargin < Math.Sqrt(localDistanceSquared);

    // RageCoop creates at most its soft limit divided by the player count of
    // another player's cars and NPCs and drops the rest. Near this player that
    // made his traffic pop in late, drive without a driver or exist only in
    // his game, where the two players then crashed into each other's cars.
    internal static bool CreateNearbyCopy(float localDistanceSquared) =>
        Finite.Is(localDistanceSquared) && localDistanceSquared <= ProtectedDistance * ProtectedDistance;

    // Both games create the same parked cars, boats, shop clerks and scenario
    // NPCs at fixed points. A copy of another game's one on top of this
    // game's own is not created: each player sees his own.
    internal const float VehicleTwinDistance = 3f;
    internal const float PedTwinDistance = 2f;

    // Ties go to the lower RageCoop player number. Only GTA's own random
    // vehicles are replaced; a persistent or script vehicle stays per game.
    internal static bool TheirCopyWins(int owner, int local, bool replaceable) =>
        replaceable && owner != 0 && local != 0 && owner < local;

    internal static bool IsReplaceable(bool persistent, GTA.EntityPopulationType population) =>
        !persistent && population >= GTA.EntityPopulationType.RandomPermanent && population <= GTA.EntityPopulationType.RandomAmbient;

    internal static bool CanCull(bool overLimit, bool onScreen, bool persistent, bool playerOccupant, float playerDistanceSquared) =>
        overLimit && !onScreen && !persistent && !playerOccupant &&
        Finite.Is(playerDistanceSquared) &&
        playerDistanceSquared > ProtectedDistance * ProtectedDistance;
}
