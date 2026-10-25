using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LemonUI.Menus;
using RageCoopAdmin.Client;

internal static class Program
{
    private static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CoopAddonVehiclesCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckEmptyCatalog();
            CheckPublicMenu(directory);
            Console.WriteLine("Add-on vehicle checks passed (GTA and menu boundaries are isolated)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CheckEmptyCatalog()
    {
        const string json = "{\"version\":1,\"vehicles\":[]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert(AddonVehicleCatalog.Load(stream).Count == 0, "a valid empty catalog requires no vehicle DLC");
        MustReject(json, document => document["version"] = 2, "an empty catalog still requires a supported version");
        MustReject(json, document => document["vehicles"] = null, "a null vehicle list is not an empty catalog");
        MustReject(json, document => document.AsObject().Remove("vehicles"), "a missing vehicle list is rejected");
    }

    private static void CheckPublicMenu(string directory)
    {
        Assert(AddonVehicleCatalog.LoadEmbedded().Count == 0, "the public build embeds only the empty catalog");
        GameBoundary.Reset();
        var menu = new AdminMenuScript();
        menu.Start(directory);
        Assert(!menu.HasAddonMenu && menu.RegisteredMenuCount == 1 && menu.FavoriteRows.Count > 0,
            "an empty catalog omits the add-on submenu and retains favorites");
        const int stockHash = 12345;
        GameBoundary.Installed.Add(stockHash);
        Assert(menu.CanSpawn(stockHash), "stock vehicles remain available without an add-on catalog");
        menu.Toggle(stockHash);
        var reopened = new AdminMenuScript();
        reopened.Start(directory);
        Assert(reopened.HasFavorite(stockHash), "a stock vehicle favorite survives reopening the public menu");
    }


    private static void MustReject(string json, Action<JsonObject> change, string label)
    {
        JsonObject document = JsonNode.Parse(json)!.AsObject();
        change(document);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
        bool rejected = false;
        try { AddonVehicleCatalog.Load(stream); }
        catch (Exception ex) when (ex is InvalidDataException || ex is SerializationException) { rejected = true; }
        Assert(rejected, label);
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition) { throw new InvalidOperationException(label); }
        Console.WriteLine("PASS " + label);
    }
}
