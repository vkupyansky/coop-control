using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using CoopAdmin.Server;

int checks = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL " + message);
    checks++;
    Console.WriteLine("PASS " + message);
}

string root = Path.Combine(Path.GetTempPath(), "coop-server-control-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string[] english = ServerBanner.Lines(4499, new[] { IPAddress.Parse("192.168.1.20"), IPAddress.Parse("100.101.102.103") }, russian: false);
    Assert(english[0].Contains("Coop Control") && english[0].Contains("Vladislav Kupyansky (vkupyansky)") && english[0].Contains("@skibidessa"),
        "The start banner names the product, the author and the contact.");
    Assert(english.Contains("  on this PC: 127.0.0.1:4499") && english.Contains("  on the same or a virtual network: 192.168.1.20:4499") &&
        english.Contains("  on the same or a virtual network: 100.101.102.103:4499") && english.Last().Contains("forward UDP 4499"),
        "The banner lists the addresses friends type, with the port, and how to play over the internet.");
    string[] russian = ServerBanner.Lines(5000, Array.Empty<IPAddress>(), russian: true);
    Assert(russian[1] == "Адрес для друзей:" && russian.Any(line => line.Contains("127.0.0.1:5000")) && russian.Last().Contains("UDP 5000"),
        "A Russian host gets the same banner in Russian with the configured port.");
    Assert(ServerBanner.IsUsable(IPAddress.Parse("10.0.0.5")) && !ServerBanner.IsUsable(IPAddress.Parse("169.254.10.1")) &&
        !ServerBanner.IsUsable(IPAddress.Loopback) && !ServerBanner.IsUsable(IPAddress.Parse("fe80::1")),
        "Self-assigned, loopback and IPv6 addresses are not offered to friends.");

    string settings = Path.Combine(root, "Settings.xml");
    File.WriteAllText(settings, "<Settings><Port>5123</Port></Settings>");
    Assert(ServerBanner.ReadPort(settings) == 5123, "The port is read from Settings.xml.");
    File.WriteAllText(settings, "<Settings><Port>99999</Port></Settings>");
    Assert(ServerBanner.ReadPort(settings) == 4499, "An impossible port falls back to the default.");
    File.WriteAllText(settings, "<Settings><Port>");
    Assert(ServerBanner.ReadPort(settings) == 4499 && ServerBanner.ReadPort(Path.Combine(root, "missing.xml")) == 4499,
        "A broken or missing settings file falls back to the default port.");
    File.WriteAllText(settings, "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Settings><Port>&e;</Port></Settings>");
    Assert(ServerBanner.ReadPort(settings) == 4499, "Settings with a DTD are not processed.");

    string releases = "[{\"url\":\"x\",\"author\":{\"login\":\"a\"},\"tag_name\":\"v0.9.0-wip\",\"name\":\"d\",\"draft\":true,\"prerelease\":true}," +
        "{\"tag_name\":\"v0.3.1-wip\",\"draft\":false,\"prerelease\":true,\"assets\":[{\"name\":\"a.zip\"}]}," +
        "{\"tag_name\":\"v0.2.0-wip\",\"draft\":false,\"prerelease\":true},{\"tag_name\":\"nightly\",\"draft\":false}]";
    Assert(CoopControl.Shared.ReleaseCheck.Newest(releases) == new Version(0, 3, 1),
        "The newest published release is found among pre-releases; drafts and tags that are not versions are ignored.");
    Assert(CoopControl.Shared.ReleaseCheck.IsNewer(new Version(0, 3, 1)) && !CoopControl.Shared.ReleaseCheck.IsNewer(CoopControl.Shared.ReleaseCheck.Current) &&
        !CoopControl.Shared.ReleaseCheck.IsNewer(null) && CoopControl.Shared.ReleaseCheck.Current == new Version(0, 3, 0),
        "Only a newer release is offered; this version and a failed check are not.");
    Assert(CoopControl.Shared.ReleaseCheck.Newest("[]") == null && CoopControl.Shared.ReleaseCheck.Newest("not json") == null &&
        CoopControl.Shared.ReleaseCheck.Newest(new string(' ', CoopControl.Shared.ReleaseCheck.MaximumResponse + 1)) == null,
        "An empty, broken or oversized answer offers nothing.");

    ServerProcessFile.Create(root);
    string pidFile = Path.Combine(root, ServerProcessFile.Name);
    string pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
    Assert(File.ReadAllText(pidFile) == pid, "The server writes its process number beside RageCoop.Server.dll for the stop button.");
    File.WriteAllText(pidFile, "12345");
    ServerProcessFile.Remove(pidFile, pid);
    Assert(File.Exists(pidFile), "Exit does not remove a file that belongs to another server process.");
    File.WriteAllText(pidFile, pid);
    ServerProcessFile.Remove(pidFile, pid);
    Assert(!File.Exists(pidFile), "A normal exit removes its own process file.");
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("Server control checks passed: " + checks);
