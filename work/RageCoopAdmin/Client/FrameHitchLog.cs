using System;
using System.Globalization;
using System.Text;
using CoopControl.Shared;
using GTA;
using GTA.Math;
using GTA.Native;
using RageCoop.Client.Scripting;

namespace RageCoopAdmin.Client;

// Context for the freeze log (FrameHitchMonitor): where the player is, how fast
// and how high, how full the world is and which of our own loading steps ran.
// "pending loads" is the game's queue of models and map pieces still to stream in.
public sealed partial class AdminMenuScript
{
    private const long LoadsWindowMilliseconds = 2_000;
    private readonly FrameHitchMonitor _frameMonitor = new FrameHitchMonitor(message => API.Logger.Info(message));
    private readonly int[] _collections = new int[3];
    private readonly int[] _collected = new int[3];
    private long _loadsWindowStart;
    private int _loadsWindow;
    private int _loadsBefore;

    private void TickFrameMonitor()
    {
        long now = NowMilliseconds();
        int loads = Function.Call<int>(Hash.GET_NUMBER_OF_STREAMING_REQUESTS);
        for (int generation = 0; generation < 3; generation++)
        {
            int count = GC.CollectionCount(generation);
            _collected[generation] = count - _collections[generation];
            _collections[generation] = count;
        }

        _frameMonitor.Frame(now, loads, WindowFocusClock.Take(), FrameContext);
        EngineTickTimer.Sample();
        // Pending loads over the last two to four seconds: a freeze is logged after it ends.
        if (now - _loadsWindowStart >= LoadsWindowMilliseconds)
        {
            _loadsBefore = _loadsWindow;
            _loadsWindow = 0;
            _loadsWindowStart = now;
        }

        _loadsWindow = Math.Max(_loadsWindow, loads);
    }

    private string FrameContext()
    {
        var text = new StringBuilder(200);
        Ped player = Game.Player.Character;
        if (player != null && player.Exists())
        {
            Vector3 at = player.Position;
            Vehicle? vehicle = player.CurrentVehicle;
            Entity moving = vehicle != null && vehicle.Exists() ? vehicle : player;
            text.Append("at ").Append(Whole(at.X)).Append(',').Append(Whole(at.Y)).Append(',').Append(Whole(at.Z))
                .Append("; speed ").Append(Whole(moving.Speed * 3.6f)).Append(" km/h; above ground ")
                .Append(Whole(moving.HeightAboveGround)).Append(" m");
            if (moving is Vehicle car) text.Append("; vehicle ").Append(car.DisplayName);
            text.Append("; ");
        }

        text.Append("world vehicles ").Append(World.GetAllVehicles().Length)
            .Append(", people ").Append(World.GetAllPeds().Length)
            .Append(", objects ").Append(World.GetAllProps().Length);
        if (Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE)) text.Append("; pause menu");
        if (_mapLoad != null) text.Append("; map loading");
        if (_pendingTeleport != null) text.Append("; teleport");
        if (_specialPlaces.IslandShown) text.Append("; over island");
        // Who held the frame: RageCoop's sync, its world script, the resources'
        // tick (ours among them); none of them long means the game itself stood still.
        text.Append("; work RageCoop ").Append(EngineTickTimer.Main).Append(" ms, world ").Append(EngineTickTimer.World)
            .Append(" ms, menus ").Append(EngineTickTimer.Resources).Append(" ms; pending loads before ")
            .Append(Math.Max(_loadsBefore, _loadsWindow)).Append("; GC ").Append(_collected[0]).Append('/')
            .Append(_collected[1]).Append('/').Append(_collected[2]);
        return text.ToString();
    }

    private static string Whole(float value) =>
        Finite.Is(value) ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture) : "?";
}
