using System.Collections.Generic;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

public sealed partial class CoopAdminServer
{
    private const long PlaceInviteCooldownMs = 3000;
    private readonly Dictionary<Client, long> lastPlaceInvite = new();

    private void RegisterSpecialPlaceEvents() =>
        API.RegisterCustomEventHandler(SpecialPlaceProtocol.Invite, OnSpecialPlaceInvite);

    // Relays an invitation to a hidden place; each game loads the place itself.
    private void OnSpecialPlaceInvite(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || !IsCurrentReadyClient(e.Client) ||
            !SpecialPlaceProtocol.TryReadInvite(e.Args, out int place))
        {
            return;
        }

        if (!CanRun(e.Client, lastPlaceInvite, PlaceInviteCooldownMs))
        {
            SendStatus(e.Client, "Подождите пару секунд перед новым приглашением");
            return;
        }

        List<Client> others = new();
        foreach (Client client in ReadyClients())
        {
            if (!ReferenceEquals(client, e.Client)) others.Add(client);
        }

        if (others.Count == 0)
        {
            SendStatus(e.Client, "Напарников на сервере нет: переносим только вас");
            return;
        }

        API.SendCustomEventQueued(others, SpecialPlaceProtocol.Offer, SpecialPlaceProtocol.EncodeOffer(place, e.Client.Username));
    }
}
