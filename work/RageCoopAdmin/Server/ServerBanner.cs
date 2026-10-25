using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml;
using CoopControl.Shared;

namespace CoopAdmin.Server;

// What the host sees when the server starts: who made it and which addresses
// to give friends. RageCoop's own list prints every interface, IPv6 included.
internal static class ServerBanner
{
    internal const string Contacts = "github.com/vkupyansky · Telegram " + ProductInfo.Telegram;
    internal const int DefaultPort = 4499;

    internal static string[] Lines(int port, IEnumerable<IPAddress> local, bool russian)
    {
        var lines = new List<string>
        {
            ProductInfo.Name + " " + ProductInfo.Version + (russian ? " — для RageCoop. Автор: " : " — for RageCoop. By ") + ProductInfo.Author + " · " + Contacts,
            russian ? "Адрес для друзей:" : "Address for friends:",
            (russian ? "  на этом компьютере: " : "  on this PC: ") + "127.0.0.1:" + port,
        };
        foreach (IPAddress address in local)
            lines.Add((russian ? "  в той же или виртуальной сети: " : "  on the same or a virtual network: ") + address + ":" + port);
        lines.Add(russian
            ? "  через интернет: ваш внешний адрес:" + port + " — откройте порт UDP " + port + " на роутере или используйте Tailscale, ZeroTier, playit.gg (README → «Как подключаются игроки»)"
            : "  over the internet: your public address:" + port + " — forward UDP " + port + " on the router or use Tailscale, ZeroTier, playit.gg (README → Connecting players)");
        return lines.ToArray();
    }

    internal static IPAddress[] LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .Where(IsUsable)
                .Distinct()
                .Take(8)
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<IPAddress>();
        }
    }

    internal static bool IsUsable(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        byte[] bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254) && bytes[0] != 0;
    }

    // RageCoop runs from its own folder; a missing or broken file means the default port.
    internal static int ReadPort(string settings)
    {
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(settings, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                document.Load(reader);
            string? text = document.DocumentElement?["Port"]?.InnerText;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port > 0 && port <= 65535 ? port : DefaultPort;
        }
        catch (Exception error) when (error is IOException or XmlException or UnauthorizedAccessException)
        {
            return DefaultPort;
        }
    }

    // COOP_CONTROL_LANGUAGE=ru|en overrides the system language: a terminal's
    // locale is often English on a Russian computer.
    internal static bool Russian => Environment.GetEnvironmentVariable("COOP_CONTROL_LANGUAGE") switch
    {
        "ru" => true,
        "en" => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru",
    };
}
