using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using GTA.UI;
using HarmonyLib;
using LemonUI.Menus;

internal static partial class Program
{
    private static void CheckMenu(Harmony boundary)
    {
        const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assembly resource = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RageCoopAdmin.Client.dll"));
        Type script = resource.GetType("RageCoopAdmin.Client.AdminMenuScript", true)!;
        Type controllerType = resource.GetType("RageCoopAdmin.Client.WorldStateController", true)!;
        Type natives = resource.GetType("RageCoopAdmin.Client.WorldStateNatives", true)!;
        foreach (MethodInfo native in natives.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            boundary.Patch(native, prefix: new HarmonyMethod(typeof(Program), nameof(Native)));
        boundary.Patch(typeof(NativeMenu).GetMethod("UpdateItems", methods)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        boundary.Patch(typeof(NativeMenu).GetMethod("Recalculate", methods, null, Type.EmptyTypes, null)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        foreach (Type element in new[] { typeof(LemonUI.Elements.ScaledText), typeof(LemonUI.Elements.ScaledTexture), typeof(LemonUI.Elements.ScaledRectangle) })
            boundary.Patch(element.GetMethod("Recalculate", methods, null, Type.EmptyTypes, null)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        boundary.Patch(typeof(LemonUI.Elements.ScaledTexture).GetMethod("Request", methods)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        boundary.Patch(typeof(LemonUI.Elements.ScaledText).GetProperty("Width")!.GetGetMethod()!, prefix: new HarmonyMethod(typeof(Program), nameof(UiTextWidth)));
        Type extensions = typeof(NativeMenu).Assembly.GetType("LemonUI.Tools.Extensions", true)!;
        boundary.Patch(extensions.GetMethod("ToRelative", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(System.Drawing.PointF) }, null)!,
            prefix: new HarmonyMethod(typeof(Program), nameof(UiPoint)));
        boundary.Patch(typeof(LemonUI.Scaleform.InstructionalButton).GetConstructor(new[] { typeof(string), typeof(GTA.Control) })!,
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        boundary.Patch(typeof(LemonUI.Scaleform.InstructionalButtons).GetMethod("Add", methods)!, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        boundary.Patch(typeof(LemonUI.Scaleform.BaseScaleform).GetConstructor(methods, null, new[] { typeof(string) }, null)!,
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));
        foreach (MethodInfo notification in typeof(Notification).GetMethods().Where(method => method.Name == "PostTicker"))
            boundary.Patch(notification, prefix: new HarmonyMethod(typeof(Program), nameof(SkipUiBoundary)));

        object instance = FormatterServices.GetUninitializedObject(script);
        object Get(string name) => script.GetField(name, methods)!.GetValue(instance)!;
        void Set(string name, object value) => script.GetField(name, methods)!.SetValue(instance, value);
        object? CallScript(string name, params object[] args) => script.GetMethod(name, methods)!.Invoke(instance, args);
        Set("_worldActions", Activator.CreateInstance(script.GetField("_worldActions", methods)!.FieldType)!);
        Set("_weatherItems", Activator.CreateInstance(script.GetField("_weatherItems", methods)!.FieldType)!);
        var transport = new NativeMenu("Check", "Transport");
        var weather = new NativeMenu("Check", "Weather");
        var time = new NativeMenu("Check", "Time");
        CallScript("BuildWorldControls", transport, weather, time);
        NativeCheckboxItem ghost = (NativeCheckboxItem)Get("_ghostTrafficItem");
        NativeCheckboxItem clock = (NativeCheckboxItem)Get("_freezeTimeItem");
        NativeCheckboxItem sky = (NativeCheckboxItem)Get("_freezeWeatherItem");
        Check(new[] { ghost, clock, sky }.All(item => !item.Enabled && !item.Checked) && transport.Items.Contains(ghost) &&
            weather.Items.Contains(sky) && time.Items.Contains(clock), "the actual assembled menu places traffic under Transport and independent time/weather freezes in their submenus, all default-off and disabled before confirmation");
        var sends = new List<Tuple<int, object[]>>();
        object actualController = Activator.CreateInstance(controllerType, methods, null,
            new object[] { new Action<int, object[]>((kind, args) => sends.Add(Tuple.Create(kind, args))) }, null)!;
        Set("_worldState", actualController);
        Set("_worldConnected", true);
        EventInfo changed = controllerType.GetEvent("Changed", methods)!;
        changed.GetAddMethod(true)!.Invoke(actualController, new object[] { Delegate.CreateDelegate(changed.EventHandlerType!, instance, script.GetMethod("OnWorldChanged", methods)!) });
        object? CallController(string name, params object[] args) => controllerType.GetMethod(name, methods)!.Invoke(actualController, args);
        try
        {
            CallController("Start");
            int session = (int)controllerType.GetProperty("SessionToken", methods)!.GetValue(actualController)!;
            string epoch = new string('c', 32);
            object[] Wire(long revision, int request, bool accepted, int kind, bool enabled) => new object[]
                { session, epoch, revision, request, accepted, kind, false, false, enabled, -1, 0, 0, -1 };
            CallController("Receive", new object[] { Wire(0, 0, true, 0, false) });
            Check(new[] { ghost, clock, sky }.All(item => item.Enabled && !item.Checked), "the actual menu enables shared actions only after the matching server state");
            ghost.Checked = true;
            if ((int)controllerType.GetProperty("PendingRequestId", methods)!.GetValue(actualController)! == 0)
                ((Delegate)typeof(NativeCheckboxItem).GetField("CheckboxChanged", methods)!.GetValue(ghost)!).DynamicInvoke(ghost, EventArgs.Empty);
            object[] request = sends.Last(value => value.Item1 == 20774143).Item2;
            int token = (int)request[1];
            Check((int)request[3] == 5 && (int)request[4] == 0 && (int)request[5] == 0 && (bool)request[6] &&
                !ghost.Checked && new[] { ghost, clock, sky }.All(item => !item.Enabled),
                "the actual traffic toggle sends canonical values and restores confirmed-off display while its request is pending");
            CallController("Receive", new object[] { Wire(1, token, true, 5, true) });
            Check(ghost.Checked && !clock.Checked && !sky.Checked && new[] { ghost, clock, sky }.All(item => item.Enabled),
                "a real correlated server acknowledgement updates only the matching confirmed checkbox and re-enables the menu");
            ghost.Checked = false;
            if ((int)controllerType.GetProperty("PendingRequestId", methods)!.GetValue(actualController)! == 0)
                ((Delegate)typeof(NativeCheckboxItem).GetField("CheckboxChanged", methods)!.GetValue(ghost)!).DynamicInvoke(ghost, EventArgs.Empty);
            int rejected = (int)sends.Last(value => value.Item1 == 20774143).Item2[1];
            CallController("Receive", new object[] { Wire(1, rejected, false, 5, true) });
            Check(ghost.Checked && ghost.Enabled, "a refused real menu request retains the last confirmed shared mode rather than optimistic local success");
            CallScript("CleanupWorldControls");
            Check(script.GetField("_worldState", methods)!.GetValue(instance) == null && !(bool)Get("_worldConnected") &&
                ((IList)Get("_worldActions")).Count == 0,
                "actual menu cleanup stops its controller, drops subscriptions and clears action references");
        }
        finally { CallController("Stop"); }
    }

    private static bool SkipUiBoundary() => false;
    private static bool UiTextWidth(ref float __result) { __result = 0.1f; return false; }
    private static bool UiPoint(System.Drawing.PointF __0, ref System.Drawing.PointF __result) { __result = __0; return false; }
}
