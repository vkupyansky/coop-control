using OGCoop.Shared;
using GTA.Native;

namespace RageCoopAdmin.Client;

// GTA boundary kept separate from server/session logic; isolated checks capture
// these calls without pretending to execute the game's world or clock.
internal static class WorldStateNatives
{
    internal static void SetClock(int hour, int minute, int second) => Function.Call(Hash.SET_CLOCK_TIME, hour, minute, second);
    internal static void PauseClock(bool paused) => Function.Call(Hash.PAUSE_CLOCK, paused);
    internal static void ClearWeather()
    {
        Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
        Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
    }
    internal static void FreezeWeather(int index)
    {
        Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
        Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, WorldOptions.WeatherCodes[index]);
    }
    internal static void SelectWeather(int index)
    {
        ClearWeather();
        Function.Call(Hash.SET_WEATHER_TYPE_NOW, WorldOptions.WeatherCodes[index]);
    }
    internal static void ApplyNaturalWeather(int previous, int next, float blend) =>
        Function.Call((Hash)0x578C752848ECFA0CUL, previous, next, blend);

    internal static object[] Observe(int session, string epoch, long revision)
    {
        int rate = Function.Call<int>((Hash)0x2F8B4D1C595B11DBUL);
        using (var previous = new OutputArgument())
        using (var next = new OutputArgument())
        using (var blend = new OutputArgument())
        {
            Function.Call((Hash)0xF3BBE884A14BB413UL, previous, next, blend);
            return new object[] { session, epoch, revision,
                Function.Call<int>(Hash.GET_CLOCK_HOURS), Function.Call<int>(Hash.GET_CLOCK_MINUTES),
                Function.Call<int>(Hash.GET_CLOCK_SECONDS), previous.GetResult<int>(), next.GetResult<int>(), blend.GetResult<float>(), rate };
        }
    }
}
