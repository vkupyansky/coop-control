using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopAdmin.Server;
using CoopControl.Shared;
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
            Console.WriteLine($"Garage checks passed: {_checks}. Shared rules, server files and the client run as built; the network is a direct call.");
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
        Assert(GarageStore.Key("Rider") == "rider" && GarageStore.Key("../x").StartsWith("user-", StringComparison.Ordinal) &&
            GarageStore.Key(new string('a', 40)).StartsWith("user-", StringComparison.Ordinal), "unusual names become a safe file name");
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
