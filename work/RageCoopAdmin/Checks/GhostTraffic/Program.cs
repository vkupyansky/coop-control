using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OGCoop.Shared;
using GTA.Math;
using GTA.Native;
using HarmonyLib;
using RageCoopAdmin.Client;

internal static partial class Program
{
    private const string Epoch = "0123456789abcdef0123456789abcdef";
    private static readonly List<(Hash Native, ulong[] Args)> NativeCalls = new List<(Hash, ulong[])>();
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static int _checks;

    private static int Main()
    {
        try
        {
            CheckProtocol();
            CheckInstalledWireCodecs();
            CheckGlobalPairs();
            CheckAuthorityAndSessions();
            CheckLifecycle();
            CheckCamera();
            CheckProvisionalTraffic();
            CheckSolidContactLog();
            CheckBudgetsAndCadence();
            CheckRealNativeBoundary();
            CheckRealRuntimeClassification();
            Console.WriteLine($"Ghost traffic: {_checks} checks passed; real controller/protocol and native write boundary tested, live GTA physics not simulated.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckProtocol()
    {
        object[] report = GhostTrafficProtocol.EncodeReport(3, Epoch, 1, new[] { new GhostTrafficClaim(-1, -2, true), new GhostTrafficClaim(8, 7, false) });
        Assert(GhostTrafficProtocol.TryReadReport(report, out int session, out string epoch, out long sequence, out GhostTrafficClaim[] claims) &&
            session == 3 && epoch == Epoch && sequence == 1 && claims.Length == 2 && claims[0].Id == -1 && claims[0].ModelHash == -2 && claims[0].Ambient && !claims[1].Ambient,
            "Wire round trip preserves signed engine IDs/model hashes and explicit protected promotions.");
        object[] state = GhostTrafficProtocol.EncodeState(3, Epoch, 5, new[] { new GhostTrafficEntry(-1, -2, -3) });
        Assert(GhostTrafficProtocol.TryReadState(state, out _, out _, out long revision, out GhostTrafficEntry[] entries) && revision == 5 && entries[0].OwnerId == -3,
            "Registry round trip preserves current owner identity.");
        Assert(!GhostTrafficProtocol.TryReadReport(new object[] { 3, Epoch, 1L, new int[] { 1, 2 } }, out _, out _, out _, out _), "Unsupported int[] cannot masquerade as the installed custom-event byte payload.");
        Assert(!GhostTrafficProtocol.TryReadReport(new object[] { 3, Epoch, 1L, new byte[13] }, out _, out _, out _, out _), "Truncated triples fail atomically.");
        Assert(!GhostTrafficProtocol.TryReadReport(new object[] { 3, Epoch, 1L, new byte[(GhostTrafficProtocol.MaximumClaims + 1) * 12] }, out _, out _, out _, out _), "Oversized reports fail before allocation of entries.");
        Assert(!GhostTrafficProtocol.TryReadState(new object[] { 3, Epoch, 1L, new byte[(GhostTrafficProtocol.MaximumEntries + 1) * 12] }, out _, out _, out _, out _), "Oversized registries fail before allocation of entries.");
        report = GhostTrafficProtocol.EncodeReport(3, Epoch, 1, new[] { new GhostTrafficClaim(1, 2, true), new GhostTrafficClaim(1, 3, false) });
        Assert(!GhostTrafficProtocol.TryReadReport(report, out _, out _, out _, out _), "Duplicate conflicting IDs reject the complete report.");
        foreach (object[] malformed in new[]
        {
            new object[] { 0, Epoch, 1L, Array.Empty<byte>() }, new object[] { 3, "wrong", 1L, Array.Empty<byte>() },
            new object[] { 3, Epoch, -1L, Array.Empty<byte>() }, new object[] { 3, Epoch, 1, Array.Empty<byte>() }
        }) Assert(!GhostTrafficProtocol.TryReadReport(malformed, out _, out _, out _, out _), "Invalid session, epoch, sequence or numeric type cannot establish authority.");
    }

    private static void CheckGlobalPairs()
    {
        var fixture = new Fixture();
        GhostTrafficBody local = fixture.Add(1, 1, player: true, owner: 10, local: true);
        GhostTrafficBody remote = fixture.Add(2, 4, player: true, owner: 20);
        fixture.Add(3, 2, ambient: true, local: true, owner: 10);
        fixture.Add(4, 3, owner: 20);
        fixture.Controller.Tick();
        Assert(fixture.Runtime.Scans == 0 && fixture.Reports.Count == 0 && fixture.Runtime.Writes.Count == 0, "Default-off performs no scans, reports or native writes.");
        fixture.Configure(true);
        fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 2 && fixture.Runtime.Writes.All(pair => pair.Traffic == 3),
            "Newly appeared local ambient traffic passes provisionally; remote traffic without an NPC driver waits for the server.");
        fixture.Set(3, 30, local: true); fixture.Now = 3000; fixture.Frame();
        fixture.Set(3, 2, local: true); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "After the short window, native ambient population alone cannot bypass server acceptance.");
        fixture.Accept(new GhostTrafficEntry(3, 103, 10), new GhostTrafficEntry(4, 104, 20));
        fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 4 && fixture.Runtime.Writes.Any(pair => pair.Player == remote.Vehicle.Id && pair.Traffic == 3) &&
            fixture.Runtime.Writes.Any(pair => pair.Player == local.Vehicle.Id && pair.Traffic == 4),
            "One client's simulation protects both player drivers against both local-owned and remote-owned confirmed traffic.");
        Assert(fixture.Runtime.Writes.All(pair => pair.Player <= 2 && pair.Traffic >= 3), "No player-player pair is submitted.");
        Assert(GhostTrafficProtocol.TryReadReport(fixture.Reports[0], out _, out _, out _, out GhostTrafficClaim[] claims) &&
            claims.Length == 2 && claims.Single(claim => claim.Id == 1).Ambient == false && claims.Single(claim => claim.Id == 3).Ambient,
            "Only local-owner source classifications and protected player promotions are reported.");
    }

    private static void CheckAuthorityAndSessions()
    {
        var fixture = new Fixture(); fixture.Add(1, 0, player: true); fixture.Add(2, 10, owner: 20);
        fixture.Configure(true); fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 1, "Current epoch/session registry enables an accepted pair.");
        Assert(!fixture.Controller.ApplyRegistry(GhostTrafficProtocol.EncodeState(99, Epoch, 100, new[] { new GhostTrafficEntry(2, 102, 20) })), "A previous F9 session cannot replace the current registry.");
        Assert(!fixture.Controller.ApplyRegistry(GhostTrafficProtocol.EncodeState(3, "1123456789abcdef0123456789abcdef", 100, Array.Empty<GhostTrafficEntry>())), "Another server epoch cannot replace current authority despite its greater revision.");
        Assert(!fixture.Controller.ApplyRegistry(GhostTrafficProtocol.EncodeState(3, Epoch, 1, Array.Empty<GhostTrafficEntry>())), "Duplicate/stale revisions cannot erase accepted traffic.");
        fixture.Accept(new GhostTrafficEntry(2, 999, 20)); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "A mismatched model cannot authorize a non-overlapping pair.");
        fixture.Accept(new GhostTrafficEntry(2, 102, 99)); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "Stale ownership cannot authorize a fresh pair.");
        fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        GhostTrafficBody old = fixture.Runtime.Bodies[2];
        fixture.Runtime.Bodies[2] = fixture.Body(new GhostTrafficVehicle(2, 102, old.Vehicle.Handle, new IntPtr(9999), new object(), new object()), 10, owner: 20);
        fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "A native handle reused for another identity is rejected before collision writes.");
        fixture.Controller.Configure(true, "1123456789abcdef0123456789abcdef", 8); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "Reconnecting clears old registry authority even if game IDs repeat.");
        fixture = new Fixture(); fixture.Add(1, 0, player: true); fixture.Add(2, 1, owner: 20);
        fixture.Configure(true); fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        fixture.Accept(); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "An authoritative protected-vehicle removal overrides even a previous overlap while the mode remains enabled.");
    }

    private static void CheckLifecycle()
    {
        var fixture = new Fixture(); fixture.Add(1, 0, player: true); fixture.Add(2, 1, owner: 20);
        fixture.Configure(true); fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        fixture.Configure(false); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 1 && fixture.Controller.HasPendingPairs, "Off retains only the already intersecting pair, without velocity or position writes.");
        fixture.Add(3, 1, owner: 20); fixture.Accept(new GhostTrafficEntry(2, 102, 20), new GhostTrafficEntry(3, 103, 20)); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 1 && fixture.Runtime.Writes[0].Traffic == 2, "Off cannot add newly streamed traffic pairs while draining.");
        fixture.Set(2, 20); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0 && !fixture.Controller.HasPendingPairs, "Once body bounds clear, current-frame exclusion expires naturally.");
        fixture.Configure(true); fixture.Set(2, 1); fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        fixture.Controller.Stop(); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 1, "Ordinary disconnect can finish an existing overlap while the host continues ticks.");
        fixture.Set(2, 1, player: true); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 0, "A player entering former traffic immediately cancels even an overlap drain.");
        fixture.Controller.Clear(); fixture.Frame();
        Assert(!fixture.Controller.HasPendingPairs && fixture.Runtime.Clears == 1 && fixture.Runtime.Writes.Count == 0, "Final unload removes all retained bodies/registries and leaves no permanent collision state.");
    }

    private static void CheckCamera()
    {
        var fixture = new Fixture();
        fixture.Add(1, 0, player: true, local: true); fixture.Add(2, 200, player: true, owner: 20);
        fixture.Add(3, 1, owner: 20); fixture.Add(4, 5, owner: 20); fixture.Add(5, 201, owner: 20);
        fixture.Runtime.CameraHandle = Key(1).Handle;
        fixture.Frame();
        Assert(fixture.Runtime.CameraQueries == 0 && fixture.Runtime.CameraIgnores.Count == 0, "Default-off neither queries the camera vehicle nor changes camera collision.");
        fixture.Configure(true); fixture.Frame();
        Assert(fixture.Runtime.CameraQueries == 0 && fixture.Runtime.CameraIgnores.Count == 0, "Without accepted traffic pairs the camera keeps ordinary collision.");
        fixture.Accept(new GhostTrafficEntry(3, 103, 20), new GhostTrafficEntry(4, 104, 20), new GhostTrafficEntry(5, 105, 20)); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 3 && fixture.Runtime.CameraQueries == 1,
            "All passable pairs are still suppressed and the camera vehicle is read once per frame.");
        Assert(fixture.Runtime.CameraIgnores.SequenceEqual(new[] { 4, 3 }),
            "The camera ignores only traffic passable for its own vehicle, nearest last; another player's traffic keeps camera collision.");
        fixture.Runtime.CameraHandle = 0; fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 3 && fixture.Runtime.CameraIgnores.Count == 0, "On foot the camera keeps ordinary collision while drivers still pass traffic.");
        fixture.Runtime.CameraHandle = Key(2).Handle; fixture.Frame();
        Assert(fixture.Runtime.CameraIgnores.SequenceEqual(new[] { 5 }), "A passenger's camera follows the partner's car and ignores that car's passable traffic.");
        fixture.Runtime.CameraHandle = Key(1).Handle;
        fixture.Configure(false); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == 2 && fixture.Runtime.CameraIgnores.SequenceEqual(new[] { 3 }),
            "Turning off keeps camera exclusion only for the overlap still being drained with its own car.");
        fixture.Set(3, 20); fixture.Set(5, 300); fixture.Frame();
        Assert(fixture.Runtime.CameraIgnores.Count == 0 && fixture.Runtime.CameraQueries == 0, "After bodies clear, camera collision returns with no retained state.");
        fixture.Configure(true); fixture.Set(3, 1); fixture.Frame();
        fixture.Set(3, 1, player: true); fixture.Frame();
        Assert(!fixture.Runtime.CameraIgnores.Contains(3) && fixture.Runtime.CameraIgnores.SequenceEqual(new[] { 4 }),
            "Traffic a player enters is no longer passable or ignored by the camera.");
    }

    private static void CheckProvisionalTraffic()
    {
        var fixture = new Fixture();
        fixture.Add(1, 0, player: true, local: true);
        GhostTrafficBody Driven(int id, float x, bool protectedVehicle = false, bool known = true, bool driver = true, float speed = 0f) =>
            fixture.Runtime.Bodies[id] = new GhostTrafficBody(fixture.Runtime.Bodies.TryGetValue(id, out GhostTrafficBody? old) ? old.Vehicle : Key(id),
                new Vector3(x, 0, 0), 2, 20, false, false, protectedVehicle, false, false, driver, known, speed: speed);
        fixture.Configure(true); fixture.Frame();
        Driven(2, 1);
        fixture.Now = 100; fixture.Frame();
        Assert(fixture.Runtime.Scans == 2 && fixture.Runtime.Writes.SequenceEqual(new[] { (1, 2) }),
            "Another game's NPC-driven car appearing next to the player is passable within one 100 ms scan, before server confirmation.");
        Driven(3, 1.5f, protectedVehicle: true); Driven(4, 2, known: false); Driven(5, 2.5f, driver: false);
        fixture.Now = 200; fixture.Frame();
        Assert(fixture.Runtime.Writes.SequenceEqual(new[] { (1, 2) }),
            "Player-used, unclassified or driverless (parked or created) cars never pass without the server.");
        fixture.Now = 3500; fixture.Frame();
        Assert(fixture.Runtime.Writes.SequenceEqual(new[] { (1, 2) }), "An expired window never ejects a player from inside the car.");
        Driven(2, 30); fixture.Frame(); Driven(2, 1); fixture.Frame();
        Assert(fixture.Runtime.Writes.SequenceEqual(new[] { (1, 2) }),
            "Another game's car driven by an NPC stays passable after the window, without waiting for its owner's confirmation.");
        Driven(7, 1.2f, driver: false, speed: 10f); Driven(8, 1.4f, driver: false); fixture.Now = 3620; fixture.Frame();
        Assert(fixture.Runtime.Writes.OrderBy(pair => pair.Traffic).SequenceEqual(new[] { (1, 2), (1, 7) }),
            "A moving car of another game with no player in it is passable; a standing empty one (parked or created) waits for the server.");
        fixture.Runtime.Bodies.Remove(7); fixture.Runtime.Bodies.Remove(8); Driven(2, 30); fixture.Frame();
        fixture.Accept(new GhostTrafficEntry(2, 102, 20)); fixture.Frame();
        Assert(fixture.Runtime.Writes.SequenceEqual(new[] { (1, 2) }), "Server confirmation then keeps the car passable.");
        fixture.Configure(false); fixture.Frame(); Driven(2, 30); fixture.Frame();
        fixture.Configure(true); Driven(6, 1); fixture.Now = 3600; fixture.Frame();
        Assert(fixture.Runtime.Writes.OrderBy(pair => pair.Traffic).SequenceEqual(new[] { (1, 2), (1, 6) }),
            "Turning the mode back on gives newly seen NPC traffic a fresh window beside confirmed traffic.");
    }

    private static void CheckSolidContactLog()
    {
        var all = new List<string>();
        var log = new List<string>();
        var fixture = new Fixture(line => { all.Add(line); if (line.Contains("solid traffic touched")) log.Add(line); });
        fixture.Add(1, 0, player: true, local: true);
        fixture.Add(2, 1, local: true);
        fixture.Add(3, 30, owner: 20);
        fixture.Configure(true); fixture.Frame(); fixture.Frame();
        Assert(all.Count(line => line == "OG Coop: traffic pass-through on") == 1,
            "Turning the mode on is logged once, so a report can tell whether it was on.");
        Assert(log.Count == 1 && log[0].Contains("id=2") && log[0].Contains("reason=not road traffic") && log[0].Contains("owner=local"),
            "A touched solid car is logged once with its reason; distant cars are not logged.");
        fixture.Runtime.Bodies[3] = fixture.Body(fixture.Runtime.Bodies[3].Vehicle, 1, owner: 20);
        fixture.Accept(new GhostTrafficEntry(3, 103, 20)); fixture.Frame();
        Assert(log.Count == 1, "A passable touched car is not logged.");
        fixture.Runtime.Bodies[3] = fixture.Body(fixture.Runtime.Bodies[3].Vehicle, 30, owner: 20);
        fixture.Controller.Clear();
        fixture.Configure(true); fixture.Frame();
        Assert(log.Count == 2 && log[1].Contains("id=2"), "After a full reset the same solid car can be explained again.");
    }

    private static void CheckBudgetsAndCadence()
    {
        var fixture = new Fixture();
        for (int id = 1; id <= 64; id++) fixture.Add(id, 0, player: true);
        for (int id = 65; id <= 128; id++) fixture.Add(id, 1, owner: 20);
        fixture.Configure(true); fixture.Accept(Enumerable.Range(65, 64).Select(id => new GhostTrafficEntry(id, 100 + id, 20)).ToArray()); fixture.Frame();
        Assert(fixture.Runtime.Writes.Count == GhostTrafficController.MaximumPairs && fixture.Runtime.LastMaximum == GhostTrafficController.MaximumBodies,
            "Dense scenes cap native pairs and candidate-body capture.");
        fixture.Frame(); Assert(fixture.Runtime.Writes.Count == GhostTrafficController.MaximumPairs, "Intersecting drain plus refreshed candidates cannot grow beyond the pair budget.");
        Assert(fixture.Runtime.Scans == 1 && fixture.Reports.Count == 1, "Held mode reuses the candidate scan and does not report per frame.");
        fixture.Now = 250; fixture.Frame(); Assert(fixture.Runtime.Scans == 2 && fixture.Reports.Count == 1, "Candidate cache refreshes on its bounded interval, while unchanged reports stay quiet.");
        fixture.Now = 1000; fixture.Frame(); Assert(fixture.Reports.Count == 2, "A bounded heartbeat repairs a missed classification without per-frame network traffic.");
        fixture = new Fixture(); fixture.Add(1, 0, player: true); fixture.Add(2, 1, ambient: true, local: true);
        fixture.Configure(true); fixture.Frame(); fixture.Now = 250; fixture.Set(2, 1, player: true, local: true); fixture.Frame();
        Assert(fixture.Reports.Count == 2 && GhostTrafficProtocol.TryReadReport(fixture.Reports[1], out _, out _, out _, out GhostTrafficClaim[] changed) &&
            !changed.Single(claim => claim.Id == 2).Ambient, "Player adoption promptly promotes a former NPC instead of repeatedly advertising it as traffic.");
        fixture = new Fixture(); fixture.Add(1, 0, player: true); fixture.Add(2, 1, owner: 20); fixture.Add(3, 500, owner: 20);
        fixture.Configure(true); fixture.Frame();
        fixture.Runtime.Reads.Clear(); fixture.Frame();
        Assert(fixture.Runtime.Reads.OrderBy(id => id).SequenceEqual(new[] { 1, 2 }), "Between scans only player drivers and cars within pairing distance are read again.");
        fixture.Now = 100; fixture.Runtime.Reads.Clear(); fixture.Frame();
        Assert(fixture.Runtime.Reads.Contains(3), "A distant car is read again at the next scan.");
        fixture.Accept(new GhostTrafficEntry(3, 103, 20)); fixture.Set(3, 1); fixture.Now = 200; fixture.Frame();
        Assert(fixture.Runtime.Writes.SequenceEqual(new[] { (1, 3) }), "A confirmed car that comes near is paired by the next 100 ms scan, long before contact.");
    }

    private static void CheckRealNativeBoundary()
    {
        Assert(GhostTrafficRuntime.IsAvailable, "The deployed engine exposes the exact entity/ped pool signatures required for classification.");
        var harmony = new Harmony("friendslegacy.check.ghost-native-boundary");
        MethodInfo target = typeof(GhostTrafficRuntime).GetMethod(nameof(GhostTrafficRuntime.SuppressThisFrame))!;
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Program), nameof(CaptureNativeCalls)));
        try
        {
            GhostTrafficVehicle player = Key(1), traffic = Key(2);
            GhostTrafficBody Body(GhostTrafficVehicle key, params int[] riders) =>
                new GhostTrafficBody(key, Vector3.Zero, 2, 10, true, false, false, false, false, false, riders: riders);
            new GhostTrafficRuntime().SuppressThisFrame(Body(player), Body(traffic));
            Assert(NativeCalls.Count == 2 && NativeCalls.All(call => call.Native == Hash.SET_ENTITY_NO_COLLISION_ENTITY && call.Args.Length == 3 && call.Args[2] == 1),
                "Actual runtime writes only the pair-native, with thisFrameOnly=true in both calls.");
            Assert(NativeCalls[0].Args[0] == (ulong)player.Handle && NativeCalls[0].Args[1] == (ulong)traffic.Handle &&
                NativeCalls[1].Args[0] == (ulong)traffic.Handle && NativeCalls[1].Args[1] == (ulong)player.Handle,
                "The production native adapter applies both argument orders; no world/player collision flag is disabled.");
            NativeCalls.Clear();
            new GhostTrafficRuntime().SuppressThisFrame(Body(player, 501, 502), Body(traffic, 601));
            // The game keeps one no-collision partner per entity: the last write wins.
            var partner = new Dictionary<ulong, ulong>();
            foreach (var call in NativeCalls) partner[call.Args[0]] = call.Args[1];
            Assert(NativeCalls.Count == 5 && NativeCalls.All(call => call.Native == Hash.SET_ENTITY_NO_COLLISION_ENTITY && call.Args[2] == 1) &&
                partner[(ulong)traffic.Handle] == (ulong)player.Handle && partner[601] == (ulong)player.Handle &&
                partner[(ulong)player.Handle] == (ulong)traffic.Handle && partner[501] == (ulong)traffic.Handle && partner[502] == (ulong)traffic.Handle,
                "On a motorcycle the traffic car and its biker end the frame naming the motorcycle, and the motorcycle, rider and passenger name the car.");
            NativeCalls.Clear();
            var secondTraffic = Key(3);
            new GhostTrafficRuntime().SuppressThisFrame(Body(player, 501), Body(traffic));
            new GhostTrafficRuntime().SuppressThisFrame(Body(player, 501), Body(secondTraffic));
            partner.Clear();
            foreach (var call in NativeCalls) partner[call.Args[0]] = call.Args[1];
            Assert(partner[(ulong)traffic.Handle] == (ulong)player.Handle && partner[(ulong)secondTraffic.Handle] == (ulong)player.Handle,
                "With two cars around a motorcycle, both still name the motorcycle after the frame's last pair, so it passes through each.");
            MethodInfo camera = typeof(GhostTrafficRuntime).GetMethod(nameof(GhostTrafficRuntime.IgnoreCameraCollisionThisFrame))!;
            harmony.Patch(camera, transpiler: new HarmonyMethod(typeof(Program), nameof(CaptureNativeCalls)));
            NativeCalls.Clear();
            new GhostTrafficRuntime().IgnoreCameraCollisionThisFrame(traffic);
            Assert(NativeCalls.Count == 1 && NativeCalls[0].Native == Hash.SET_GAMEPLAY_CAM_IGNORE_ENTITY_COLLISION_THIS_UPDATE &&
                NativeCalls[0].Args.Length == 1 && NativeCalls[0].Args[0] == (ulong)traffic.Handle,
                "The production camera adapter writes only the current-update gameplay-camera exclusion for that traffic vehicle.");
        }
        finally
        {
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(typeof(GhostTrafficRuntime).GetMethod(nameof(GhostTrafficRuntime.IgnoreCameraCollisionThisFrame))!, HarmonyPatchType.All, harmony.Id);
        }
    }

    private static IEnumerable<CodeInstruction> CaptureNativeCalls(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction source in instructions)
        {
            var code = new CodeInstruction(source);
            if (code.operand is MethodInfo method && method.DeclaringType == typeof(Function) && method.Name == nameof(Function.Call))
                code.operand = typeof(Program).GetMethod(method.GetParameters().Length == 2 ? nameof(CaptureNative1) : nameof(CaptureNative), Static)!;
            yield return code;
        }
    }
    private static void CaptureNative(Hash hash, InputArgument first, InputArgument second, InputArgument third) => NativeCalls.Add((hash, new[] { first, second, third }.Select(arg => (ulong)typeof(InputArgument).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(arg)!).ToArray()));
    private static void CaptureNative1(Hash hash, InputArgument first) => NativeCalls.Add((hash, new[] { (ulong)typeof(InputArgument).GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(first)! }));
    private static GhostTrafficVehicle Key(int id) => new GhostTrafficVehicle(id, 100 + id, 1000 + id, new IntPtr(10000 + id), new object(), new object());
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("PASS " + message); }

    private sealed class Fixture
    {
        internal readonly FakeRuntime Runtime = new FakeRuntime();
        internal readonly List<object[]> Reports = new List<object[]>();
        internal readonly GhostTrafficController Controller;
        internal long Now;
        private long _revision;
        internal Fixture(Action<string>? log = null) { Controller = new GhostTrafficController(Runtime, args => Reports.Add(args), () => Now, 1000, log); }
        internal GhostTrafficBody Body(GhostTrafficVehicle key, float x, bool player = false, int owner = 10, bool local = false, bool ambient = false, bool protectedVehicle = false) =>
            new GhostTrafficBody(key, new Vector3(x, 0, 0), 2, owner, local, ambient, protectedVehicle || player, player, player, player);
        internal GhostTrafficBody Add(int id, float x, bool player = false, int owner = 10, bool local = false, bool ambient = false)
        { GhostTrafficBody body = Body(Key(id), x, player, owner, local, ambient); Runtime.Bodies[id] = body; return body; }
        internal void Set(int id, float x, bool player = false, bool local = false)
        { GhostTrafficBody old = Runtime.Bodies[id]; Runtime.Bodies[id] = Body(old.Vehicle, x, player, old.OwnerId, local, old.AmbientOrigin); }
        internal void Configure(bool enabled) => Controller.Configure(enabled, Epoch, 3);
        internal void Accept(params GhostTrafficEntry[] entries) => Assert(Controller.ApplyRegistry(GhostTrafficProtocol.EncodeState(3, Epoch, ++_revision, entries)), "Current registry snapshot accepted.");
        internal void Frame() { Runtime.Writes.Clear(); Runtime.CameraIgnores.Clear(); Runtime.CameraQueries = 0; Controller.Tick(); }
    }

    private sealed class FakeRuntime : IGhostTrafficRuntime
    {
        internal readonly Dictionary<int, GhostTrafficBody> Bodies = new Dictionary<int, GhostTrafficBody>();
        internal readonly List<(int Player, int Traffic)> Writes = new List<(int, int)>();
        internal int Scans, Clears, LastMaximum;
        public GhostTrafficVehicle[] Capture(int maximum) { Scans++; LastMaximum = maximum; return Bodies.Values.Take(maximum).Select(body => body.Vehicle).ToArray(); }
        internal readonly List<int> Reads = new List<int>();
        public bool TryRead(GhostTrafficVehicle key, out GhostTrafficBody? body)
        {
            Reads.Add(key.Id);
            if (Bodies.TryGetValue(key.Id, out body) && ReferenceEquals(body.Vehicle, key)) return true;
            body = null; return false;
        }
        public void SuppressThisFrame(GhostTrafficBody player, GhostTrafficBody traffic) => Writes.Add((player.Vehicle.Id, traffic.Vehicle.Id));
        internal readonly List<int> CameraIgnores = new List<int>();
        internal int CameraHandle, CameraQueries;
        public int CameraVehicleHandle() { CameraQueries++; return CameraHandle; }
        public void IgnoreCameraCollisionThisFrame(GhostTrafficVehicle traffic) => CameraIgnores.Add(traffic.Id);
        public void Clear() { Clears++; }
    }
}
