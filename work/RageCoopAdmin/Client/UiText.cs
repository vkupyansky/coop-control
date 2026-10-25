using System;
using System.Globalization;
using System.Resources;

namespace RageCoopAdmin.Client;

internal static class UiText
{
    private static readonly ResourceManager English = new ResourceManager("RageCoopAdmin.Client.Assets.MenuEnglish", typeof(UiText).Assembly);
    [ThreadStatic] private static UiLanguage? renderingLanguage;
    internal static UiLanguage Language { get; private set; }
    internal static bool IsEnglish => (renderingLanguage ?? Language) == UiLanguage.English;

    internal static void SetLanguage(UiLanguage language)
    {
        if (language != UiLanguage.Russian && language != UiLanguage.English)
            throw new ArgumentOutOfRangeException(nameof(language));
        Language = language;
    }

    internal static string Get(string source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        return IsEnglish
            ? English.GetString(source, CultureInfo.InvariantCulture) ?? source : source;
    }

    internal static string Format(string source, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, Get(source), values);

    // Text of a build's own optional files, kept beside that code instead of
    // the shared catalog.
    internal static string Pick(string russian, string english) => IsEnglish ? english : russian;

    // Snapshot both renderings now: bindings never retain callbacks to a changing
    // loop variable or old player/entity. Override is thread-local, not global.
    internal static UiTextPair Capture(Func<string> render)
    {
        UiLanguage? previous = renderingLanguage;
        try
        {
            renderingLanguage = UiLanguage.Russian;
            string russian = render();
            renderingLanguage = UiLanguage.English;
            string english = render();
            return new UiTextPair(russian, english);
        }
        finally { renderingLanguage = previous; }
    }

    internal static UiTextPair[] CaptureOptions(Func<string[]> render)
    {
        UiLanguage? previous = renderingLanguage;
        try
        {
            renderingLanguage = UiLanguage.Russian;
            string[] russian = render();
            renderingLanguage = UiLanguage.English;
            string[] english = render();
            if (russian.Length != english.Length) throw new InvalidOperationException("Localized option counts must agree.");
            var result = new UiTextPair[russian.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new UiTextPair(russian[i], english[i]);
            return result;
        }
        finally { renderingLanguage = previous; }
    }
}

internal sealed class UiTextPair
{
    internal UiTextPair(string russian, string english) { Russian = russian; English = english; }
    internal string Russian { get; }
    internal string English { get; }
    internal string Value => UiText.Language == UiLanguage.English ? English : Russian;
}
