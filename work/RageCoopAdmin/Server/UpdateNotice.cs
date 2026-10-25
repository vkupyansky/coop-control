using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CoopControl.Shared;
using RageCoop.Server;

namespace CoopAdmin.Server;

// Public build only: on start the server asks GitHub for the newest release.
// When this one is older, the host sees it in the window and every player who
// joins gets one chat line, so an outdated server gets noticed. The private
// build is updated by its owner and never points players to the public one.
public sealed partial class CoopAdminServer
{
#if PUBLIC_RELEASE
    private volatile string? newerRelease;
#endif

    internal static string[] UpdateLines(string latest, bool russian) => russian
        ? new[] { "Вышла новая версия " + ProductInfo.Name + " " + latest + " (у этого сервера " + ProductInfo.Version + ").", "Скачать: " + ProductInfo.Releases + " — остановите сервер, замените файлы, Settings.xml и данные игроков оставьте." }
        : new[] { ProductInfo.Name + " " + latest + " is out (this server runs " + ProductInfo.Version + ").", "Download: " + ProductInfo.Releases + " — stop the server, replace the files, keep Settings.xml and player data." };

    internal static string UpdateChat(string latest) =>
        ProductInfo.Name + " " + latest + " is out, this server runs " + ProductInfo.Version + " — host, please update: " + ProductInfo.Releases +
        " · Вышла новая версия, владельцу сервера стоит обновиться.";

    private void StartUpdateCheck()
    {
#if PUBLIC_RELEASE
        if (Environment.GetEnvironmentVariable(ProductInfo.UpdateCheckVariable) == "0") return;
        _ = Task.Run(CheckForUpdate);
#endif
    }

#if PUBLIC_RELEASE
    private async Task CheckForUpdate()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = ReleaseCheck.MaximumResponse };
            using var request = new HttpRequestMessage(HttpMethod.Get, ProductInfo.ReleasesApi);
            request.Headers.UserAgent.ParseAdd("CoopControl/" + ProductInfo.Version);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Debug("Coop Control update check: GitHub answered " + (int)response.StatusCode);
                return;
            }

            Version? latest = ReleaseCheck.Newest(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!ReleaseCheck.IsNewer(latest)) return;
            newerRelease = latest!.ToString(3);
            foreach (string line in UpdateLines(newerRelease, ServerBanner.Russian)) Logger.Warning(line);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Offline or GitHub unreachable: the server works the same without the notice.
            try { Logger.Debug("Coop Control update check skipped: " + error.Message); }
            catch (ObjectDisposedException)
            {
                // The server stopped while GitHub was answering.
            }
        }
    }
#endif

    private void NotifyOutdated(Client client)
    {
#if PUBLIC_RELEASE
        if (newerRelease is string latest) API.SendChatMessage(UpdateChat(latest), new List<Client> { client }, ProductInfo.Name, null);
#endif
    }
}
