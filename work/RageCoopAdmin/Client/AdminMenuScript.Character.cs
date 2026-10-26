using System;
using System.Collections.Generic;
using System.Linq;
using OGCoop.Shared;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using LemonUI.Menus;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// The player's character: body, face, hair, clothes and tattoos. The server keeps it
// per RageCoop name; it is put on at every join and again whenever the game
// gives the player another body (after death, say). Without a saved character
// the editor opens by itself once the map has loaded; «Позже» offers it again
// at the next join. J → Персонаж → Внешность changes it.
public sealed partial class AdminMenuScript
{
    private const long CharacterCheckMilliseconds = 500;
    private const long ModelLoadLimit = 10_000;
    private const long CharacterSaveLimit = 10_000;
    private const int CreatorFade = 300;
    private const long CreatorRoomLimit = 5_000;
    private const long CreatorReturnLimit = 10_000;
    private const int FranklinModel = unchecked((int)0x9B22DBAF); // player_one
    // The room GTA Online makes characters in, under the police station.
    private static readonly Vector3 CreatorRoom = new Vector3(402.86f, -996.41f, -99.0f);
    private const float CreatorRoomHeading = 180f;
    // The story hero the player had before becoming an Online character.
    private static int _storyModel;

    private static readonly (int Slot, bool Prop, string Name)[] CreatorSlots =
    {
        (2, false, "Причёска"), (11, false, "Верх"), (8, false, "Под верхом"), (3, false, "Руки"), (4, false, "Низ"),
        (6, false, "Обувь"), (1, false, "Маска"), (7, false, "Шея и украшения"), (5, false, "Сумка"),
        (9, false, "Бронежилет"), (10, false, "Нашивки"),
        (0, true, "Головной убор"), (1, true, "Очки"), (2, true, "Серьги"), (6, true, "Часы"), (7, true, "Браслет"),
    };
    private static readonly string[] CreatorBodies = { "Мужчина", "Женщина", "Сюжетный герой" };
    private static readonly string[] CreatorViews = { "Во весь рост", "Лицо", "Ноги" };
    // Parents as GTA Online's creator names them, with their head blend faces.
    private static readonly (int Id, string Name)[] CreatorMothers =
    {
        (21, "Hannah"), (22, "Audrey"), (23, "Jasmine"), (24, "Giselle"), (25, "Amelia"), (26, "Isabella"), (27, "Zoe"), (28, "Ava"),
        (29, "Camila"), (30, "Violet"), (31, "Sophia"), (32, "Evelyn"), (33, "Nicole"), (34, "Ashley"), (35, "Grace"), (36, "Brianna"),
        (37, "Natalie"), (38, "Olivia"), (39, "Elizabeth"), (40, "Charlotte"), (41, "Emma"), (45, "Misty"),
    };
    private static readonly (int Id, string Name)[] CreatorFathers =
    {
        (0, "Benjamin"), (1, "Daniel"), (2, "Joshua"), (3, "Noah"), (4, "Andrew"), (5, "Juan"), (6, "Alex"), (7, "Isaac"), (8, "Evan"),
        (9, "Ethan"), (10, "Vincent"), (11, "Angel"), (12, "Diego"), (13, "Adrian"), (14, "Gabriel"), (15, "Michael"), (16, "Santiago"),
        (17, "Kevin"), (18, "Louis"), (19, "Samuel"), (20, "Anthony"), (42, "Claude"), (43, "Niko"), (44, "John"),
    };
    // GTA's face features 0–19 in order.
    private static readonly string[] CreatorFeatures =
    {
        "Ширина носа", "Высота кончика носа", "Длина кончика носа", "Высота переносицы", "Опущенность кончика носа", "Изгиб переносицы",
        "Высота бровей", "Глубина бровей", "Высота скул", "Ширина скул", "Ширина щёк", "Разрез глаз", "Толщина губ",
        "Ширина челюсти", "Форма челюсти", "Высота подбородка", "Длина подбородка", "Ширина подбородка", "Ямочка на подбородке",
        "Толщина шеи",
    };
    private static readonly string[] CreatorEyes =
    {
        "Зелёные", "Изумрудные", "Светло-голубые", "Синие", "Светло-карие", "Тёмно-карие", "Ореховые", "Тёмно-серые",
        "Светло-серые", "Розовые", "Жёлтые", "Фиолетовые", "Чёрные", "Оттенки серого", "Текила санрайз", "Атомные", "Искажённые",
        "И-Кола", "Космические", "Инь-ян", "Мишень", "Ящериные", "Драконьи", "Инопланетные", "Козьи", "Смайлики", "Одержимые",
        "Демонические", "Заражённые", "Чужие", "Нежить", "Зомби",
    };
    // Head overlays in the order GTA Online's creator lists them.
    private static readonly (int Slot, string Name)[] CreatorOverlays =
    {
        (2, "Брови"), (1, "Борода"), (10, "Волосы на груди"), (4, "Макияж"), (5, "Румяна"), (8, "Помада"),
        (0, "Пятна на коже"), (3, "Старение"), (6, "Цвет лица"), (7, "Повреждения от солнца"), (9, "Родинки и веснушки"),
    };

    private CharacterLooks? _characters;
    private HeroLink? _heroLink;
    private CreatorSession? _creator;
    private ModelChange? _modelChange;
    private NativeMenu? _creatorMenu;
    private NativeMenu? _creatorHeritageMenu;
    private NativeMenu? _creatorFaceMenu;
    private NativeMenu? _creatorLookMenu;
    private NativeMenu? _creatorClothesMenu;
    private NativeMenu? _creatorTattooMenu;
    private NativeMenu[] _creatorTattooZones = Array.Empty<NativeMenu>();
    private NativeSubmenuItem? _creatorTattooEntry;
    private List<(NativeCheckboxItem Item, TattooOption Option)> _creatorTattooItems = new List<(NativeCheckboxItem, TattooOption)>();
    // The body the tattoo lists were made for; 0 for none.
    private int _creatorTattooModel;
    private NativeListItem<string>? _creatorBody;
    private NativeListItem<string>? _creatorView;
    private NativeListItem<string>? _creatorTurn;
    private NativeItem? _creatorRandom;
    private NativeItem? _creatorDone;
    private NativeItem? _creatorLater;
    private List<NativeListItem<string>> _creatorParts = new List<NativeListItem<string>>();
    private List<HeadRow> _creatorHeadRows = new List<HeadRow>();
    private bool _creatorUpdating;
    private bool _creatorOffered;
    private bool _ownLookApplied;
    private bool _clothesRestoreLogged;
    private long _lastCharacterCheck;
    private const int ParachuteBagSlot = 5;

    private void RegisterCharacterEvents()
    {
        _eventSubscriptions.Register(CharacterProtocol.State, e => QueueCharacterEvent(e, CharacterProtocol.State));
        _eventSubscriptions.Register(CharacterProtocol.Saved, e => QueueCharacterEvent(e, CharacterProtocol.Saved));
        _eventSubscriptions.Register(CharacterProtocol.Look, e => QueueCharacterEvent(e, CharacterProtocol.Look));
    }

    private void InitializeCharacters()
    {
        _characters = new CharacterLooks((hash, args) => API.SendCustomEvent(hash, args));
        _heroLink = new HeroLink(message => Logger.Info(message));
        _creatorOffered = false;
        _ownLookApplied = false;
        RememberStoryModel(Game.Player.Character);
        if (!CharacterReplicas.Install(username => _characters?.Of(username), Logger.Warning))
            Logger.Warning("OG Coop: other players' characters are unavailable; RageCoop clothing contract changed.");
        _characters.Request();
    }

    private void QueueCharacterEvent(CustomEventReceivedArgs e, int kind)
    {
        if (_stopping || e.Args == null) return;
        object[] args = (object[])e.Args.Clone();
        API.QueueAction(() =>
        {
            if (!_active || _stopping || _characters == null) return;
            if (kind == CharacterProtocol.State)
            {
                if (_characters.ApplyState(args)) _ownLookApplied = false;
            }
            else if (kind == CharacterProtocol.Look)
            {
                if (_characters.ApplyLook(args, out string username)) CharacterReplicas.Dress(username);
            }
            else if (args is { Length: 1 } && args[0] is bool saved)
            {
                FinishCharacterSave(saved);
            }
        });
    }

    private void BuildCharacterMenus()
    {
        if (_playerMenu == null || _pool == null) return;
        NativeItem appearance = UiMenu.Item(() => UiText.Get("Внешность"),
            () => UiText.Get("Персонаж GTA Online: лицо, волосы и одежда. Сохраняется на сервере и виден напарнику."));
        appearance.Activated += (_, _) => OpenCreator(first: false);
        _playerMenu.Add(appearance);

        _creatorMenu = CreateMenu("OG Coop", "Внешность");
        _creatorHeritageMenu = CreateMenu("OG Coop", "Наследственность");
        _creatorFaceMenu = CreateMenu("OG Coop", "Черты лица");
        _creatorLookMenu = CreateMenu("OG Coop", "Внешний вид");
        _creatorClothesMenu = CreateMenu("OG Coop", "Одежда");
        _creatorParts = new List<NativeListItem<string>>();
        _creatorHeadRows = new List<HeadRow>();
        _creatorBody = UiMenu.List(() => UiText.Get("Тело"), () => UiText.Get("Мужчина или женщина из GTA Online, или ваш сюжетный герой."),
            () => CreatorBodies.Select(UiText.Get).ToArray());
        _creatorBody.ItemChanged += (_, e) => { if (!_creatorUpdating) ChangeCreatorBody(e.Index); };
        _creatorMenu.Add(_creatorBody);
        foreach (NativeMenu section in new[] { _creatorHeritageMenu, _creatorFaceMenu, _creatorLookMenu, _creatorClothesMenu })
        {
            _creatorMenu.AddSubMenu(section);
            NativeMenu shown = section;
            // The face sections look at the face, clothes at the whole body.
            section.Shown += (_, _) => SetCreatorView(ReferenceEquals(shown, _creatorClothesMenu) ? 0 : 1);
        }
        BuildCreatorTattoos(_creatorMenu);

        BuildCreatorHeritage(_creatorHeritageMenu);
        for (int index = 0; index < CreatorFeatures.Length; index++)
        {
            int feature = index;
            AddHeadRow(_creatorFaceMenu, () => UiText.Get(CreatorFeatures[feature]), () => UiText.Get("Листайте ←/→: от −10 до +10."),
                () => Enumerable.Range(-CharacterCodec.FeatureLimit, CharacterCodec.FeatureLimit * 2 + 1).Select(Signed).ToArray(),
                head => head.Feature(feature) + CharacterCodec.FeatureLimit, (head, value) => head.SetFeature(feature, value - CharacterCodec.FeatureLimit));
        }

        _creatorRandom = UiMenu.Item(() => UiText.Get("Случайная одежда"), () => UiText.Get("Одеть персонажа случайно; дальше можно поправить по частям."));
        _creatorRandom.Activated += (_, _) => RandomizeCreator();
        _creatorClothesMenu.Add(_creatorRandom);
        foreach ((int slot, bool prop, string name) in CreatorSlots)
        {
            NativeListItem<string> part = UiMenu.List(() => UiText.Get(name), () => string.Empty, () => new[] { "1" });
            int partSlot = slot;
            bool partProp = prop;
            part.ItemChanged += (_, e) => { if (!_creatorUpdating) WearCreatorPart(part, partSlot, partProp, e.Index); };
            part.Activated += (_, _) => NextCreatorColour(part, partSlot, partProp);
            // The hairstyle sits with the hair colours, as in GTA Online.
            (slot == 2 && !prop ? _creatorLookMenu : _creatorClothesMenu).Add(part);
            _creatorParts.Add(part);
        }
        BuildCreatorLook(_creatorLookMenu);

        _creatorView = UiMenu.List(() => UiText.Get("Камера"), () => UiText.Get("Показать персонажа целиком, лицо или ноги."),
            () => CreatorViews.Select(UiText.Get).ToArray());
        _creatorView.ItemChanged += (_, _) => AimCreatorCamera();
        _creatorMenu.Add(_creatorView);
        _creatorTurn = UiMenu.List(() => UiText.Get("Поворот"), () => UiText.Get("Повернуть персонажа, чтобы рассмотреть со всех сторон."),
            () => Enumerable.Range(0, 8).Select(step => (step * 45).ToString(System.Globalization.CultureInfo.InvariantCulture) + "°").ToArray());
        _creatorTurn.ItemChanged += (_, _) => TurnCreatorPed();
        _creatorMenu.Add(_creatorTurn);
        _creatorDone = UiMenu.Item(() => UiText.Get("Готово"), () => UiText.Get("Сохранить персонажа на сервере. Он будет с вами при каждом входе."));
        _creatorDone.Activated += (_, _) => SaveCreator();
        _creatorMenu.Add(_creatorDone);
        _creatorLater = UiMenu.Item(() => UiText.Get("Отмена"), () => UiText.Get("Выйти без изменений."));
        _creatorLater.Activated += (_, _) => LeaveCreator(revert: true);
        _creatorMenu.Add(_creatorLater);
        foreach (NativeMenu menu in CreatorMenus()) _pool.Add(menu);
    }

    private void BuildCreatorHeritage(NativeMenu menu)
    {
        Func<string> heritage = () => UiText.Get("Лицо и цвет кожи складываются из лиц мамы и папы.");
        AddHeadRow(menu, () => UiText.Get("Мама"), heritage, () => CreatorMothers.Select(parent => parent.Name).ToArray(),
            head => Math.Max(0, Array.FindIndex(CreatorMothers, parent => parent.Id == head.Mother)), (head, value) => head.Mother = CreatorMothers[value].Id);
        AddHeadRow(menu, () => UiText.Get("Папа"), heritage, () => CreatorFathers.Select(parent => parent.Name).ToArray(),
            head => Math.Max(0, Array.FindIndex(CreatorFathers, parent => parent.Id == head.Father)), (head, value) => head.Father = CreatorFathers[value].Id);
        AddHeadRow(menu, () => UiText.Get("Сходство с папой"), () => UiText.Get("На кого больше похож: 0% — на маму, 100% — на папу."),
            Percents, head => head.Resemblance / 10, (head, value) => head.Resemblance = value * 10);
        AddHeadRow(menu, () => UiText.Get("Цвет кожи как у папы"), () => UiText.Get("Цвет кожи: 0% — как у мамы, 100% — как у папы."),
            Percents, head => head.SkinTone / 10, (head, value) => head.SkinTone = value * 10);
        NativeItem random = UiMenu.Item(() => UiText.Get("Случайные родители"), () => UiText.Get("Выбрать маму, папу и сходство случайно."));
        random.Activated += (_, _) => RandomizeCreatorParents();
        menu.Add(random);
    }

    // Tattoos by body part, as GTA Online's parlours sell them; the lists are
    // filled for the body when the editor shows it.
    private void BuildCreatorTattoos(NativeMenu editor)
    {
        _creatorTattooItems = new List<(NativeCheckboxItem, TattooOption)>();
        _creatorTattooModel = 0;
        _creatorTattooMenu = CreateMenu("OG Coop", "Татуировки");
        UiMenu.Description(_creatorTattooMenu, () => UiText.Get("Татуировки салонов GTA Online по частям тела."));
        _creatorTattooEntry = editor.AddSubMenu(_creatorTattooMenu);
        _creatorTattooMenu.Shown += (_, _) => SetCreatorView(0);
        _creatorTattooZones = CharacterTattoos.Zones.Select(zone => CreateMenu("OG Coop", zone)).ToArray();
        for (int zone = 0; zone < _creatorTattooZones.Length; zone++)
        {
            _creatorTattooMenu.AddSubMenu(_creatorTattooZones[zone]);
            // The head is seen up close, the legs from below, the rest whole.
            int view = zone == 1 ? 1 : zone >= 4 ? 2 : 0;
            _creatorTattooZones[zone].Shown += (_, _) => SetCreatorView(view);
        }
        NativeItem clear = UiMenu.Item(() => UiText.Get("Снять все татуировки"), () => UiText.Get("Убрать все татуировки с тела."));
        clear.Activated += (_, _) => ClearCreatorTattoos();
        _creatorTattooMenu.Add(clear);
    }

    private void BuildCreatorLook(NativeMenu menu)
    {
        AddHeadRow(menu, () => UiText.Get("Цвет волос"), () => UiText.Get("Основной цвет волос."),
            () => Numbers(CharacterNatives.HairTints()), head => head.HairColor, (head, value) => head.HairColor = value);
        AddHeadRow(menu, () => UiText.Get("Мелирование"), () => UiText.Get("Цвет прядей; такой же, как основной, — без мелирования."),
            () => Numbers(CharacterNatives.HairTints()), head => head.HairHighlight, (head, value) => head.HairHighlight = value);
        AddHeadRow(menu, () => UiText.Get("Цвет глаз"), () => UiText.Get("Листайте ←/→."),
            () => CreatorEyes.Select(UiText.Get).ToArray(), head => head.EyeColor, (head, value) => head.EyeColor = value);
        foreach ((int slot, string name) in CreatorOverlays)
        {
            int overlay = slot;
            string title = name;
            AddHeadRow(menu, () => UiText.Get(title), () => UiText.Get("Листайте ←/→. «Нет» — убрать."),
                () => new[] { UiText.Get("Нет") }.Concat(Numbers(CharacterNatives.OverlayStyles(overlay))).ToArray(),
                head => head.Overlay(overlay).Style + 1, (head, value) => EditOverlay(head, overlay, item => item.Style = value - 1));
            AddHeadRow(menu, () => UiText.Format("{0}: насыщенность", UiText.Get(title)), () => UiText.Get("Насколько заметно."),
                Percents, head => head.Overlay(overlay).Opacity / 10, (head, value) => EditOverlay(head, overlay, item => item.Opacity = value * 10));
            if (!CharacterNatives.TakesColour(overlay)) continue;
            AddHeadRow(menu, () => UiText.Format("{0}: цвет", UiText.Get(title)), () => UiText.Get("Листайте ←/→."),
                () => Numbers(CharacterNatives.OverlayTints(overlay)), head => head.Overlay(overlay).Color,
                (head, value) => EditOverlay(head, overlay, item => item.Color = value));
        }
    }

    private static void EditOverlay(CharacterHead head, int slot, Action<CharacterOverlay> edit)
    {
        CharacterOverlay overlay = head.Overlay(slot).Copy();
        edit(overlay);
        head.SetOverlay(overlay);
    }

    // A face row: its options are read from the game when the editor opens,
    // and a choice changes the editor's face and puts it on at once.
    private void AddHeadRow(NativeMenu menu, Func<string> title, Func<string> description, Func<string[]> options,
        Func<CharacterHead, int> read, Action<CharacterHead, int> write)
    {
        NativeListItem<string> item = UiMenu.List(title, description, () => new[] { "1" });
        var row = new HeadRow(item, options, read, write);
        item.ItemChanged += (_, e) => { if (!_creatorUpdating) ChangeCreatorHead(row, e.Index); };
        menu.Add(item);
        _creatorHeadRows.Add(row);
    }

    private static string[] Percents() => Enumerable.Range(0, 11).Select(step => (step * 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%").ToArray();

    private static string Signed(int value) => value > 0
        ? "+" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Numbers(int count) =>
        Enumerable.Range(1, Math.Max(1, count)).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private IEnumerable<NativeMenu> CreatorMenus() =>
        new[] { _creatorMenu, _creatorHeritageMenu, _creatorFaceMenu, _creatorLookMenu, _creatorClothesMenu, _creatorTattooMenu }
            .Concat(_creatorTattooZones ?? Array.Empty<NativeMenu>()).Where(menu => menu != null)!;

    private bool CreatorMenuVisible() => CreatorMenus().Any(menu => menu.Visible);

    private void HideCreatorMenus()
    {
        foreach (NativeMenu menu in CreatorMenus()) menu.Visible = false;
    }

    private void TickCharacters(long now)
    {
        if (_characters == null) return;
        TickModelChange(now);
        if (_creator != null)
        {
            TickCreator(now);
            return;
        }

        if (now - _lastCharacterCheck < CharacterCheckMilliseconds) return;
        _lastCharacterCheck = now;
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists()) return;
        if (_modelChange == null) _heroLink?.Sync(StoryModel(), player.Model.Hash);
        // The game's respawn dresses a hero in its own default clothes; the
        // saved character's clothes go on again once the player is back.
        if (player.IsDead)
        {
            _ownLookApplied = false;
            return;
        }
        if (!_characters.OwnKnown || _modelChange != null || !WorldQuiet(now)) return;
        CharacterLook? own = _characters.Own;
        if (own == null)
        {
            if (_creatorOffered || IsInputBlocked() || _pool?.AreAnyVisible != false) return;
            _creatorOffered = true;
            OpenCreator(first: true);
            return;
        }

        // The game gives a story hero back after death; the server's character
        // goes on again. A story hero choice brings back the hero the player had.
        int model = player.Model.Hash;
        int wanted = own.IsOnline ? own.Model : CharacterModels.IsOnline(model) ? StoryModel() : model;
        if (model != wanted)
        {
            Logger.Info("OG Coop: putting on the saved character.");
            ChangeModel(wanted, ped => { if (own.IsOnline) CharacterNatives.Apply(ped.Handle, own); });
            _ownLookApplied = true;
            return;
        }

        if (!_ownLookApplied)
        {
            _ownLookApplied = true;
            if (own.IsOnline) CharacterNatives.Apply(player.Handle, own);
            return;
        }

        // Story scripts dress the body by the story hero's item numbers (the
        // respawn after a death does), which leaves holes in an Online body:
        // the saved clothes go back on. The bag is left alone (the parachute
        // takes it), and so are accessories (a motorbike helmet).
        if (!own.IsOnline || player.IsInVehicle()) return;
        int changed = CharacterNatives.RestoreClothes(player.Handle, own, keep: ParachuteBagSlot);
        if (changed == 0) return;
        // The same scripts swap the tattoos for the story hero's.
        CharacterNatives.ApplyTattoos(player.Handle, own.Tattoos);
        if (!_clothesRestoreLogged)
        {
            _clothesRestoreLogged = true;
            Logger.Info("OG Coop: the game changed " + changed + " clothing slots of the character; put back with the tattoos.");
        }
    }

    private static void RememberStoryModel(Ped? player)
    {
        if (player != null && player.Exists() && !CharacterModels.IsOnline(player.Model.Hash)) _storyModel = player.Model.Hash;
    }

    // The hero the player came from; after a resource restart in an Online
    // body the game's hero table still tells which one it was.
    private int StoryModel()
    {
        if (_storyModel != 0) return _storyModel;
        int linked = _heroLink?.LinkedHero() ?? -1;
        return linked >= 0 ? HeroLink.Heroes[linked] : FranklinModel;
    }

    // SET_PLAYER_MODEL needs the model in memory, which takes a few frames; the
    // new body then gets the old one's weapons, health and armour.
    private void ChangeModel(int model, Action<Ped> then)
    {
        if (_modelChange != null) Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, _modelChange.Model);
        Function.Call(Hash.REQUEST_MODEL, model);
        _modelChange = new ModelChange(model, NowMilliseconds(), then);
    }

    private void TickModelChange(long now)
    {
        ModelChange? change = _modelChange;
        if (change == null) return;
        if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, change.Model))
        {
            if (now - change.Started < ModelLoadLimit) return;
            _modelChange = null;
            Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, change.Model);
            Logger.Warning("OG Coop: character body did not load in time.");
            Notification.PostTicker(UiText.Get("~o~Персонаж:~s~ модель не загрузилась. Попробуйте ещё раз."), false);
            if (_creator?.Stage != CreatorStage.Editing) return;
            _creatorUpdating = true;
            try { _creatorBody!.SelectedIndex = BodyIndex(Game.Player.Character.Model.Hash); }
            finally { _creatorUpdating = false; }
            SetCreatorEnabled(true);
            return;
        }

        _modelChange = null;
        Ped before = Game.Player.Character;
        RememberStoryModel(before);
        CarriedGear gear = CarriedGear.Take(before);
        Function.Call(Hash.SET_PLAYER_MODEL, Game.Player.Handle, change.Model);
        Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, change.Model);
        _heroLink?.Sync(StoryModel(), change.Model);
        Ped after = Game.Player.Character;
        gear.GiveTo(after);
        change.Then(after);
    }

    private void OpenCreator(bool first)
    {
        if (_creator != null || _characters == null || _creatorMenu == null) return;
        if (!_characters.OwnKnown)
        {
            Notification.PostTicker(UiText.Get("~o~Персонаж:~s~ сервер ещё не ответил. Попробуйте через несколько секунд."), false);
            return;
        }
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists() || player.IsDead || _modelChange != null ||
            _mapLoad != null || _pendingTeleport != null || _pendingRecall != null)
        {
            Notification.PostTicker(UiText.Get("~o~Персонаж:~s~ дождитесь окончания загрузки или перемещения."), false);
            return;
        }

        _pool?.HideAll();
        RememberStoryModel(player);
        int model = player.Model.Hash;
        Vehicle? vehicle = player.CurrentVehicle;
        // The face cannot be read back from the game: an Online body wears the
        // saved character's face, or the default one.
        CharacterLook? own = _characters.Own;
        bool saved = own is { IsOnline: true } && own.Model == model;
        CharacterHead? head = !CharacterModels.IsOnline(model) ? null
            : saved ? (own!.Head ?? CharacterHead.Default(model)).Copy() : CharacterHead.Default(model);
        _creator = new CreatorSession(first, NowMilliseconds(), model, CharacterNatives.Read(player.Handle, model), head,
            saved ? own!.Tattoos : Enumerable.Empty<CharacterTattoo>(), player.Position, player.Heading,
            vehicle != null && vehicle.Exists() ? vehicle : null, vehicle != null && vehicle.Exists() ? player.SeatIndex : VehicleSeat.None);
        // On the island, in Yankton, on a ship or inside an interior the place
        // around the player unloads while they are in the room and is not there
        // to land on when they come back: the editor opens where they stand.
        bool settled = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, player.Handle) == 0 &&
            !_specialPlaces.IsInsideSeparatePlace(player.Position);
        _creator.Interior = settled ? InteriorNatives.InteriorAt(CreatorRoom.X, CreatorRoom.Y, CreatorRoom.Z) : 0;
        if (_creator.Interior != 0) Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, _creator.Interior);
        MapLoadingScreen.FadeOut(CreatorFade);
        Logger.Info("OG Coop: character editor opened" + (first ? " for a new player" : string.Empty) + ".");
    }

    private void TickCreator(long now)
    {
        CreatorSession session = _creator!;
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists())
        {
            EndCreator(session);
            return;
        }

        switch (session.Stage)
        {
            case CreatorStage.Entering:
                if (!MapLoadingScreen.IsFadedOut() && now - session.StageAt < 1000) return;
                if (session.Interior == 0)
                {
                    StartCreatorEditing(session, player);
                    return;
                }
                EnterCreatorRoom(session, player);
                session.Stage = CreatorStage.Loading;
                session.StageAt = now;
                break;
            case CreatorStage.Loading:
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, CreatorRoom.X, CreatorRoom.Y, CreatorRoom.Z);
                bool ready = InteriorNatives.IsReady(session.Interior) && Function.Call<bool>((Hash)0xE9676F61BC0B3321UL, player.Handle); // HAS_COLLISION_LOADED_AROUND_ENTITY
                if (!ready && now - session.StageAt < CreatorRoomLimit) return;
                if (!ready)
                {
                    // No room in this game: edit where the player stood.
                    ReturnFromCreator(session, player);
                    Function.Call(Hash.UNPIN_INTERIOR, session.Interior);
                    session.Interior = 0;
                    session.InRoom = false;
                }
                StartCreatorEditing(session, player);
                break;
            case CreatorStage.Editing:
            case CreatorStage.Saving:
                Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                HoldCreatorPed(session, player);
                if (session.Stage == CreatorStage.Saving)
                {
                    // The answer decides how the editor ends; until then it stays open.
                    if (now - session.StageAt >= CharacterSaveLimit) FinishCharacterSave(false);
                    else if (!CreatorMenuVisible() && _creatorMenu != null) _creatorMenu.Visible = true;
                }
                else if (!CreatorMenuVisible() && _modelChange == null) LeaveCreator(revert: true);
                break;
            case CreatorStage.Reverting:
                Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                if (_modelChange != null) return;
                session.Stage = CreatorStage.Leaving;
                session.StageAt = now;
                MapLoadingScreen.FadeOut(CreatorFade);
                break;
            case CreatorStage.Leaving:
                Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
                if (!MapLoadingScreen.IsFadedOut() && now - session.StageAt < 1000) return;
                ReturnFromCreator(session, player);
                session.Stage = CreatorStage.Returning;
                session.StageAt = now;
                break;
            case CreatorStage.Returning:
                // Still frozen and behind the dark screen until the ground under
                // the player has loaded again.
                Vector3 back = session.ReturnPosition;
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, back.X, back.Y, back.Z);
                Entity held = player.CurrentVehicle is Vehicle car && car.Exists() ? car : player;
                bool ground = Function.Call<bool>((Hash)0xE9676F61BC0B3321UL, held.Handle); // HAS_COLLISION_LOADED_AROUND_ENTITY
                if (!ground && now - session.StageAt < CreatorReturnLimit) return;
                if (!ground) Logger.Warning("OG Coop: the ground did not load around the player back from the character editor.");
                EndCreator(session);
                break;
        }
    }

    private static void EnterCreatorRoom(CreatorSession session, Ped player)
    {
        if (player.IsInVehicle()) Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player.Handle);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, true);
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, player.Handle, CreatorRoom.X, CreatorRoom.Y, CreatorRoom.Z, false, false, false);
        player.Heading = CreatorRoomHeading;
        session.InRoom = true;
    }

    private void StartCreatorEditing(CreatorSession session, Ped player)
    {
        session.Heading = session.InRoom ? CreatorRoomHeading : session.ReturnHeading;
        session.Anchor = player.Position;
        Logger.Info("OG Coop: character editor " + (session.InRoom ? "in the creator room." : "where the player stands (the creator room did not load)."));
        HoldCreatorPed(session, player);
        session.Camera = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", false);
        AimCreatorCamera();
        Function.Call(Hash.SET_CAM_ACTIVE, session.Camera, true);
        Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false, 0);
        MapLoadingScreen.FadeIn(500);

        _creatorUpdating = true;
        try
        {
            _creatorBody!.SelectedIndex = BodyIndex(player.Model.Hash);
            _creatorView!.SelectedIndex = 0;
            _creatorTurn!.SelectedIndex = 0;
        }
        finally { _creatorUpdating = false; }
        UiMenu.Title(_creatorLater!, () => UiText.Get(session.First ? "Позже" : "Отмена"));
        UiMenu.Description(_creatorLater!, () => UiText.Get(session.First
            ? "Выйти без персонажа. Редактор откроется снова при следующем входе на сервер."
            : "Выйти без изменений."));
        SetCreatorEnabled(true);
        session.Stage = CreatorStage.Editing;
        session.StageAt = NowMilliseconds();
        _creatorMenu!.SelectedIndex = 0;
        _creatorMenu.Visible = true;
        if (session.First)
            Notification.PostTicker(UiText.Get("~b~Персонаж:~s~ создайте своего персонажа. Его увидит напарник, и он сохранится на сервере."), false);
    }

    // The body stays still facing the camera; a new body after a model change
    // is put back the same way.
    private void HoldCreatorPed(CreatorSession session, Ped player)
    {
        if (!player.IsInVehicle())
        {
            Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, true);
            float heading = session.Heading + (_creatorTurn?.SelectedIndex ?? 0) * 45f;
            if (Math.Abs(player.Heading - heading % 360f) > 1f) player.Heading = heading % 360f;
        }
    }

    private void AimCreatorCamera()
    {
        CreatorSession? session = _creator;
        if (session == null || session.Camera == 0) return;
        int view = _creatorView?.SelectedIndex ?? 0;
        (float distance, float height, float target) = view switch
        {
            1 => (0.9f, 0.65f, 0.65f),
            2 => (1.6f, -0.45f, -0.6f),
            _ => (2.6f, 0.2f, 0f),
        };
        double angle = session.Heading * Math.PI / 180.0;
        // GTA's heading 0 faces north (+Y); the camera stands in front of the body.
        var front = new Vector3((float)-Math.Sin(angle), (float)Math.Cos(angle), 0f);
        Vector3 at = session.Anchor + front * distance + new Vector3(0f, 0f, height);
        Function.Call(Hash.SET_CAM_COORD, session.Camera, at.X, at.Y, at.Z);
        Function.Call(Hash.POINT_CAM_AT_COORD, session.Camera, session.Anchor.X, session.Anchor.Y, session.Anchor.Z + target);
        Function.Call(Hash.SET_CAM_FOV, session.Camera, 40f);
    }

    private void TurnCreatorPed()
    {
        if (_creator != null) HoldCreatorPed(_creator, Game.Player.Character);
    }

    private void SetCreatorView(int view)
    {
        if (_creatorView == null || _creatorView.SelectedIndex == view) return;
        _creatorUpdating = true;
        try { _creatorView.SelectedIndex = view; }
        finally { _creatorUpdating = false; }
        AimCreatorCamera();
    }

    private void ChangeCreatorHead(HeadRow row, int index)
    {
        Ped player = Game.Player.Character;
        CharacterHead? head = _creator?.Head;
        if (_creator?.Stage != CreatorStage.Editing || head == null || !CharacterModels.IsOnline(player.Model.Hash)) return;
        row.Write(head, index);
        CharacterNatives.ApplyHead(player.Handle, player.Model.Hash, head);
    }

    private void RandomizeCreatorParents()
    {
        Ped player = Game.Player.Character;
        CharacterHead? head = _creator?.Head;
        if (_creator?.Stage != CreatorStage.Editing || head == null || !CharacterModels.IsOnline(player.Model.Hash)) return;
        var random = new Random();
        head.Mother = CreatorMothers[random.Next(CreatorMothers.Length)].Id;
        head.Father = CreatorFathers[random.Next(CreatorFathers.Length)].Id;
        head.Resemblance = random.Next(11) * 10;
        head.SkinTone = random.Next(11) * 10;
        CharacterNatives.ApplyHead(player.Handle, player.Model.Hash, head);
        RefreshCreatorRows(player);
    }

    private static int BodyIndex(int model) => model == CharacterModels.Male ? 0 : model == CharacterModels.Female ? 1 : 2;

    private void ChangeCreatorBody(int index)
    {
        if (_creator == null || _creator.Stage != CreatorStage.Editing) return;
        int model = index == 0 ? CharacterModels.Male : index == 1 ? CharacterModels.Female : StoryModel();
        if (Game.Player.Character.Model.Hash == model) return;
        SetCreatorEnabled(false);
        ChangeModel(model, ped =>
        {
            if (_creator == null) return;
            if (CharacterModels.IsOnline(model))
            {
                _creator.Head ??= CharacterHead.Default(model);
                CharacterNatives.Reset(ped.Handle, model, _creator.Head, _creator.TattoosOf(model));
            }
            HoldCreatorPed(_creator, ped);
            SetCreatorEnabled(true);
        });
    }

    private void RandomizeCreator()
    {
        Ped player = Game.Player.Character;
        if (_creator?.Stage != CreatorStage.Editing || !CharacterModels.IsOnline(player.Model.Hash)) return;
        CharacterNatives.Randomize(player.Handle, player.Model.Hash, _creator.Head);
        // A random top gets the arms and undershirt it needs, as in Online.
        CharacterNatives.WearForcedParts(player.Handle, TopSlot, CharacterNatives.Drawable(player.Handle, TopSlot, false),
            CharacterNatives.Texture(player.Handle, TopSlot, false));
        RefreshCreatorRows(player);
    }

    private const int TopSlot = 11;

    private void WearCreatorPart(NativeListItem<string> item, int slot, bool prop, int index)
    {
        Ped player = Game.Player.Character;
        if (_creator?.Stage != CreatorStage.Editing || !CharacterModels.IsOnline(player.Model.Hash)) return;
        int drawable = prop ? index - 1 : index;
        CharacterNatives.Set(player.Handle, slot, prop, drawable, 0);
        // The parts an item needs (a top's arms) come with it; the rows then
        // show what changed.
        if (!prop && CharacterNatives.WearForcedParts(player.Handle, slot, drawable, 0) > 0) RefreshCreatorRows(player);
        else DescribeCreatorPart(item, player, slot, prop);
    }

    private void NextCreatorColour(NativeListItem<string> item, int slot, bool prop)
    {
        Ped player = Game.Player.Character;
        if (_creator?.Stage != CreatorStage.Editing || !CharacterModels.IsOnline(player.Model.Hash)) return;
        int drawable = CharacterNatives.Drawable(player.Handle, slot, prop);
        int colours = CharacterNatives.Textures(player.Handle, slot, prop, drawable);
        if (drawable < 0 || colours <= 1) return;
        int next = (Math.Max(0, CharacterNatives.Texture(player.Handle, slot, prop)) + 1) % colours;
        CharacterNatives.Set(player.Handle, slot, prop, drawable, next);
        DescribeCreatorPart(item, player, slot, prop);
    }

    // Each clothing row lists the items this body has, with the current one
    // selected; each face row shows the editor's face. A story hero keeps the
    // game's own face and clothes, so those rows are off.
    private void RefreshCreatorRows(Ped player)
    {
        bool online = CharacterModels.IsOnline(player.Model.Hash);
        CharacterHead? head = _creator?.Head;
        _creatorUpdating = true;
        try
        {
            if (_creatorRandom != null) _creatorRandom.Enabled = online;
            foreach (NativeMenu? section in new[] { _creatorHeritageMenu, _creatorFaceMenu })
                if (section != null) foreach (NativeItem item in section.Items) item.Enabled = online && head != null;
            foreach (HeadRow row in _creatorHeadRows)
            {
                row.Item.Enabled = online && head != null;
                if (!online || head == null) continue;
                UiMenu.Options(row.Item, row.Options);
                row.Item.SelectedIndex = Math.Max(0, Math.Min(row.Item.Items.Count - 1, row.Read(head)));
            }
            for (int i = 0; i < CreatorSlots.Length; i++)
            {
                (int slot, bool prop, _) = CreatorSlots[i];
                NativeListItem<string> item = _creatorParts[i];
                int count = online ? Math.Max(0, CharacterNatives.Drawables(player.Handle, slot, prop)) : 0;
                UiMenu.Options(item, () => PartOptions(count, prop));
                item.Enabled = online && count > 0;
                if (!online)
                {
                    UiMenu.Description(item, () => UiText.Get("У сюжетного героя своя одежда."));
                    continue;
                }
                int drawable = CharacterNatives.Drawable(player.Handle, slot, prop);
                item.SelectedIndex = Math.Max(0, Math.Min(item.Items.Count - 1, prop ? drawable + 1 : drawable));
                DescribeCreatorPart(item, player, slot, prop);
            }
            RefreshCreatorTattoos(player, online);
        }
        finally { _creatorUpdating = false; }
    }

    // The lists follow the body (the man's and the woman's tattoos differ);
    // the marks follow the editor's tattoos. A story hero keeps the game's own.
    private void RefreshCreatorTattoos(Ped player, bool online)
    {
        int model = online ? player.Model.Hash : 0;
        if (_creatorTattooEntry != null) _creatorTattooEntry.Enabled = online;
        if (model != _creatorTattooModel)
        {
            _creatorTattooModel = model;
            _creatorTattooItems.Clear();
            foreach (NativeMenu zone in _creatorTattooZones) zone.Clear();
            int number = 0;
            foreach (TattooOption option in CharacterTattoos.For(model, message => Logger.Info(message)))
            {
                int shown = ++number;
                NativeCheckboxItem item = UiMenu.Checkbox(() => CharacterTattoos.Name(option) ?? UiText.Format("Татуировка {0}", shown),
                    () => UiText.Get("Enter — сделать или свести."));
                item.CheckboxChanged += (_, _) => ToggleCreatorTattoo(item, option);
                _creatorTattooZones[option.Zone].Add(item);
                _creatorTattooItems.Add((item, option));
            }
        }
        List<CharacterTattoo>? worn = online ? _creator?.TattoosOf(model) : null;
        foreach ((NativeCheckboxItem item, TattooOption option) in _creatorTattooItems)
            item.Checked = worn != null && worn.Any(option.Is);
    }

    private void ToggleCreatorTattoo(NativeCheckboxItem item, TattooOption option)
    {
        if (_creatorUpdating) return;
        Ped player = Game.Player.Character;
        if (_creator?.Stage != CreatorStage.Editing || !CharacterModels.IsOnline(player.Model.Hash)) return;
        List<CharacterTattoo> tattoos = _creator.TattoosOf(player.Model.Hash);
        int index = tattoos.FindIndex(option.Is);
        if (item.Checked && index < 0)
        {
            if (tattoos.Count >= CharacterCodec.MaximumTattoos)
            {
                _creatorUpdating = true;
                try { item.Checked = false; }
                finally { _creatorUpdating = false; }
                Notification.PostTicker(UiText.Format("~o~Персонаж:~s~ не больше {0} татуировок.", CharacterCodec.MaximumTattoos), false);
                return;
            }
            tattoos.Add(option.ToTattoo());
        }
        else if (!item.Checked && index >= 0)
        {
            tattoos.RemoveAt(index);
        }
        CharacterNatives.ApplyTattoos(player.Handle, tattoos);
    }

    private void ClearCreatorTattoos()
    {
        Ped player = Game.Player.Character;
        if (_creator?.Stage != CreatorStage.Editing || !CharacterModels.IsOnline(player.Model.Hash)) return;
        _creator.TattoosOf(player.Model.Hash).Clear();
        CharacterNatives.ApplyTattoos(player.Handle, Enumerable.Empty<CharacterTattoo>());
        RefreshCreatorRows(player);
    }

    private static string[] PartOptions(int count, bool prop)
    {
        IEnumerable<string> numbers = Enumerable.Range(1, Math.Max(1, count)).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return (prop ? new[] { UiText.Get("Нет") }.Concat(numbers) : numbers).ToArray();
    }

    private static void DescribeCreatorPart(NativeListItem<string> item, Ped player, int slot, bool prop)
    {
        int drawable = CharacterNatives.Drawable(player.Handle, slot, prop);
        if (drawable < 0)
        {
            UiMenu.Description(item, () => UiText.Get("Ничего не надето. Листайте ←/→."));
            return;
        }
        int texture = Math.Max(0, CharacterNatives.Texture(player.Handle, slot, prop));
        int colours = Math.Max(1, CharacterNatives.Textures(player.Handle, slot, prop, drawable));
        string name = CharacterNatives.ShopName(player.Handle, slot, prop, drawable, texture) is string shop ? shop + ". " : string.Empty;
        bool arms = !prop && slot == ArmsSlot;
        UiMenu.Description(item, () => (colours > 1
            ? name + UiText.Format("Цвет {0} из {1}: Enter — следующий. Вещи — ←/→.", texture + 1, colours)
            : name + UiText.Get("Вещи — ←/→.")) + (arms ? " " + UiText.Get("Видна дыра на теле — подберите руки под верх.") : string.Empty));
    }

    private const int ArmsSlot = 3;

    // Off while a body loads or the server saves; back on, the face and
    // clothing rows follow the current body.
    private void SetCreatorEnabled(bool enabled)
    {
        foreach (NativeMenu menu in CreatorMenus())
            foreach (NativeItem item in menu.Items) item.Enabled = enabled;
        if (enabled) RefreshCreatorRows(Game.Player.Character);
    }

    private void SaveCreator()
    {
        CreatorSession? session = _creator;
        Ped player = Game.Player.Character;
        if (session?.Stage != CreatorStage.Editing || _characters == null || _modelChange != null) return;
        int model = player.Model.Hash;
        CharacterLook look = CharacterModels.IsOnline(model) ? CharacterNatives.Read(player.Handle, model) : new CharacterLook();
        if (look.IsOnline)
        {
            look.Head = (session.Head ?? CharacterHead.Default(model)).Copy();
            look.Tattoos = session.TattoosOf(model).Select(Copy).ToList();
        }
        try { _characters.Save(look); }
        catch (System.IO.InvalidDataException)
        {
            Notification.PostTicker(UiText.Get("~o~Персонаж:~s~ эту одежду нельзя сохранить. Выберите другую."), false);
            return;
        }
        session.Saving = look;
        session.Stage = CreatorStage.Saving;
        session.StageAt = NowMilliseconds();
        SetCreatorEnabled(false);
    }

    private void FinishCharacterSave(bool saved)
    {
        CreatorSession? session = _creator;
        if (session?.Stage != CreatorStage.Saving || session.Saving == null) return;
        if (!saved)
        {
            session.Stage = CreatorStage.Editing;
            session.Saving = null;
            SetCreatorEnabled(true);
            Notification.PostTicker(UiText.Get("~o~Персонаж:~s~ сервер не сохранил персонажа. Попробуйте ещё раз."), false);
            return;
        }

        _characters?.Saved(session.Saving);
        _ownLookApplied = true;
        Logger.Info("OG Coop: character saved.");
        Notification.PostTicker(UiText.Get("~g~Персонаж сохранён.~s~ Он будет с вами при каждом входе на сервер."), false);
        LeaveCreator(revert: false);
    }

    private void LeaveCreator(bool revert)
    {
        CreatorSession? session = _creator;
        if (session == null || session.Stage is CreatorStage.Entering or CreatorStage.Reverting or CreatorStage.Leaving or CreatorStage.Returning) return;
        session.Stage = CreatorStage.Reverting;
        session.StageAt = NowMilliseconds();
        HideCreatorMenus();
        if (!revert) return;
        CharacterLook original = session.Original;
        int model = session.OriginalModel;
        CharacterHead? head = session.OriginalHead;
        List<CharacterTattoo> tattoos = session.OriginalTattoos;
        Action<Ped> restore = ped =>
        {
            if (CharacterModels.IsOnline(model)) CharacterNatives.ApplyHead(ped.Handle, model, head);
            CharacterNatives.ApplyParts(ped.Handle, original);
            if (CharacterModels.IsOnline(model)) CharacterNatives.ApplyTattoos(ped.Handle, tattoos);
        };
        if (Game.Player.Character.Model.Hash == model) restore(Game.Player.Character);
        else ChangeModel(model, restore);
        if (session.First) Notification.PostTicker(UiText.Get("~b~Персонаж:~s~ создать его можно позже: J → Персонаж → Внешность."), false);
    }

    // Puts the player back where they stood; the caller keeps them frozen
    // until the ground there has loaded.
    private void ReturnFromCreator(CreatorSession session, Ped player)
    {
        if (!session.InRoom) return;
        session.InRoom = false;
        Vector3 back = session.ReturnPosition;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, player.Handle, back.X, back.Y, back.Z, false, false, false);
        player.Heading = session.ReturnHeading;
        Vehicle? vehicle = session.ReturnVehicle;
        if (vehicle != null && vehicle.Exists() && !vehicle.IsDead && session.ReturnSeat != VehicleSeat.None && vehicle.IsSeatFree(session.ReturnSeat))
            player.SetIntoVehicle(vehicle, session.ReturnSeat);
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, back.X, back.Y, back.Z);
    }

    // Undo everything the editor holds: the camera, the frozen body, the room.
    // Also runs when the resource stops in the middle of editing.
    private void EndCreator(CreatorSession session)
    {
        _creator = null;
        HideCreatorMenus();
        if (session.Camera != 0)
        {
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            Function.Call(Hash.DESTROY_CAM, session.Camera, false);
        }
        Ped player = Game.Player.Character;
        if (player != null && player.Exists())
        {
            if (session.Stage is CreatorStage.Loading or CreatorStage.Editing or CreatorStage.Saving or CreatorStage.Reverting or CreatorStage.Leaving)
                ReturnFromCreator(session, player);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, player.Handle, false);
        }
        if (session.Interior != 0) Function.Call(Hash.UNPIN_INTERIOR, session.Interior);
        MapLoadingScreen.FadeIn(500);
    }

    private void CleanupCharacters()
    {
        if (_creator != null) EndCreator(_creator);
        if (_modelChange != null) Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, _modelChange.Model);
        _modelChange = null;
        CharacterReplicas.Uninstall();
        _characters?.Clear();
    }

    private enum CreatorStage { Entering, Loading, Editing, Saving, Reverting, Leaving, Returning }

    private static CharacterTattoo Copy(CharacterTattoo tattoo) => new CharacterTattoo { Collection = tattoo.Collection, Name = tattoo.Name };

    private sealed class CreatorSession
    {
        // Each body's tattoos while editing: switching bodies keeps them.
        private readonly Dictionary<int, List<CharacterTattoo>> _tattoos = new Dictionary<int, List<CharacterTattoo>>();

        internal CreatorSession(bool first, long started, int originalModel, CharacterLook original, CharacterHead? head,
            IEnumerable<CharacterTattoo> tattoos, Vector3 returnPosition, float returnHeading, Vehicle? returnVehicle, VehicleSeat returnSeat)
        {
            First = first;
            StageAt = started;
            OriginalModel = originalModel;
            Original = original;
            OriginalHead = head?.Copy();
            Head = head;
            OriginalTattoos = tattoos.Select(Copy).ToList();
            _tattoos[originalModel] = OriginalTattoos.Select(Copy).ToList();
            ReturnPosition = returnPosition;
            ReturnHeading = returnHeading;
            ReturnVehicle = returnVehicle;
            ReturnSeat = returnSeat;
            Heading = returnHeading;
        }

        internal bool First { get; }
        internal int OriginalModel { get; }
        internal CharacterLook Original { get; }
        internal CharacterHead? OriginalHead { get; }
        // The face being edited; null until the body is an Online one.
        internal CharacterHead? Head { get; set; }
        internal List<CharacterTattoo> OriginalTattoos { get; }
        internal Vector3 ReturnPosition { get; }
        internal float ReturnHeading { get; }
        internal Vehicle? ReturnVehicle { get; }
        internal VehicleSeat ReturnSeat { get; }
        internal CreatorStage Stage { get; set; }
        internal long StageAt { get; set; }
        internal int Interior { get; set; }
        internal bool InRoom { get; set; }
        internal Vector3 Anchor { get; set; }
        internal float Heading { get; set; }
        internal int Camera { get; set; }
        internal CharacterLook? Saving { get; set; }

        internal List<CharacterTattoo> TattoosOf(int model)
        {
            if (!_tattoos.TryGetValue(model, out List<CharacterTattoo>? tattoos)) _tattoos[model] = tattoos = new List<CharacterTattoo>();
            return tattoos;
        }
    }

    private sealed class HeadRow
    {
        internal HeadRow(NativeListItem<string> item, Func<string[]> options, Func<CharacterHead, int> read, Action<CharacterHead, int> write)
        {
            Item = item;
            Options = options;
            Read = read;
            Write = write;
        }

        internal NativeListItem<string> Item { get; }
        internal Func<string[]> Options { get; }
        internal Func<CharacterHead, int> Read { get; }
        internal Action<CharacterHead, int> Write { get; }
    }

    private sealed class ModelChange
    {
        internal ModelChange(int model, long started, Action<Ped> then)
        {
            Model = model;
            Started = started;
            Then = then;
        }

        internal int Model { get; }
        internal long Started { get; }
        internal Action<Ped> Then { get; }
    }
}
