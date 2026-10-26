using System;
using System.Globalization;

namespace OGCoop.Installer;

// Russian on a Russian Windows, English everywhere else.
internal static class Text
{
    internal static bool Russian { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    internal static string Get(string russian, string english) => Russian ? russian : english;
}

internal sealed class InstallerException : Exception
{
    internal InstallerException(string message, string hint, Exception? inner = null) : base(message, inner) => Hint = hint;

    internal string Hint { get; }
}
