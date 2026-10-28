using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Resources;
using System.Text.RegularExpressions;
using GTA.UI;
using HarmonyLib;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly Resource = null!;
    private static Type Text = null!;
    private static Type MenuBinding = null!;
    private static Type Language = null!;
    private static int Passes;

    private static int Main()
    {
        var boundary = new Harmony("friendslegacy.check.menulocalizationboundary");
        try
        {
            Resource = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RageCoopAdmin.Client.dll"));
            Text = Resource.GetType("RageCoopAdmin.Client.UiText", true)!;
            MenuBinding = Resource.GetType("RageCoopAdmin.Client.UiMenu", true)!;
            Language = Resource.GetType("RageCoopAdmin.Client.UiLanguage", true)!;
            IsolateGameBoundary(boundary);
            CheckCatalog();
            CheckTextAndBindings();
            CheckGameCatalogNames();
            CheckActualMenu();
            Console.WriteLine("Menu localization checks passed: " + Passes + ". Actual compiled menu and LemonUI are used; only game/native drawing boundaries are isolated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (Text != null) SetLanguage(0);
            foreach (MethodBase method in Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(boundary.Id) == true).ToArray())
                boundary.Unpatch(method, HarmonyPatchType.All, boundary.Id);
        }
    }

    private static void IsolateGameBoundary(Harmony boundary)
    {
        boundary.Patch(typeof(API).GetProperty("IsOnServer")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(NotOnServer)));
        boundary.Patch(typeof(Screen).GetProperty("Resolution")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(ScreenResolution)));
        boundary.Patch(typeof(GTA.Model).GetProperty("IsInCdImage")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(MissingModel)));
        boundary.Patch(Resource.GetType("RageCoopAdmin.Client.AdminMenuScript", true)!.GetMethod("BuildWeaponsMenu", Instance)!,
            transpiler: new HarmonyMethod(typeof(Program), nameof(IsolateWeaponDiscovery)));
        Type safeZone = typeof(NativeMenu).Assembly.GetType("LemonUI.Tools.SafeZone", true)!;
        boundary.Patch(safeZone.GetProperty("Size", Static)!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(SafeZoneSize)));
        foreach (string name in new[] { "Hour", "Minute" })
            boundary.Patch(typeof(GTA.Chrono.GameClock).GetProperty(name)!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(Clock)));
        foreach (string name in new[] { "UpdateItems", "Recalculate" })
            foreach (MethodInfo method in typeof(NativeMenu).GetMethods(Instance).Where(method => method.Name == name))
                boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        foreach (Type element in new[] { typeof(LemonUI.Elements.ScaledText), typeof(LemonUI.Elements.ScaledTexture), typeof(LemonUI.Elements.ScaledRectangle) })
            boundary.Patch(element.GetMethod("Recalculate", Instance, null, Type.EmptyTypes, null)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        boundary.Patch(typeof(LemonUI.Elements.ScaledTexture).GetMethod("Request", Instance)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        boundary.Patch(typeof(LemonUI.Elements.ScaledText).GetProperty("Width")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(TextWidth)));
        Type extensions = typeof(NativeMenu).Assembly.GetType("LemonUI.Tools.Extensions", true)!;
        boundary.Patch(extensions.GetMethod("ToRelative", Static, null, new[] { typeof(System.Drawing.PointF) }, null)!,
            prefix: new HarmonyMethod(typeof(Program), nameof(UiPoint)));
        boundary.Patch(typeof(LemonUI.Scaleform.InstructionalButton).GetConstructor(new[] { typeof(string), typeof(GTA.Control) })!,
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        foreach (string name in new[] { "Add", "Clear" })
            boundary.Patch(typeof(LemonUI.Scaleform.InstructionalButtons).GetMethod(name, Instance)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        boundary.Patch(typeof(LemonUI.Scaleform.BaseScaleform).GetConstructor(Instance, null, new[] { typeof(string) }, null)!,
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
        foreach (MethodInfo notification in typeof(Notification).GetMethods().Where(method => method.Name == "PostTicker"))
            boundary.Patch(notification, prefix: new HarmonyMethod(typeof(Program), nameof(SkipBoundary)));
    }

    private static void CheckCatalog()
    {
        var manager = new ResourceManager("RageCoopAdmin.Client.Assets.MenuEnglish", Resource);
        ResourceSet entries = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        var translations = entries.Cast<DictionaryEntry>().ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value!, StringComparer.Ordinal);
        Check(translations.Count > 200, "the actual built DLL embeds the complete English resource catalog");
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sources", "Client"), "*.cs"))
        {
            foreach (Match literal in Regex.Matches(File.ReadAllText(file), "UiText\\.(?:Get|Format)\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string key = DecodeLiteral(literal.Groups[1].Value);
                if (Regex.IsMatch(key, "[А-Яа-яЁё]") && !translations.ContainsKey(key)) missing.Add(key);
            }
        }
        foreach (string file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sources", "Server"), "*.cs"))
        {
            string code = File.ReadAllText(file);
            foreach (Match literal in Regex.Matches(code, "(?:(?:SendStatus|SendVehicleSpawnFailure|SendRecallFailure)\\([^;\\n]*|error\\s*=\\s*)\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string key = DecodeLiteral(literal.Groups[1].Value);
                if (Regex.IsMatch(key, "[А-Яа-яЁё]") && !translations.ContainsKey(key)) missing.Add(key);
            }
        }
        Check(missing.Count == 0, "all explicit localized UI keys and server status templates exist in the compiled catalog: " + string.Join(" | ", missing));
        foreach (KeyValuePair<string, string> entry in translations)
        {
            string[] sourceSlots = Slots(entry.Key, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}");
            string[] translatedSlots = Slots(entry.Value, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}");
            if (!sourceSlots.SequenceEqual(translatedSlots)) throw new InvalidOperationException("Composite format slots changed: " + entry.Key);
            if (!Slots(entry.Key, "~[^~]+~").SequenceEqual(Slots(entry.Value, "~[^~]+~")))
                throw new InvalidOperationException("GTA formatting codes changed: " + entry.Key);
        }
        Check(true, "every compiled translation preserves composite format slots and GTA formatting codes");
    }

    private static void CheckTextAndBindings()
    {
        SetLanguage(0);
        Check(Translate("Транспорт") == "Транспорт", "Russian rendering uses the original source text");
        SetLanguage(1);
        Check(Translate("Транспорт") == "Transport", "English rendering reads the embedded catalog");
        string mixedName = "Моя машина Pз 🚗";
        Check(Translate(mixedName) == mixedName, "unknown player text is preserved without transliteration or replacement");
        string formatted = (string)Text.GetMethod("Format", Static)!.Invoke(null,
            new object[] { "~o~Машина:~s~ {0}", new object[] { mixedName } })!;
        Check(formatted == "~o~Vehicle:~s~ " + mixedName, "localized notification templates preserve inserted player text and formatting codes");
        // Deliberately collide with a translated UI key: owning this text as a
        // player name must keep it raw even though Get would translate the key.
        string custom = "Транспорт";
        var item = (NativeListItem<string>)MenuBinding.GetMethod("List", Static)!.Invoke(null, new object?[] {
            new Func<string>(() => custom), new Func<string>(() => Translate("Модель вместе с цветом, дисками и тюнингом. Избранное хранит только модель.")),
            new Func<string[]>(() => new[] { Translate("Создать"), Translate("Удалить") }), null })!;
        var menu = new NativeMenu("Check", "Check");
        menu.Add(item);
        item.SelectedIndex = 1;
        Refresh(new[] { menu });
        Check(item.Items.SequenceEqual(new[] { "Spawn", "Delete" }) && item.Description.StartsWith("Save the model", StringComparison.Ordinal),
            "bindings created while English is selected render list actions and descriptions in English");
        SetLanguage(0);
        Refresh(new[] { menu });
        Check(item.Items.SequenceEqual(new[] { "Создать", "Удалить" }) && item.SelectedIndex == 1 && item.Title == custom,
            "an English-created binding returns to Russian without resetting its action or custom title");
        SetLanguage(1);
        Refresh(new[] { menu });
        Check(item.Items.SequenceEqual(new[] { "Spawn", "Delete" }) && item.SelectedIndex == 1 && item.Title == custom,
            "repeated language changes preserve list selection and custom names");
    }

    private static void CheckGameCatalogNames()
    {
        Type names = Resource.GetType("RageCoopAdmin.Client.GameCatalogNames", true)!;
        string Weapon(string identifier) => (string)names.GetMethod("Weapon", Static)!.Invoke(null,
            new[] { Enum.Parse(typeof(GTA.WeaponHash), identifier), "Русское имя" })!;
        Check(Weapon("APPistol") == "AP Pistol" && Weapon("PistolMk2") == "Pistol Mk II",
            "actual SDK weapon identifiers produce readable AP and Mk II names");
        object model = Enum.GetValues(typeof(GTA.VehicleHash)).Cast<object>().First(value => Convert.ToUInt64(value, CultureInfo.InvariantCulture) > int.MaxValue);
        int hash = unchecked((int)Convert.ToUInt32(model, CultureInfo.InvariantCulture));
        string translated = (string)names.GetMethod("Vehicle", Static)!.Invoke(null, new object[] { hash, "Машина" })!;
        Check(translated != "Машина" && translated.Length > 0, "a high-bit vehicle hash uses the supported SDK enum without a signedness exception");
        string authored = (string)names.GetMethod("Vehicle", Static)!.Invoke(null, new object[] { hash, "BMW M5 Competition" })!;
        string unknown = (string)names.GetMethod("Vehicle", Static)!.Invoke(null, new object[] { 0, "Моя модель" })!;
        Check(authored == "BMW M5 Competition" && unknown == "Моя модель", "add-on authors' Latin names and unknown model names remain unchanged");
    }

    private static void CheckActualMenu()
    {
        SetLanguage(0);
        Type scriptType = Resource.GetType("RageCoopAdmin.Client.AdminMenuScript", true)!;
        object script = Activator.CreateInstance(scriptType)!;
        object Get(string name) => scriptType.GetField(name, Instance)!.GetValue(script)!;
        void Set(string name, object value) => scriptType.GetField(name, Instance)!.SetValue(script, value);
        object? Call(string name, params object[] args) => scriptType.GetMethod(name, Instance)!.Invoke(script, args);
        // Avoid asking GTA whether optional add-on models are installed. Their
        // data and native availability are independent of translating the menu.
        Type addon = Resource.GetType("RageCoopAdmin.Client.AddonVehicleDefinition", true)!;
        Set("_addonVehicles", Array.CreateInstance(addon, 0));
        Call("BuildMenus");
        NativeMenu[] menus = ((IEnumerable)Get("_pool")).Cast<object>().OfType<NativeMenu>().ToArray();
        NativeMenu root = (NativeMenu)Get("_mainMenu");
        string[] russianRoot = root.Items.Select(item => item.Title).ToArray();
        Check(russianRoot.SequenceEqual(new[] { "Поделиться меткой", "Транспорт", "Телепорты", "Напарники", "Персонаж", "Оружие", "Мир", "Настройки" }),
            "the actual assembled Russian root retains the approved eight sections and order");
        NativeCheckboxItem ghost = (NativeCheckboxItem)Get("_ghostTrafficItem");
        NativeCheckboxItem clock = (NativeCheckboxItem)Get("_freezeTimeItem");
        NativeCheckboxItem weather = (NativeCheckboxItem)Get("_freezeWeatherItem");
        Type controllerType = Resource.GetType("RageCoopAdmin.Client.WorldStateController", true)!;
        Type snapshotType = Resource.GetType("OGCoop.Shared.WorldStateSnapshot", true)!;
        object controller = Activator.CreateInstance(controllerType, Instance, null,
            new object[] { new Action<int, object[]>((_, _) => throw new InvalidOperationException("Changing the display language must not send a world request.")) }, null)!;
        object snapshot = Activator.CreateInstance(snapshotType, Instance, null,
            new object[] { new string('a', 32), 1L, true, true, true, 9, 8, 0, 0 }, null)!;
        controllerType.GetProperty("Snapshot", Instance)!.SetValue(controller, snapshot);
        Set("_worldState", controller);
        Set("_worldConnected", true);
        Call("RefreshWorldControls");
        Check(ghost.Checked && clock.Checked && weather.Checked, "the actual menu fixture starts with all three confirmed shared modes enabled");
        NativeListItem<string> wanted = (NativeListItem<string>)Get("_wantedItem");
        wanted.SelectedIndex = 4;
        NativeMenu settings = menus.Single(menu => menu.Name == "Настройки");
        NativeListItem<string> language = settings.Items.OfType<NativeListItem<string>>().Single(item => item.Title.Contains("Language"));
        // The menu shows the garage the server sent, and an empty one is enough here.
        Type garageType = Resource.GetType("RageCoopAdmin.Client.GarageClient", true)!;
        object garage = Activator.CreateInstance(garageType, Instance, null, new object[] { new Action<int, object[]>((_, _) => { }) }, null)!;
        string emptyGarage = (string)Resource.GetType("OGCoop.Shared.GarageCodec", true)!.GetMethod("Encode", Static)!
            .Invoke(null, new[] { Activator.CreateInstance(Resource.GetType("OGCoop.Shared.GarageData", true)!)! })!;
        garageType.GetMethod("ApplyState", Instance)!.Invoke(garage, new object[] { new object[] { emptyGarage } });
        Set("_garage", garage);
        ((HashSet<int>)Get("_favoriteHashes")).Add(0x7f00123);
        Set("_catalogReady", true);
        Call("RefreshFavoritesMenu");
        var missingFavorite = ((NativeMenu)Get("_favoritesMenu")).Items.OfType<NativeListItem<string>>().Single();
        Check(missingFavorite.Title == "Модель недоступна", "an actual favorite without catalog or add-on metadata starts with the Russian fallback title");
        root.SelectedIndex = 6;
        object input = CreateInputFixture(scriptType, script, custom: "Моя Pз");
        object settingsValue = Get("_interfaceSettings");
        bool[] beforeSettings = settingsValue.GetType().GetProperties(Instance).Where(property => property.PropertyType == typeof(bool)).Select(property => (bool)property.GetValue(settingsValue)!).ToArray();
        language.SelectedIndex = 1;
        RaiseListChange(language);
        Check(root.Items.Select(item => item.Title).SequenceEqual(new[] { "Share waypoint", "Transport", "Teleports", "Partners", "Character", "Weapons", "World", "Settings" }),
            "the actual Settings language action translates every root section in place");
        Check(language.Title == "Язык / Language" && language.Items.SequenceEqual(new[] { "Русский", "English" }) &&
            language.Description == "Menu language. Your vehicle names and text input language stay the same.",
            "the language selector keeps its bilingual label and options while translating its description into English");
        Check(missingFavorite.Title == "Model unavailable", "a missing favorite's resource-owned fallback title changes to English in place");
        Check(root.SelectedIndex == 6 && wanted.SelectedIndex == 4 && ghost.Checked && clock.Checked && weather.Checked,
            "translation preserves current menu selection, wanted selection and all confirmed world flags (root=" + root.SelectedIndex + ", wanted=" + wanted.SelectedIndex +
            ", traffic=" + ghost.Checked + ", time=" + clock.Checked + ", weather=" + weather.Checked + ")");
        Check(!((NativeMenu)Get("_mainMenu")).Items.Any(item => item.Title.Contains("~")), "translated root titles contain no leaked GTA formatting tags");
        CheckInputUnchanged(input);
        bool[] afterSettings = settingsValue.GetType().GetProperties(Instance).Where(property => property.PropertyType == typeof(bool)).Select(property => (bool)property.GetValue(settingsValue)!).ToArray();
        Check(beforeSettings.SequenceEqual(afterSettings) && scriptType.GetField("_interfaceStore", Instance)!.GetValue(script) == null,
            "local language selection does not alter other preferences or require a settings store in this fixture");
        language.SelectedIndex = 0;
        RaiseListChange(language);
        Check(root.Items.Select(item => item.Title).SequenceEqual(russianRoot) && root.SelectedIndex == 6 && wanted.SelectedIndex == 4,
            "the actual Settings action returns the same menu objects to Russian with selections preserved");
        Check(missingFavorite.Title == "Модель недоступна" && language.Description == "Язык меню. Названия ваших машин и язык ввода текста не меняются.",
            "the missing favorite fallback and language selector description return to Russian");
        CheckInputUnchanged(input);
    }

    private static object CreateInputFixture(Type scriptType, object script, string custom)
    {
        Type inputType = Resource.GetType("RageCoopAdmin.Client.ResourceTextInput", true)!;
        var inputMenu = (NativeMenu)scriptType.GetMethod("CreateMenu", Static)!.Invoke(null, new object[] { "OG Coop", "Название машины" })!;
        Check(inputMenu.BannerText.Text == "OG COOP", "The banner reads OG COOP in capitals of one height, like the GTA logo.");
        object input = Activator.CreateInstance(inputType, Instance, null, new object[] { inputMenu }, null)!;
        scriptType.GetField("_textInput", Instance)!.SetValue(script, input);
        object pool = scriptType.GetField("_pool", Instance)!.GetValue(script)!;
        pool.GetType().GetMethod("Add")!.Invoke(pool, new object[] { inputMenu });
        Type bufferType = Resource.GetType("RageCoopAdmin.Client.UnicodeTextBuffer", true)!;
        object buffer = Activator.CreateInstance(bufferType, Instance, null, new object[] { 40 }, null)!;
        bufferType.GetMethod("Append", Instance)!.Invoke(buffer, new object[] { custom });
        inputType.GetField("_buffer", Instance)!.SetValue(input, buffer);
        inputType.GetMethod("UpdateInputMode", Instance)!.Invoke(input, new object[] { true });
        var alphabet = (NativeListItem<string>)inputType.GetField("_alphabet", Instance)!.GetValue(input)!;
        alphabet.SelectedIndex = 1;
        RaiseListChange(alphabet);
        inputType.GetMethod("RefreshValue", Instance)!.Invoke(input, Array.Empty<object>());
        return input;
    }

    private static void CheckInputUnchanged(object input)
    {
        Type type = input.GetType();
        var value = (NativeItem)type.GetField("_value", Instance)!.GetValue(input)!;
        var alphabet = (NativeListItem<string>)type.GetField("_alphabet", Instance)!.GetValue(input)!;
        var letter = (NativeListItem<string>)type.GetField("_letter", Instance)!.GetValue(input)!;
        Check(value.Title == "Моя Pз" && alphabet.SelectedIndex == 1 && letter.Items.SequenceEqual("abcdefghijklmnopqrstuvwxyz".Select(character => character.ToString())),
            "menu translation preserves entered mixed-language text, selected input alphabet and alphabet characters");
    }

    private static void RaiseListChange(NativeListItem<string> item)
    {
        // LemonUI's programmatic SelectedIndex does not fire ItemChanged. Use
        // the real event delegate, as input would, without exercising old input.
        FieldInfo eventField = typeof(NativeListItem<string>).GetField("ItemChanged", Instance)!;
        if (eventField.GetValue(item) is not Delegate changed) return;
        ConstructorInfo constructor = typeof(ItemChangedEventArgs<string>).GetConstructors(Instance).Single();
        object[] values = constructor.GetParameters().Select(parameter => parameter.ParameterType == typeof(int) ? (object)item.SelectedIndex
            : parameter.ParameterType == typeof(string) ? item.SelectedItem
            : parameter.ParameterType == typeof(NativeListItem<string>) ? item
            : parameter.ParameterType == typeof(Direction) ? Enum.Parse(typeof(Direction), "Right")
            : throw new InvalidOperationException("Unexpected LemonUI list event parameter: " + parameter.ParameterType)).ToArray();
        changed.DynamicInvoke(item, constructor.Invoke(values));
    }

    private static string[] Slots(string value, string pattern) => Regex.Matches(value, pattern).Cast<Match>().Select(match => match.Value).OrderBy(slot => slot, StringComparer.Ordinal).ToArray();
    private static string DecodeLiteral(string value) => Regex.Unescape(value);
    private static string Translate(string value) => (string)Text.GetMethod("Get", Static)!.Invoke(null, new object[] { value })!;
    private static void SetLanguage(int language) => Text.GetMethod("SetLanguage", Static)!.Invoke(null, new[] { Enum.ToObject(Language, language) });
    private static void Refresh(NativeMenu[] menus) => MenuBinding.GetMethod("Refresh", Static)!.Invoke(null, new object[] { menus });
    private static bool NotOnServer(ref bool __result) { __result = false; return false; }
    private static bool MissingModel(ref bool __result) { __result = false; return false; }
    private static IEnumerable<CodeInstruction> IsolateWeaponDiscovery(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(GTA.Weapon) && method.Name == "GetAllWeaponHashesForHumanPeds")
            {
                if (method.ReturnType != typeof(GTA.WeaponHash[]) || method.GetParameters().Length != 0)
                    throw new InvalidOperationException("The supported SDK weapon discovery boundary changed.");
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(Program).GetMethod(nameof(EmptyWeaponHashes), Static)!;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected exactly one native weapon-discovery boundary.");
    }
    private static GTA.WeaponHash[] EmptyWeaponHashes() => Array.Empty<GTA.WeaponHash>();
    private static bool Clock(ref int __result) { __result = 12; return false; }
    private static bool ScreenResolution(ref System.Drawing.Size __result) { __result = new System.Drawing.Size(1920, 1080); return false; }
    private static bool SafeZoneSize(ref float __result) { __result = 1f; return false; }
    private static bool SkipBoundary() => false;
    private static bool TextWidth(ref float __result) { __result = 0.1f; return false; }
    private static bool UiPoint(System.Drawing.PointF __0, ref System.Drawing.PointF __result) { __result = __0; return false; }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Passes++; Console.WriteLine("PASS " + message); }
}
