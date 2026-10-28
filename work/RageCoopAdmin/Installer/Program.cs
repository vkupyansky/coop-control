using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using OGCoop.Shared;

namespace OGCoop.Installer;

internal static class Program
{
    internal const string PrivatePayload = "1 Скопировать в папку игры";
    internal const string PublicPayload = "client";
    internal const string VehicleFolder = "2 Машины";
    private static readonly string[] GameProcesses = { "GTA5", "PlayGTAV", "GTAVLauncher", "GTA5_BE" };
    private static StreamWriter? _log;
    private static bool _handedOff;
    // Friends' private archive: problems go to its owner first, never to the public download.
    private static bool _private;

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 64;
        }

        if (options.Language != null) Text.Russian = options.Language == "ru";
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = Text.Get("OG Coop — установка и обновление", "OG Coop — install and update");
        }
        catch (IOException)
        {
            // Output redirected: no console window to configure.
        }

        string home = AppDomain.CurrentDomain.BaseDirectory;
        _private = Directory.Exists(Path.Combine(home, PrivatePayload));
        OpenLog(home);
        int code;
        try
        {
            code = Run(options, home);
        }
        catch (InstallerException error)
        {
            Fail(error.Message, error.Hint, error.InnerException);
            code = 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Fail(Text.Get("Не удалось записать файлы: ", "Could not write files: ") + error.Message,
                Text.Get("Закройте GTA и OpenIV и запустите установщик ещё раз.", "Close GTA and OpenIV and run the installer again."), error);
            code = 1;
        }
        // The window must explain any failure before it closes. The full error goes to the log.
        catch (Exception error)
        {
            Fail(Text.Get("Непредвиденная ошибка: ", "Unexpected error: ") + error.Message,
                Text.Get("Запустите установщик ещё раз.", "Run the installer again."), error);
            code = 2;
        }
        finally
        {
            _log?.Dispose();
        }

        if (!options.NoPause && !_handedOff)
        {
            Console.WriteLine();
            Console.WriteLine(Text.Get("Нажмите Enter, чтобы закрыть окно.", "Press Enter to close this window."));
            Console.ReadLine();
        }

        return code;
    }

    private static int Run(Options options, string home)
    {
        Say(Text.Get("=== OG Coop " + ProductInfo.Version + " — установка и обновление ===", "=== OG Coop " + ProductInfo.Version + " — install and update ==="));
        Say(Text.Get("Автор: ", "By ") + ProductInfo.Author + " · Telegram " + ProductInfo.Telegram + " · github.com/vkupyansky");
        if (options.Check) Say(Text.Get("Режим проверки: ничего не меняю, только показываю.", "Check mode: nothing is changed."));
        string payload = Payload(home) ?? throw new InstallerException(
            Text.Get("Рядом с установщиком нет файлов для установки.", "The files to install are not next to the installer."),
            Text.Get("Распакуйте архив целиком (правой кнопкой → «Извлечь всё») и запустите установщик из распакованной папки.",
                "Extract the whole archive (right-click → Extract All) and run the installer from the extracted folder."));

        if (!_private && !CheckForNewerRelease(options)) return 0;
        WaitForGameClosed(options);
        GameFolder game = ChooseGame(options);
        InstallerException? rejected = Rejection(game);
        if (rejected != null) throw rejected;
        Say(Text.Get("Папка игры: ", "Game folder: ") + game.Path + " (GTA V Legacy " + game.Version + ")");

        bool update = File.Exists(Path.Combine(game.Path, @"scripts\RageCoop\RageCoop.Client.dll"));
        Say(update ? Text.Get("Нашёл прошлую установку — обновляю только изменившееся.", "Found an earlier installation — updating only what changed.")
            : Text.Get("Ставлю впервые.", "First installation."));

        if (!options.Check && !CanWrite(game.Path))
        {
            Elevate(options, game.Path);
            return 0;
        }

        CheckPrerequisites(options);

        CopyResult copied = GameFiles.Install(payload, game.Path, apply: !options.Check);
        Say(Text.Get("Файлы игры: ", "Game files: ") + copied.Added + Text.Get(" новых, ", " new, ") + copied.Updated +
            Text.Get(" обновлено, ", " updated, ") + copied.Settings + Text.Get(" настроек дополнено, ", " settings merged, ") +
            copied.Unchanged + Text.Get(" без изменений.", " unchanged."));
        foreach (string changed in copied.Changed) Log("  " + changed);

        EnsureScriptHookV(options, home, game.Path);
        string[] missing = new[] { "ScriptHookVDotNet.asi", "ScriptHookVDotNet3.dll", @"scripts\RageCoop\RageCoop.Client.dll" }
            .Where(name => !File.Exists(Path.Combine(game.Path, name)) && !File.Exists(Path.Combine(payload, name))).ToArray();
        if (missing.Length > 0)
            throw new InstallerException(Text.Get("Не хватает файлов: ", "Missing files: ") + string.Join(", ", missing),
                Text.Get("Скачайте архив заново и распакуйте его целиком.", "Download the archive again and extract all of it."));

        bool vehiclesPending = ReportVehicles(home, game.Path);

        Say(string.Empty);
        Say(options.Check ? Text.Get("Проверка закончена. Ничего не изменено.", "Check finished. Nothing was changed.")
            : Text.Get("Готово! Всё установлено.", "Done! Everything is installed."));
        Say(Text.Get("Дальше:", "Next:"));
        int step = 1;
        if (vehiclesPending) Say(step++ + Text.Get(". Поставьте машины через OpenIV — шаги выше.", ". Install the cars with OpenIV — steps above."));
        if (!update) Say(step++ + Text.Get(". Отключите BattlEye: Rockstar Games Launcher → Настройки → GTA V Legacy; в Steam и Epic — параметр запуска -nobattleye.",
            ". Turn off BattlEye: Rockstar Games Launcher → Settings → GTA V Legacy; on Steam and Epic use the -nobattleye launch option."));
        Say(step + Text.Get(". Запустите GTA → Сюжетный режим → F9 → адрес сервера → Подключиться. Меню — J.",
            ". Start GTA → Story Mode → F9 → server address → Connect. Menu: J."));
        return 0;
    }

    // Public archive: an older download offers the new one before installing.
    // Returns false when the player chose to stop and download it.
    private static bool CheckForNewerRelease(Options options)
    {
        if (Environment.GetEnvironmentVariable(ProductInfo.UpdateCheckVariable) == "0") return true;
        Version? latest;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(ProductInfo.ReleasesApi);
            request.UserAgent = "OGCoop/" + ProductInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = request.ReadWriteTimeout = 5000;
            using var response = (HttpWebResponse)request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream()!, Encoding.UTF8);
            var buffer = new char[ReleaseCheck.MaximumResponse + 1];
            int length = reader.ReadBlock(buffer, 0, buffer.Length);
            latest = ReleaseCheck.Newest(new string(buffer, 0, length));
        }
        catch (Exception error) when (error is WebException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            Log("Update check skipped: " + error.Message);
            return true;
        }

        if (!ReleaseCheck.IsNewer(latest)) return true;
        Say(Text.Get("Вышла новая версия OG Coop " + latest!.ToString(3) + ", а это " + ProductInfo.Version + ". Скачайте новую:",
            "OG Coop " + latest!.ToString(3) + " is out; this is " + ProductInfo.Version + ". Download the new one:"));
        Say("  " + ProductInfo.Releases);
        if (options.Check) return true;
        Say(Text.Get("Enter — открыть страницу и выйти, Y — всё равно поставить эту версию.", "Enter — open the page and quit, Y — install this version anyway."));
        string? answer = Console.ReadLine();
        if (answer != null && (answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) || answer.Trim().StartsWith("д", StringComparison.OrdinalIgnoreCase))) return true;
        OpenPage(ProductInfo.Releases);
        return false;
    }

    private static string? Payload(string home) =>
        new[] { PrivatePayload, PublicPayload }.Select(name => Path.Combine(home, name)).FirstOrDefault(Directory.Exists);

    private static void WaitForGameClosed(Options options)
    {
        while (GameProcesses.Any(name => Process.GetProcessesByName(name).Length > 0))
        {
            if (options.Check)
            {
                Say(Text.Get("GTA сейчас запущена — перед установкой её нужно закрыть.", "GTA is running — close it before installing."));
                return;
            }

            Say(Text.Get("GTA запущена. Закройте игру и нажмите Enter.", "GTA is running. Close the game and press Enter."));
            if (Console.ReadLine() == null) throw new InstallerException(Text.Get("GTA не закрыта.", "GTA is still running."),
                Text.Get("Закройте GTA и запустите установщик снова.", "Close GTA and run the installer again."));
        }
    }

    private static GameFolder ChooseGame(Options options)
    {
        if (options.Game != null)
        {
            return GameFolder.Inspect(options.Game) ?? throw new InstallerException(
                Text.Get("В папке нет GTA5.exe: ", "No GTA5.exe in: ") + options.Game,
                Text.Get("Укажите папку, где лежит GTA5.exe.", "Choose the folder that contains GTA5.exe."));
        }

        Say(Text.Get("Ищу GTA V…", "Looking for GTA V…"));
        List<GameFolder> found = GameLocator.Find();
        if (found.Count == 1 && !found[0].IsSupported)
        {
            // In check mode the caller reports the reason. Installing, the player may choose another copy.
            if (options.Check) return found[0];
            Say(Text.Get("Нашёл: ", "Found: ") + found[0].Path);
            Say(Rejection(found[0])!.Message);
        }
        else if (found.Count == 1)
        {
            Say(Text.Get("Нашёл: ", "Found: ") + found[0].Path);
            if (options.Check) return found[0];
            Say(Text.Get("Ставлю сюда? Enter — да, N — указать другую папку.", "Install here? Enter — yes, N — choose another folder."));
            string? answer = Console.ReadLine();
            if (answer == null || !answer.Trim().StartsWith("n", StringComparison.OrdinalIgnoreCase) &&
                !answer.Trim().StartsWith("н", StringComparison.OrdinalIgnoreCase)) return found[0];
        }
        else if (found.Count > 1)
        {
            Say(Text.Get("Нашёл несколько копий игры:", "Found several game copies:"));
            for (int index = 0; index < found.Count; index++)
            {
                GameFolder item = found[index];
                string note = item.Kind == GameKind.Enhanced ? " — Enhanced" : item.IsSupported ? string.Empty : " — " + Text.Get("другая версия", "other version");
                Say("  " + (index + 1) + ". " + item.Path + note);
            }

            Say(Text.Get("Введите номер и нажмите Enter (просто Enter — 1, 0 — указать другую папку).",
                "Type a number and press Enter (Enter alone — 1, 0 — choose another folder)."));
            string? answer = options.Check ? null : Console.ReadLine();
            if (string.IsNullOrWhiteSpace(answer)) return found[0];
            if (int.TryParse(answer!.Trim(), out int choice) && choice >= 1 && choice <= found.Count) return found[choice - 1];
        }
        else
        {
            Say(Text.Get("Сам не нашёл папку игры.", "Could not find the game folder."));
        }

        while (true)
        {
            Say(Text.Get("Откройте папку игры, перетащите файл GTA5.exe в это окно и нажмите Enter (или вставьте путь к папке).",
                "Open the game folder, drag GTA5.exe into this window and press Enter (or paste the folder path)."));
            string? input = options.Check ? null : Console.ReadLine();
            if (input == null) throw new InstallerException(Text.Get("Папка игры не выбрана.", "No game folder was chosen."),
                Text.Get("Запустите установщик снова и укажите папку, где лежит GTA5.exe.", "Run the installer again and choose the folder with GTA5.exe."));
            GameFolder? folder = GameFolder.Inspect(input);
            InstallerException? rejected = folder == null ? null : Rejection(folder);
            if (folder != null && rejected == null) return folder;
            Say(rejected == null ? Text.Get("Здесь нет GTA5.exe, попробуйте ещё раз.", "No GTA5.exe there, try again.")
                : rejected.Message + " " + rejected.Hint + Text.Get(" Или укажите другую папку.", " Or choose another folder."));
        }
    }

    // Null when the copy is supported.
    internal static InstallerException? Rejection(GameFolder game)
    {
        if (game.Kind == GameKind.Enhanced)
            return new InstallerException(Text.Get("Это GTA V Enhanced, а нужна GTA V Legacy.", "This is GTA V Enhanced; GTA V Legacy is required."),
                Text.Get("Установите GTA V Legacy — в лаунчере это отдельная игра.", "Install GTA V Legacy — the launcher lists it as a separate game."));
        if (game.IsSupported) return null;
        return new InstallerException(
            Text.Get("Нужна GTA V Legacy 1.0." + GameFolder.SupportedBuild + ", а у вас ", "GTA V Legacy 1.0." + GameFolder.SupportedBuild + " is required; this game is ") +
            (game.Version?.ToString() ?? Text.Get("версия, которую не удалось прочитать", "an unreadable version")) + ".",
            Text.Get("Обновите игру в лаунчере. Под другую версию ScriptHookV и дополнение не подходят.",
                "Update the game in its launcher. ScriptHookV and the toolkit match this version only."));
    }

    private static bool CanWrite(string game)
    {
        string probe = Path.Combine(game, ".coop-write-test");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A player may move Downloads to another drive, so Windows is asked for the path.
    private static string DownloadsFolder(Options options) =>
        options.Downloads ?? KnownFolders.Downloads() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Program Files needs administrator rights. Windows is asked once and the
    // new window continues with the same choice.
    private static void Elevate(Options options, string game)
    {
        if (options.Elevated)
            throw new InstallerException(Text.Get("Нет доступа к папке игры: ", "No access to the game folder: ") + game,
                Text.Get("Проверьте, что игра не на защищённом диске, и запустите установщик снова.", "Check the drive permissions and run the installer again."));
        Say(Text.Get("Для записи в папку игры нужны права администратора — подтвердите запрос Windows.",
            "Writing to the game folder needs administrator rights — confirm the Windows prompt."));
        // The elevated window may run under another account, so this player's Downloads go along.
        string arguments = "--elevated --game \"" + game.TrimEnd('\\') + "\"" + (options.Language != null ? " --lang " + options.Language : string.Empty) +
            " --downloads \"" + DownloadsFolder(options).TrimEnd('\\') + "\"";
        try
        {
            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName, arguments) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            throw new InstallerException(Text.Get("Без прав администратора файлы в папку игры не записать.", "Files cannot be written to the game folder without administrator rights."),
                Text.Get("Запустите установщик снова и нажмите «Да» в запросе Windows.", "Run the installer again and choose Yes in the Windows prompt."), error);
        }

        Say(Text.Get("Продолжаю в новом окне.", "Continuing in a new window."));
        _handedOff = true;
    }

    private static void CheckPrerequisites(Options options)
    {
        if (!Prerequisites.NetFrameworkInstalled())
            throw new InstallerException(Text.Get("Нет .NET Framework 4.8.", ".NET Framework 4.8 is missing."),
                Text.Get("Установите его: ", "Install it: ") + Prerequisites.NetFrameworkUrl + Text.Get(", затем запустите установщик снова.", ", then run the installer again."));
        if (Prerequisites.VcRedistInstalled()) return;
        Say(Text.Get("Нет Microsoft Visual C++ (x64) — без него GTA с модами не запустится.", "Microsoft Visual C++ (x64) is missing — GTA with mods will not start without it."));
        if (options.Check) return;
        Say(Text.Get("Установить сейчас? Enter — да, N — нет.", "Install it now? Enter — yes, N — no."));
        string? answer = Console.ReadLine();
        if (answer != null && (answer.Trim().StartsWith("n", StringComparison.OrdinalIgnoreCase) || answer.Trim().StartsWith("н", StringComparison.OrdinalIgnoreCase)))
        {
            Say(Text.Get("Пропускаю. Установите позже: ", "Skipped. Install it later: ") + Prerequisites.VcRedistUrl);
            return;
        }

        Prerequisites.InstallVcRedist(Say);
    }

    private static void EnsureScriptHookV(Options options, string home, string game)
    {
        if (ScriptHookV.Installed(game)) return;
        string[] folders = { home, DownloadsFolder(options) };
        while (true)
        {
            string? zip = ScriptHookV.TryImport(game, folders, apply: !options.Check);
            if (zip != null)
            {
                Say(Text.Get("ScriptHookV взят из ", "ScriptHookV taken from ") + zip);
                return;
            }

            Say(Text.Get("Нужен ScriptHookV 3889.0 — его нельзя класть в архив, скачайте с сайта автора:",
                "ScriptHookV 3889.0 is needed — it may not be bundled, download it from its author:"));
            Say("  " + Prerequisites.ScriptHookVPage);
            Say(Text.Get("Нажмите Download, ZIP оставьте в «Загрузках» (распаковывать не нужно) и нажмите здесь Enter.",
                "Click Download, leave the ZIP in Downloads (no need to extract) and press Enter here."));
            if (options.Check) return;
            OpenPage(Prerequisites.ScriptHookVPage);
            if (Console.ReadLine() == null) throw new InstallerException(Text.Get("ScriptHookV не найден.", "ScriptHookV was not found."),
                Text.Get("Скачайте его и запустите установщик снова.", "Download it and run the installer again."));
        }
    }

    // Returns true when the player still has to run the OpenIV step.
    private static bool ReportVehicles(string home, string game)
    {
        string folder = Path.Combine(home, VehicleFolder);
        string? oiv = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.oiv").FirstOrDefault(path => Path.GetFileName(path).IndexOf("Install", StringComparison.OrdinalIgnoreCase) >= 0)
            : null;
        if (oiv == null) return false;
        Say(Text.Get("Проверяю машины…", "Checking the cars…"));
        VehicleStatus status = VehiclePack.Inspect(oiv, game);
        if (status.State == VehicleState.Current)
        {
            Say(Text.Get("Машины: уже стоят актуальные (", "Cars: already up to date (") + status.Expected + Text.Get("). OpenIV не нужен.", "). OpenIV is not needed."));
            return false;
        }

        Say(status.State switch
        {
            VehicleState.Outdated => Text.Get("Машины: нужно обновить через OpenIV.", "Cars: update them with OpenIV."),
            _ => Text.Get("Машины: нужно поставить через OpenIV (один раз, несколько минут).", "Cars: install them with OpenIV (once, a few minutes)."),
        });
        Say(Text.Get("  1. Скачайте и установите OpenIV: https://openiv.com", "  1. Download and install OpenIV: https://openiv.com"));
        Say(Text.Get("  2. OpenIV → GTA V → Windows → папка игры.", "  2. OpenIV → GTA V → Windows → the game folder."));
        if (status.State == VehicleState.NoModsLoader)
            Say(Text.Get("  3. Инструменты → ASI Manager → у «OpenIV.ASI» нажмите «Установить».", "  3. Tools → ASI Manager → OpenIV.ASI → Install."));
        Say(Text.Get("  " + (status.State == VehicleState.NoModsLoader ? 4 : 3) + ". Инструменты → Установщик пакетов → «" + VehicleFolder + "» → " + Path.GetFileName(oiv) +
                " → Установить → mods → Установить. Ждите «Установка завершена».",
            "  " + (status.State == VehicleState.NoModsLoader ? 4 : 3) + ". Tools → Package Installer → \"" + VehicleFolder + "\" → " + Path.GetFileName(oiv) +
                " → Install → mods → Install. Wait for \"Installation complete\"."));
        return true;
    }

    private static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // No browser association: the address is printed above.
        }
    }

    private static void OpenLog(string home)
    {
        try
        {
            _log = new StreamWriter(Path.Combine(home, "install-log.txt"), false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log = null;
        }
    }

    private static void Say(string line)
    {
        Console.WriteLine(line);
        Log(line);
    }

    private static void Log(string line) => _log?.WriteLine(line);

    private static void Fail(string message, string hint, Exception? cause)
    {
        Console.WriteLine();
        ConsoleColor color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Say(Text.Get("Ошибка: ", "Error: ") + message);
        Console.ForegroundColor = color;
        Say(Text.Get("Что сделать: ", "What to do: ") + hint);
        Say(_private
            ? Text.Get("Не помогло — пришлите файл install-log.txt из этой папки в Telegram " + ProductInfo.Telegram + " или создайте сообщение об ошибке: ",
                "Still stuck? Send install-log.txt from this folder to Telegram " + ProductInfo.Telegram + " or report the problem: ") + ProductInfo.NewIssue
            : Text.Get("Не помогло — создайте сообщение об ошибке и приложите файл install-log.txt из этой папки: ",
                "Still stuck? Report the problem and attach install-log.txt from this folder: ") + ProductInfo.NewIssue);
        if (cause != null) Log(cause.ToString());
    }
}

internal sealed class Options
{
    internal string? Game { get; private set; }
    internal string? Language { get; private set; }
    internal bool Check { get; private set; }
    internal bool NoPause { get; private set; }
    internal bool Elevated { get; private set; }
    internal string? Downloads { get; private set; }

    internal static Options Parse(string[] args)
    {
        var options = new Options();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--game" when index + 1 < args.Length: options.Game = args[++index]; break;
                case "--lang" when index + 1 < args.Length && (args[index + 1] == "ru" || args[index + 1] == "en"): options.Language = args[++index]; break;
                case "--check": options.Check = true; break;
                case "--no-pause": options.NoPause = true; break;
                case "--elevated": options.Elevated = true; break;
                case "--downloads" when index + 1 < args.Length: options.Downloads = args[++index]; break;
                default: throw new ArgumentException("Usage: CoopInstaller [--check] [--game <folder>] [--lang ru|en] [--no-pause] [--downloads <folder>]");
            }
        }

        return options;
    }
}
