using System.Linq;
using GTA;
using GTA.Math;
using GTA.UI;

namespace RageCoopAdmin.Client;

// After a death the game puts the player back at a hospital; with a partner
// alive on the server, they continue next to that partner instead. Alone,
// nothing changes.
public sealed partial class AdminMenuScript
{
    private const float PartnerRespawnSkip = 60f;
    private bool _localDied;

    private void TickRespawn()
    {
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists()) return;
        if (player.IsDead)
        {
            _localDied = true;
            return;
        }

        if (_localDied)
        {
            _localDied = false;
            RespawnNearPartner(player);
        }
    }

    private void RespawnNearPartner(Ped player)
    {
        if (_pendingTeleport != null) return;
        Vector3 here = player.Position;
        Vector3[] partners = RuntimeEntityLookup.LivingRemotePlayerPositions();
        if (partners.Length == 0) return;
        Vector3 partner = partners.OrderBy(position => position.DistanceToSquared(here)).First();
        if (partner.DistanceTo(here) < PartnerRespawnSkip) return;
        Notification.PostTicker(UiText.Get("~g~Возрождение:~s~ рядом с напарником."), false);
        BeginTeleport(partner.X + 2f, partner.Y + 2f, partner.Z, "напарнику", timeoutSeconds: PlaceTeleportSeconds, loadScene: true);
    }

    private void ResetRespawn() => _localDied = false;
}
