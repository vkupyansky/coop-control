using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OGCoop.Shared;
using RageCoop.Server;
using RageCoop.Server.Scripting;

namespace CoopAdmin.Server;

// Characters are kept per RageCoop name like the garage (CharacterStore) and
// handed to the other players, whose games put them on this player's copy.
public sealed partial class CoopAdminServer
{
    private const long CharacterSaveCooldownMs = 1000;
    private readonly object characterGate = new();
    private readonly Dictionary<Client, long> lastCharacterSave = new();
    private CharacterStore? characters;

    private void RegisterCharacterEvents()
    {
        characters = new CharacterStore(CurrentResource.DataFolder);
        API.RegisterCustomEventHandler(CharacterProtocol.Request, OnCharacterRequest);
        API.RegisterCustomEventHandler(CharacterProtocol.Save, OnCharacterSave);
    }

    // A joining player gets their own character and everyone else's, and the
    // others get the joining player's one.
    private void OnCharacterRequest(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 0 } || !CanRequestData(e.Client, CharacterProtocol.Request)) return;
        lock (characterGate)
        {
            // An unreadable file gets no answer: the game then neither offers a
            // new character nor saves one over the file.
            if (!TryLoadCharacter(e.Client, out CharacterLook? own)) return;
            string encoded = own == null ? string.Empty : CharacterCodec.Encode(own);
            var requester = new List<Client> { e.Client };
            API.SendCustomEventQueued(requester, CharacterProtocol.State, encoded);
            List<Client> others = ReadyClients().Where(client => !ReferenceEquals(client, e.Client)).ToList();
            foreach (Client other in others)
            {
                if (TryLoadCharacter(other, out CharacterLook? look) && look != null)
                    API.SendCustomEventQueued(requester, CharacterProtocol.Look, other.Username, CharacterCodec.Encode(look));
            }
            if (own != null && others.Count > 0) API.SendCustomEventQueued(others, CharacterProtocol.Look, e.Client.Username, encoded);
        }
    }

    private void OnCharacterSave(CustomEventReceivedArgs e)
    {
        if (!HasReadyPlayer(e) || e.Args is not { Length: 1 } || e.Args[0] is not string xml) return;
        lock (characterGate)
        {
            CharacterLook look;
            try { look = CharacterCodec.Decode(xml); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
            {
                SendCharacterSaved(e.Client, false);
                return;
            }
            bool saved = CanRun(e.Client, lastCharacterSave, CharacterSaveCooldownMs) && TrySaveCharacter(e.Client, look);
            SendCharacterSaved(e.Client, saved);
            if (!saved) return;
            List<Client> others = ReadyClients().Where(client => !ReferenceEquals(client, e.Client)).ToList();
            if (others.Count > 0) API.SendCustomEventQueued(others, CharacterProtocol.Look, e.Client.Username, CharacterCodec.Encode(look));
        }
    }

    private void OnCharacterPlayerDisconnected(Client client)
    {
        lock (characterGate) lastCharacterSave.Remove(client);
    }

    private bool TryLoadCharacter(Client client, out CharacterLook? look)
    {
        try
        {
            look = characters!.Load(client.Username);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            // An unreadable file is kept untouched rather than replaced.
            Logger.Warning($"Character of {client.Username} could not be loaded: {error.GetType().Name}");
            look = null;
            return false;
        }
    }

    private bool TrySaveCharacter(Client client, CharacterLook look)
    {
        try
        {
            characters!.Save(client.Username, look);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Warning($"Character of {client.Username} could not be saved: {error.GetType().Name}");
            return false;
        }
    }

    private void SendCharacterSaved(Client client, bool saved) =>
        API.SendCustomEventQueued(new List<Client> { client }, CharacterProtocol.Saved, saved);
}
