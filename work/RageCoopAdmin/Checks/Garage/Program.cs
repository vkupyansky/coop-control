using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopAdmin.Server;
using OGCoop.Shared;
using RageCoopAdmin.Client;

internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopGarageCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckWireContract();
            CheckCodec();
            CheckChanges();
            CheckServerStore(Path.Combine(directory, "server"));
            CheckClient(Path.Combine(directory, "client"));
            CheckWorldModes(Path.Combine(directory, "modes"));
            CheckCharacters(Path.Combine(directory, "server"));
            CheckYachts(Path.Combine(directory, "server"));
            CheckHomes(Path.Combine(directory, "server"));
            Console.WriteLine($"Garage and character checks passed: {_checks}. Shared rules, server files and the client run as built; the network is a direct call.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckWorldModes(string directory)
    {
        var store = new WorldModeStore(directory);
        Assert(store.LoadGhostTraffic() == false, "a new server starts with traffic pass-through off");
        store.SaveGhostTraffic(true);
        Assert(new WorldModeStore(directory).LoadGhostTraffic() == true, "turning pass-through on survives a server restart");
        store.SaveGhostTraffic(false);
        Assert(new WorldModeStore(directory).LoadGhostTraffic() == false, "turning it off is remembered too");
        File.WriteAllText(Path.Combine(directory, "world-modes.txt"), "ghost-traffic=maybe");
        Assert(store.LoadGhostTraffic() == null, "a damaged file is reported, not read as a choice");
        File.WriteAllText(Path.Combine(directory, "world-modes.txt"), new string('x', 100));
        Assert(store.LoadGhostTraffic() == null, "an oversized file is not read");
    }

    private static CharacterLook Character(int model, int top)
    {
        var look = new CharacterLook { Model = model };
        for (int slot = 0; slot < CharacterCodec.ComponentCount; slot++)
            look.Clothes.Add(new CharacterPart { Slot = slot, Drawable = slot == 11 ? top : 0, Texture = slot == 11 ? 3 : 0 });
        look.Props.Add(new CharacterPart { Slot = 0, Drawable = 12, Texture = 1 });
        return look;
    }

    // Characters: the same per-name files as the garage, a strict format and
    // the client's view of its own and the other players' characters.
    private static void CheckCharacters(string folder)
    {
        CharacterLook man = Character(CharacterModels.Male, 392);
        CharacterLook copy = CharacterCodec.Decode(CharacterCodec.Encode(man));
        Assert(copy.Model == CharacterModels.Male && copy.Clothes.Single(part => part.Slot == 11).Drawable == 392 &&
            copy.Props.Single().Drawable == 12, "an Online character keeps items above 255, which RageCoop's own clothes cut");
        Assert(CharacterCodec.SameLook(man, copy) && !CharacterCodec.SameLook(man, Character(CharacterModels.Female, 392)),
            "characters compare by their content");
        Assert(CharacterCodec.Decode(CharacterCodec.Encode(new CharacterLook())).IsOnline == false, "the story hero choice is a character of its own");
        MustReject(() => CharacterCodec.Encode(new CharacterLook { Model = 12345 }), "an unknown body is refused");
        MustReject(() => CharacterCodec.Encode(new CharacterLook { Clothes = Character(CharacterModels.Male, 1).Clothes }), "a story hero carries no clothes of ours");
        CharacterLook missing = Character(CharacterModels.Female, 1);
        missing.Clothes.RemoveAt(4);
        MustReject(() => CharacterCodec.Encode(missing), "every clothing slot is listed");
        CharacterLook twice = Character(CharacterModels.Female, 1);
        twice.Props.Add(new CharacterPart { Slot = 0, Drawable = 1 });
        MustReject(() => CharacterCodec.Encode(twice), "an accessory slot is listed once");
        CharacterLook wrongSlot = Character(CharacterModels.Female, 1);
        wrongSlot.Props[0].Slot = 3;
        MustReject(() => CharacterCodec.Encode(wrongSlot), "only GTA Online's accessory slots are accepted");
        CharacterLook huge = Character(CharacterModels.Male, CharacterCodec.MaximumDrawable + 1);
        MustReject(() => CharacterCodec.Encode(huge), "item numbers are bounded");
        MustReject(() => CharacterCodec.Decode("<!DOCTYPE Character [<!ENTITY x \"y\">]><Character version=\"1\"><Model>0</Model></Character>"),
            "a document type is refused");
        MustReject(() => CharacterCodec.Decode("<Character version=\"2\"><Model>0</Model></Character>"), "an unknown format version is refused");

        CheckCharacterHead();
        CheckCharacterTattoos();

        var store = new CharacterStore(folder);
        Assert(store.Load("Rider") == null, "a player without a character has none, so the editor is offered");
        store.Save("Rider", man);
        Assert(CharacterCodec.SameLook(store.Load("RIDER"), man), "the character follows the RageCoop name regardless of letter case");
        Assert(File.Exists(Path.Combine(folder, "characters", "rider.xml")) && File.Exists(Path.Combine(folder, "garages", "rider.xml")),
            "the character sits next to the garage under the same name");
        Assert(store.Load("Pilot") == null, "another player's character is separate");
        string broken = Path.Combine(folder, "characters", "broken.xml");
        File.WriteAllText(broken, "<Character");
        MustReject(() => store.Load("broken"), "an unreadable character is reported");
        Assert(File.ReadAllText(broken) == "<Character", "an unreadable character file is left untouched");
        File.WriteAllText(Path.Combine(folder, "characters", "big.xml"), new string(' ', CharacterCodec.MaximumXmlCharacters * 4 + 1));
        MustReject(() => store.Load("big"), "an oversized character file is not read");

        var sent = new List<(int Hash, object[] Args)>();
        var client = new CharacterLooks((hash, args) => sent.Add((hash, args)));
        client.Request();
        Assert(sent.Single().Hash == CharacterProtocol.Request && sent.Single().Args.Length == 0 && !client.OwnKnown,
            "the game asks the server and offers nothing before it answers");
        Assert(client.ApplyState(new object[] { string.Empty }) && client.OwnKnown && client.Own == null,
            "an empty answer means no character yet");
        Assert(client.ApplyState(new object[] { CharacterCodec.Encode(man) }) && CharacterCodec.SameLook(client.Own, man),
            "the saved character arrives");
        Assert(!client.ApplyState(new object[] { "<Character" }) && CharacterCodec.SameLook(client.Own, man),
            "a damaged answer keeps what the game knew");
        Assert(client.ApplyLook(new object[] { "Pilot", CharacterCodec.Encode(man) }, out string name) && name == "Pilot" &&
            client.Of("pilot") != null && client.Of("Rider") == null, "another player's character is kept by name, any letter case");
        Assert(!client.ApplyLook(new object[] { "", CharacterCodec.Encode(man) }, out _) && !client.ApplyLook(new object[] { "Pilot" }, out _),
            "a nameless or incomplete message is ignored");
        client.Save(Character(CharacterModels.Female, 5));
        Assert(sent.Last().Hash == CharacterProtocol.Save && sent.Last().Args is [string saved] && CharacterCodec.Decode(saved).Model == CharacterModels.Female,
            "saving sends the whole character");
        client.Clear();
        Assert(!client.OwnKnown && client.Own == null && client.Of("Pilot") == null, "leaving the server forgets every character");
    }

    // The face: GTA Online's parents, features, colours and overlays, with
    // characters saved before the face editor still readable.
    private static void CheckCharacterHead()
    {
        CharacterLook earlier = CharacterCodec.Decode(CharacterCodec.Encode(Character(CharacterModels.Male, 5)));
        Assert(earlier.Head == null && !CharacterCodec.Encode(earlier).Contains("<Head"),
            "a character saved before the face editor has no face of its own and keeps the default one");
        CharacterHead man = CharacterHead.Default(CharacterModels.Male), woman = CharacterHead.Default(CharacterModels.Female);
        Assert(man.Mother == 21 && man.Father == 0 && man.Resemblance == 80 && woman.Resemblance == 20 &&
            man.Overlay(CharacterCodec.Eyebrows).Style == 0 && man.Overlay(1).Style == -1,
            "a new face has Hannah and Benjamin as parents, takes after the parent of its sex and has eyebrows only");

        CharacterLook look = Character(CharacterModels.Female, 7);
        look.Head = CharacterHead.Default(CharacterModels.Female);
        look.Head.Mother = 45;
        look.Head.Father = 44;
        look.Head.SetFeature(3, -10);
        look.Head.SetFeature(19, 10);
        look.Head.SetFeature(5, 0);
        look.Head.SetOverlay(new CharacterOverlay { Slot = 8, Style = 4, Opacity = 60, Color = 63 });
        look.Head.SetOverlay(new CharacterOverlay { Slot = 1, Style = -1, Opacity = 30, Color = 2 });
        look.Head.EyeColor = 31;
        look.Head.HairColor = 63;
        look.Head.HairHighlight = 12;
        CharacterLook copy = CharacterCodec.Decode(CharacterCodec.Encode(look));
        CharacterHead head = copy.Head!;
        Assert(head.Mother == 45 && head.Father == 44 && head.Feature(3) == -10 && head.Feature(19) == 10 && head.Feature(5) == 0 &&
            head.Features.Count == 2 && head.EyeColor == 31 && head.HairColor == 63 && head.HairHighlight == 12,
            "parents, features, eyes and hair colour survive saving; a zero feature is not stored");
        Assert(head.Overlay(8).Style == 4 && head.Overlay(8).Opacity == 60 && head.Overlay(8).Color == 63 &&
            head.Overlay(1).Style == -1 && head.Overlay(1).Opacity == 30 && head.Overlay(CharacterCodec.Eyebrows).Style == 0,
            "overlays keep style, strength and colour, also while switched off");
        CharacterHead edited = head.Copy();
        edited.Overlay(8).Opacity = 10;
        edited.SetFeature(3, 1);
        Assert(head.Overlay(8).Opacity == 60 && head.Feature(3) == -10, "a copy of the face is edited without touching the original");

        foreach ((string label, Action<CharacterHead> spoil) in new (string, Action<CharacterHead>)[]
        {
            ("an unknown parent is refused", spoiled => spoiled.Mother = CharacterCodec.ParentCount),
            ("a resemblance above 100% is refused", spoiled => spoiled.Resemblance = 101),
            ("an unknown eye colour is refused", spoiled => spoiled.EyeColor = CharacterCodec.EyeColorCount),
            ("an unknown hair colour is refused", spoiled => spoiled.HairHighlight = -1),
            ("a feature beyond ±10 is refused", spoiled => spoiled.Features.Add(new CharacterFeature { Index = 2, Value = 11 })),
            ("a feature listed twice is refused", spoiled => spoiled.Features.Add(new CharacterFeature { Index = 3, Value = 1 })),
            ("an unknown feature is refused", spoiled => spoiled.Features.Add(new CharacterFeature { Index = CharacterCodec.FeatureCount, Value = 1 })),
            ("an unknown overlay is refused", spoiled => spoiled.Overlays.Add(new CharacterOverlay { Slot = CharacterCodec.OverlayCount, Style = 0 })),
            ("an overlay listed twice is refused", spoiled => spoiled.Overlays.Add(new CharacterOverlay { Slot = 8, Style = 0 })),
            ("an overlay strength above 100% is refused", spoiled => spoiled.Overlay(8).Opacity = 101),
            ("an unknown overlay colour is refused", spoiled => spoiled.Overlay(8).Color = CharacterCodec.TintCount),
        })
        {
            CharacterLook spoiledLook = CharacterCodec.Decode(CharacterCodec.Encode(look));
            spoil(spoiledLook.Head!);
            MustReject(() => CharacterCodec.Encode(spoiledLook), label);
        }
        MustReject(() => CharacterCodec.Encode(new CharacterLook { Head = CharacterHead.Default(CharacterModels.Male) }),
            "a story hero carries no face of ours");
    }

    // Yachts: GTA Online's berths and looks, kept per name like the garage, and
    // the game's view of its own and the other players' yachts.
    private static void CheckYachts(string folder)
    {
        Assert(Yachts.Berths.Length == YachtCodec.BerthCount && Yachts.Berths.Select((berth, index) => berth.Id == index).All(ok => ok) &&
            Yachts.Berths.Select(berth => berth.Hull).Distinct().Count() == YachtCodec.BerthCount &&
            Yachts.Berths[0].Hull == "apa_yacht_grp01_1" && Yachts.Berths[35].Interior == "apa_yacht_grp12_3_int" &&
            Yachts.Berths[YachtCodec.CarrierBerth].Hull == "apa_yacht_grp02_2",
            "36 berths of GTA Online in the script's order, each with its own hull and interior");
        Assert(Yachts.Flags.Length == YachtCodec.FlagCount && Yachts.Flags.Select(flag => flag.Model).Distinct().Count() == Yachts.Flags.Length &&
            Yachts.Colors.Length == YachtCodec.ColorCount && Yachts.Models.Length == YachtCodec.ModelCount,
            "every flag, colour and model of the yacht has its own entry");
        var aquaris = new YachtLook { Model = 2, GoldRails = true, Lighting = 8 };
        string[] parts = Yachts.Parts(aquaris).Select(part => part.Model).ToArray();
        Assert(parts.Contains("apa_mp_apa_yacht_option3") && parts.Contains("apa_mp_apa_yacht_o3_rail_b") && parts.Contains("apa_mp_apa_y3_l2d") &&
            parts.Contains("apa_mp_apa_yacht_option3_cole") && Yachts.Parts(new YachtLook()).All(part => !part.Model.Contains("_l")),
            "the Aquaris with gold rails and gold Vivacious lights gets its decks, rails and lights; no lights means none");

        var look = new YachtLook { Berth = 33, Model = 1, Color = 15, GoldRails = true, Lighting = 3, Flag = YachtCodec.FlagCount };
        YachtLook copy = YachtCodec.Decode(YachtCodec.Encode(look));
        Assert(YachtCodec.Same(look, copy) && copy.Berth == 33 && copy.Flag == YachtCodec.FlagCount && copy.GoldRails,
            "a yacht keeps its berth, model, colour, rails, lights and flag");
        foreach ((string label, Action<YachtLook> spoil) in new (string, Action<YachtLook>)[]
        {
            ("the berth beside the carrier is refused", spoiled => spoiled.Berth = YachtCodec.CarrierBerth),
            ("an unknown berth is refused", spoiled => spoiled.Berth = YachtCodec.BerthCount),
            ("an unknown model is refused", spoiled => spoiled.Model = YachtCodec.ModelCount),
            ("an unknown colour is refused", spoiled => spoiled.Color = -1),
            ("unknown lights are refused", spoiled => spoiled.Lighting = YachtCodec.LightingCount + 1),
            ("an unknown flag is refused", spoiled => spoiled.Flag = YachtCodec.FlagCount + 1),
        })
        {
            YachtLook spoiled = YachtCodec.Copy(look);
            spoil(spoiled);
            MustReject(() => YachtCodec.Encode(spoiled), label);
        }
        MustReject(() => YachtCodec.Decode("<!DOCTYPE Yacht [<!ENTITY x \"y\">]><Yacht version=\"1\"><Berth>0</Berth></Yacht>"), "a document type is refused");
        Assert(YachtCodec.FreeBerth(3, berth => berth == 3) == 5 && YachtCodec.FreeBerth(35, berth => berth == 35) == 0 &&
            YachtCodec.FreeBerth(0, berth => true) == -1, "the next free berth skips taken ones and the carrier's");

        var store = new YachtStore(folder);
        Assert(store.Load("Rider") == null, "a player without a yacht has none");
        store.Save("Rider", look);
        Assert(YachtCodec.Same(store.Load("RIDER"), look) && File.Exists(Path.Combine(folder, "yachts", "rider.xml")),
            "the yacht follows the RageCoop name regardless of letter case, next to the garage");

        var sent = new List<(int Hash, object[] Args)>();
        var client = new YachtLooks((hash, args) => sent.Add((hash, args)));
        client.Request();
        Assert(sent.Single().Hash == YachtProtocol.Request && !client.OwnKnown, "the game asks the server for its yacht");
        Assert(client.ApplyState(new object[] { string.Empty }) && client.OwnKnown && client.Own == null && !client.All().Any(),
            "an empty answer means no yacht yet");
        int revision = client.Revision;
        Assert(client.ApplyLook(new object[] { "Pilot", YachtCodec.Encode(look) }) && client.TakenByOther(33) && client.Revision > revision &&
            client.All().Single().Owner == "Pilot", "another player's yacht is put on the map and takes its berth");
        Assert(client.ApplyLook(new object[] { "pilot", string.Empty }) && !client.All().Any() && !client.TakenByOther(33),
            "a player who left takes the yacht along");
        Assert(!client.ApplyLook(new object[] { "Pilot", "<Yacht" }) && !client.ApplyLook(new object[] { "", YachtCodec.Encode(look) }),
            "a damaged or nameless yacht is ignored");
        client.Saved(look);
        Assert(client.All().Single().Owner.Length == 0 && client.Own!.Berth == 33, "this player's saved yacht is on the map");
        client.Clear();
        Assert(!client.OwnKnown && !client.All().Any(), "leaving the server forgets every yacht");
    }

    // Tattoos: GTA decoration hashes, with characters saved before them unchanged.
    private static void CheckCharacterTattoos()
    {
        CharacterLook earlier = Character(CharacterModels.Male, 5);
        string earlierXml = CharacterCodec.Encode(earlier);
        Assert(!earlierXml.Contains("Tattoo") && CharacterCodec.Decode(earlierXml).Tattoos.Count == 0,
            "a character without tattoos is saved as before and reads back with none");
        CharacterLook look = Character(CharacterModels.Female, 5);
        look.Tattoos.Add(new CharacterTattoo { Collection = unchecked((int)0xA9B4D8A1), Name = 1185713461 });
        look.Tattoos.Add(new CharacterTattoo { Collection = 62137527, Name = -1029112321 });
        CharacterLook copy = CharacterCodec.Decode(CharacterCodec.Encode(look));
        Assert(copy.Tattoos.Count == 2 && copy.Tattoos[0].Collection == unchecked((int)0xA9B4D8A1) && copy.Tattoos[1].Name == -1029112321 &&
            CharacterCodec.SameLook(look, copy) && !CharacterCodec.SameLook(look, Character(CharacterModels.Female, 5)),
            "tattoos survive saving in their order and make the character differ");

        CharacterLook twice = CharacterCodec.Decode(CharacterCodec.Encode(look));
        twice.Tattoos.Add(new CharacterTattoo { Collection = 62137527, Name = -1029112321 });
        MustReject(() => CharacterCodec.Encode(twice), "a tattoo listed twice is refused");
        CharacterLook empty = CharacterCodec.Decode(CharacterCodec.Encode(look));
        empty.Tattoos.Add(new CharacterTattoo { Collection = 62137527, Name = 0 });
        MustReject(() => CharacterCodec.Encode(empty), "a tattoo without its item is refused");
        CharacterLook many = Character(CharacterModels.Male, 5);
        many.Head = CharacterHead.Default(CharacterModels.Male);
        // The longest hashes, so the size limit is tried at its worst.
        for (int index = 1; index <= CharacterCodec.MaximumTattoos + 1; index++)
            many.Tattoos.Add(new CharacterTattoo { Collection = -2000000000 + index, Name = -1000000000 - index });
        MustReject(() => CharacterCodec.Encode(many), "more than " + CharacterCodec.MaximumTattoos + " tattoos are refused");
        many.Tattoos.RemoveAt(0);
        Assert(CharacterCodec.Decode(CharacterCodec.Encode(many)).Tattoos.Count == CharacterCodec.MaximumTattoos,
            CharacterCodec.MaximumTattoos + " tattoos fit the character's size limit");
        CharacterLook hero = new CharacterLook();
        hero.Tattoos.Add(new CharacterTattoo { Collection = 1, Name = 2 });
        MustReject(() => CharacterCodec.Encode(hero), "a story hero carries no tattoos of ours");
    }

    private static VehicleAppearance Appearance()
    {
        var value = new VehicleAppearance
        {
            ModelHash = -1216765807,
            Primary = 27,
            Secondary = 111,
            PrimaryRgb = 0x0A24F1,
            WheelType = 7,
            FrontCustomTires = true,
            RearCustomTires = false,
            NeonMask = 9,
            NeonRgb = 0x00FF19,
            ExtrasPresent = 7,
            ExtrasEnabled = 5,
            Plate = "FRIENDS",
            Livery = 2,
            ToggleMask = 63
        };
        value.Mods[0] = 4;
        value.Mods[23] = 12;
        return value;
    }

    private static void CheckWireContract()
    {
        VehicleAppearance expected = Appearance();
        string xml = VehicleAppearanceCodec.Encode(expected);
        VehicleAppearance actual = VehicleAppearanceCodec.Decode(xml);
        Assert(actual.ModelHash == expected.ModelHash && actual.Mods.SequenceEqual(expected.Mods) &&
            actual.PrimaryRgb == expected.PrimaryRgb && actual.FrontCustomTires && !actual.RearCustomTires && actual.NeonMask == 9 &&
            actual.ExtrasEnabled == 5 && actual.ToggleMask == 63 && actual.Plate == "FRIENDS",
            "network roundtrip preserves signed model, mods, RGB, toggles and plate");
        MustReject(() => VehicleAppearanceCodec.Decode(new string(' ', VehicleAppearanceCodec.MaximumXmlCharacters + 1)), "network payload size is bounded");
        MustReject(() => VehicleAppearanceCodec.Decode("<!DOCTYPE VehicleAppearance [<!ENTITY x SYSTEM 'file:///do-not-read'>]>" + xml.Substring(xml.IndexOf("<VehicleAppearance", StringComparison.Ordinal))), "network DTD is rejected");
        VehicleAppearance invalid = Appearance();
        invalid.Mods[23] = 201;
        MustReject(() => VehicleAppearanceCodec.Encode(invalid), "out-of-range native mod is rejected");
        invalid = Appearance();
        invalid.NeonMask = 16;
        MustReject(() => VehicleAppearanceCodec.Encode(invalid), "unsupported neon bits are rejected");
        invalid = Appearance();
        invalid.ExtrasEnabled = 8;
        MustReject(() => VehicleAppearanceCodec.Encode(invalid), "nonexistent extras cannot be enabled");
        invalid = Appearance();
        invalid.Plate = "~r~TEXT";
        MustReject(() => VehicleAppearanceCodec.Encode(invalid), "GTA markup cannot enter plate text");
        invalid = Appearance();
        invalid.Mods = invalid.Mods.Take(49).ToArray();
        MustReject(() => VehicleAppearanceCodec.Encode(invalid), "partial native mod arrays are rejected");
    }

    private static VehiclePreset Preset(string name, int model = -1216765807)
    {
        VehicleAppearance appearance = Appearance();
        appearance.ModelHash = model;
        return new VehiclePreset { Name = name, Appearance = appearance };
    }

    private static object[] Change(int token, int operation, params object[] values) => new object[] { token, operation }.Concat(values).ToArray();

    private static void CheckCodec()
    {
        var garage = new GarageData { Vehicles = { Preset("Майбах"), Preset("M3 F80", 101) }, Favorites = { 11, -22 } };
        GarageData copy = GarageCodec.Decode(GarageCodec.Encode(garage));
        Assert(copy.Vehicles.Select(preset => preset.Name).SequenceEqual(new[] { "Майбах", "M3 F80" }) && copy.Vehicles[1].Appearance.ModelHash == 101 &&
            copy.Favorites.SequenceEqual(new[] { 11, -22 }), "a garage keeps its cars, appearance and favourites through the server message");
        MustReject(() => GarageCodec.Decode(new string(' ', GarageCodec.MaximumXmlCharacters + 1)), "an oversized garage message is rejected before parsing");
        string xml = GarageCodec.Encode(garage);
        MustReject(() => GarageCodec.Decode("<!DOCTYPE Garage [<!ENTITY x SYSTEM 'file:///do-not-read'>]>" + xml.Substring(xml.IndexOf("<Garage", StringComparison.Ordinal))), "a garage DTD is rejected");
        MustReject(() => GarageCodec.Encode(new GarageData { Vehicles = { Preset("Одна"), Preset("одна") } }), "names differing only in case are one car");
        MustReject(() => GarageCodec.Encode(new GarageData { Vehicles = { Preset("~r~Красная") } }), "GTA markup cannot enter a car name");
        MustReject(() => GarageCodec.Encode(new GarageData { Favorites = { 5, 5 } }), "a favourite model is stored once");
        MustReject(() => GarageCodec.Encode(new GarageData { Vehicles = Enumerable.Range(0, 65).Select(i => Preset("Машина " + i)).ToList() }), "the garage holds at most 64 cars");

        string legacyPresets = "<SavedVehicles version=\"1\"><Vehicles><Vehicle><Name>Старая</Name>" +
            VehicleAppearanceCodec.Encode(Appearance()).Substring(VehicleAppearanceCodec.Encode(Appearance()).IndexOf("<VehicleAppearance", StringComparison.Ordinal))
            .Replace("<VehicleAppearance", "<Appearance").Replace("</VehicleAppearance>", "</Appearance>") + "</Vehicle></Vehicles></SavedVehicles>";
        string legacyFavorites = "<FavoriteVehicles version=\"1\"><Models><Model>7</Model></Models></FavoriteVehicles>";
        GarageData legacy = GarageCodec.ReadLegacy(new StringReader(legacyPresets), new StringReader(legacyFavorites));
        Assert(legacy.Vehicles.Single().Name == "Старая" && legacy.Vehicles[0].Appearance.Plate == "FRIENDS" && legacy.Favorites.SequenceEqual(new[] { 7 }),
            "the former computer files of the garage and favourites are read for moving");
        Assert(GarageCodec.ReadLegacy(null, null).Vehicles.Count == 0, "an absent former file is an empty part");
    }

    private static void CheckChanges()
    {
        var garage = new GarageData();
        string appearance = VehicleAppearanceCodec.Encode(Appearance());
        Assert(GarageCodec.TryApply(garage, Change(1, GarageProtocol.SavePreset, "Майбах", appearance), out int token, out GarageResult result) &&
            token == 1 && result == GarageResult.Saved && garage.Vehicles.Single().Name == "Майбах", "a named car is saved with its appearance");
        GarageCodec.TryApply(garage, Change(2, GarageProtocol.SavePreset, "майбах", appearance), out _, out result);
        Assert(result == GarageResult.DuplicateName && garage.Vehicles.Count == 1, "a second car with the same name is refused");
        GarageCodec.TryApply(garage, Change(3, GarageProtocol.SavePreset, "Битая", "<VehicleAppearance"), out _, out result);
        Assert(result == GarageResult.Invalid && garage.Vehicles.Count == 1, "an unreadable appearance is refused without a change");
        GarageCodec.TryApply(garage, Change(4, GarageProtocol.DeletePreset, "Нет такой"), out _, out result);
        Assert(result == GarageResult.Missing, "deleting a car that is not there reports a changed list");
        GarageCodec.TryApply(garage, Change(5, GarageProtocol.DeletePreset, "Майбах"), out _, out result);
        Assert(result == GarageResult.Saved && garage.Vehicles.Count == 0, "a car is deleted by its exact name");
        GarageCodec.TryApply(garage, Change(6, GarageProtocol.AddFavorite, 42), out _, out result);
        GarageCodec.TryApply(garage, Change(7, GarageProtocol.AddFavorite, 42), out _, out GarageResult repeated);
        Assert(result == GarageResult.Saved && repeated == GarageResult.Saved && garage.Favorites.SequenceEqual(new[] { 42 }), "adding a favourite twice keeps one");
        GarageCodec.TryApply(garage, Change(8, GarageProtocol.RemoveFavorite, 42), out _, out result);
        Assert(result == GarageResult.Saved && garage.Favorites.Count == 0, "a favourite is removed");
        for (int i = 0; i < GarageCodec.MaximumPresets; i++) garage.Vehicles.Add(Preset("Машина " + i));
        GarageCodec.TryApply(garage, Change(9, GarageProtocol.SavePreset, "Лишняя", appearance), out _, out result);
        Assert(result == GarageResult.Full && garage.Vehicles.Count == GarageCodec.MaximumPresets, "a full garage refuses one more car");
        Assert(!GarageCodec.TryApply(garage, new object[] { "x", 1 }, out _, out _) && !GarageCodec.TryApply(garage, new object[] { 1 }, out _, out _),
            "a malformed request is ignored without a reply");
        GarageCodec.TryApply(garage, Change(10, 99, "x"), out token, out result);
        Assert(token == 10 && result == GarageResult.Invalid, "an unknown operation is answered as invalid");

        var target = new GarageData { Vehicles = { Preset("Общая") }, Favorites = { 1 } };
        var source = new GarageData { Vehicles = { Preset("общая", 5), Preset("Новая") }, Favorites = { 1, 2 } };
        GarageCodec.TryApply(target, Change(11, GarageProtocol.Import, GarageCodec.Encode(source)), out _, out result);
        Assert(result == GarageResult.Saved && target.Vehicles.Select(preset => preset.Name).SequenceEqual(new[] { "Общая", "Новая" }) &&
            target.Vehicles[0].Appearance.ModelHash != 5 && target.Favorites.SequenceEqual(new[] { 1, 2 }),
            "moving a computer's garage adds new names and favourites and keeps the server's own cars");
        var full = new GarageData { Vehicles = Enumerable.Range(0, GarageCodec.MaximumPresets).Select(i => Preset("Есть " + i)).ToList() };
        Assert(!GarageCodec.Merge(full, source) && full.Vehicles.Count == GarageCodec.MaximumPresets && full.Favorites.SequenceEqual(new[] { 1, 2 }),
            "a merge that does not fit reports it and keeps what fitted");
    }

    private static void CheckServerStore(string folder)
    {
        var store = new GarageStore(folder);
        Assert(store.Load("Rider").Vehicles.Count == 0, "a player without a garage starts with an empty one");
        store.Save("Rider", new GarageData { Vehicles = { Preset("Ламбо") }, Favorites = { 3 } });
        Assert(store.Load("rider").Vehicles.Single().Name == "Ламбо" && store.Load("RiDeR").Favorites.SequenceEqual(new[] { 3 }),
            "the garage follows the RageCoop name regardless of letter case");
        Assert(store.Load("Pilot").Vehicles.Count == 0, "another player's garage is separate");
        Assert(PlayerFiles.Key("Rider") == "rider" && PlayerFiles.Key("../x").StartsWith("user-", StringComparison.Ordinal) &&
            PlayerFiles.Key(new string('a', 40)).StartsWith("user-", StringComparison.Ordinal), "unusual names become a safe file name");
        Assert(!Directory.EnumerateFiles(Path.Combine(folder, "garages"), "*.tmp").Any(), "saving leaves no temporary files");

        string import = Path.Combine(folder, "garages", "import");
        Directory.CreateDirectory(import);
        File.WriteAllText(Path.Combine(import, "friend.vehicle-presets.xml"),
            "<SavedVehicles version=\"1\"><Vehicles><Vehicle><Name>Урус</Name><Appearance><ModelHash>77</ModelHash></Appearance></Vehicle></Vehicles></SavedVehicles>");
        Assert(store.Load("Friend").Vehicles.Single().Name == "Урус" && File.Exists(Path.Combine(import, "friend.vehicle-presets.xml.imported")) &&
            !File.Exists(Path.Combine(import, "friend.vehicle-presets.xml")), "a former garage placed for import becomes the player's garage once");
        Assert(store.Load("friend").Vehicles.Count == 1, "the imported garage is then read from the server file");

        string broken = Path.Combine(folder, "garages", "broken.xml");
        File.WriteAllText(broken, "<Garage");
        MustReject(() => store.Load("broken"), "an unreadable garage is reported");
        Assert(File.ReadAllText(broken) == "<Garage", "an unreadable garage file is left untouched");
    }

    private static void CheckClient(string folder)
    {
        Directory.CreateDirectory(folder);
        var server = new GarageData();
        GarageClient? client = null;
        var sent = new List<object[]>();
        client = new GarageClient((hash, args) =>
        {
            sent.Add(args);
            if (hash == GarageProtocol.Change && GarageCodec.TryApply(server, args, out int token, out GarageResult result))
            {
                client!.ApplyResult(new object[] { token, (int)result });
                if (result == GarageResult.Saved) client.ApplyState(new object[] { GarageCodec.Encode(server) });
            }
        });
        int changes = 0;
        client.Changed += () => changes++;
        client.Request();
        Assert(sent.Single().Length == 0 && client.Current == null, "the client asks the server for the garage and shows nothing until it arrives");
        Assert(!client.ApplyState(new object[] { "<Garage" }) && !client.ApplyState(new object[] { 1 }) && client.Current == null,
            "a malformed state message is ignored");
        client.ApplyState(new object[] { GarageCodec.Encode(server) });
        Assert(client.Current != null && changes == 1, "the server's garage becomes the menu's garage");
        GarageResult? outcome = null;
        client.SavePreset("Раптик", Appearance(), result => outcome = result);
        Assert(outcome == GarageResult.Saved && client.Current!.Vehicles.Single().Name == "Раптик" && changes == 2,
            "saving goes to the server and the menu follows its answer");
        client.SavePreset("раптик", Appearance(), result => outcome = result);
        Assert(outcome == GarageResult.DuplicateName && client.Current!.Vehicles.Count == 1, "the server's refusal reaches the caller");
        Assert(!client.ApplyResult(new object[] { 999, 0 }) && !client.ApplyResult(new object[] { 1, 77 }), "unknown answers are ignored");

        File.WriteAllText(Path.Combine(folder, GarageClient.LegacyPresetsFile),
            "<SavedVehicles version=\"1\"><Vehicles><Vehicle><Name>Старая</Name><Appearance><ModelHash>9</ModelHash></Appearance></Vehicle></Vehicles></SavedVehicles>");
        File.WriteAllText(Path.Combine(folder, GarageClient.LegacyFavoritesFile), "<FavoriteVehicles version=\"1\"><Models><Model>9</Model></Models></FavoriteVehicles>");
        bool? moved = null;
        Assert(client.ImportLocal(folder, value => moved = value) && moved == true &&
            server.Vehicles.Select(preset => preset.Name).SequenceEqual(new[] { "Раптик", "Старая" }) && server.Favorites.SequenceEqual(new[] { 9 }) &&
            !File.Exists(Path.Combine(folder, GarageClient.LegacyPresetsFile)) && !File.Exists(Path.Combine(folder, GarageClient.LegacyFavoritesFile)),
            "this computer's former garage moves to the server and its files are removed only afterwards");
        Assert(!client.ImportLocal(folder, _ => { }), "with nothing left on the computer there is nothing to move");

        for (int i = server.Vehicles.Count; i < GarageCodec.MaximumPresets; i++) server.Vehicles.Add(Preset("Полный " + i));
        File.WriteAllText(Path.Combine(folder, GarageClient.LegacyPresetsFile),
            "<SavedVehicles version=\"1\"><Vehicles><Vehicle><Name>Не влезла</Name><Appearance><ModelHash>9</ModelHash></Appearance></Vehicle></Vehicles></SavedVehicles>");
        moved = null;
        client.ImportLocal(folder, value => moved = value);
        Assert(moved == false && File.Exists(Path.Combine(folder, GarageClient.LegacyPresetsFile)),
            "a garage that does not fit on the server keeps its files on the computer");
    }

    // своё жильё: ключ каталога по имени RageCoop рядом с гаражом
    private static void CheckHomes(string folder)
    {
        Assert(HomeCodec.IsKey(string.Empty) && HomeCodec.IsKey("eclipse_penthouse_1") && !HomeCodec.IsKey("Eclipse") && !HomeCodec.IsKey("a/b") &&
            !HomeCodec.IsKey(new string('a', HomeCodec.MaximumKeyLength + 1)) && !HomeCodec.IsKey(null),
            "a home is no choice or a catalog key of small letters, digits and underscores");
        var store = new HomeStore(folder);
        Assert(store.Load("Rider") == string.Empty, "a player who chose no home has none");
        store.Save("Rider", "wild_oats_house");
        string file = Path.Combine(folder, "homes", "rider.xml");
        Assert(store.Load("RIDER") == "wild_oats_house" && File.Exists(file), "the home follows the RageCoop name regardless of letter case, next to the garage");
        store.Save("Rider", string.Empty);
        Assert(store.Load("Rider") == string.Empty, "choosing no home is kept as well");
        MustReject(() => store.Save("Rider", "<Home>x</Home>"), "a key with markup is refused");
        File.WriteAllText(file, "<!DOCTYPE Home [<!ENTITY x \"y\">]><Home>&x;</Home>");
        MustReject(() => store.Load("Rider"), "a document type in the file is refused");
        File.WriteAllText(file, "<Home>Wild Oats</Home>");
        MustReject(() => store.Load("Rider"), "a file with an unknown key is refused, not replaced");
        File.WriteAllText(file, new string('a', 300));
        MustReject(() => store.Load("Rider"), "an oversized file is refused");
    }

    private static void MustReject(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { Pass(label); return; }
        throw new InvalidOperationException("Accepted invalid input: " + label);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Pass(message);
    }

    private static void Pass(string message)
    {
        _checks++;
        Console.WriteLine("PASS " + message);
    }
}
