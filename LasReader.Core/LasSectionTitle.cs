namespace LasReader.Core;

internal enum LasVersion { V12, V20, V30 }
internal enum LasSection { Other, Version, Well, Curve, Parameter, Data }

internal readonly record struct LasSectionTitle(LasSection Kind, string Name, string Association = "")
{
    internal static LasSectionTitle Parse(ReadOnlySpan<char> marker, LasVersion version, int lineNumber)
    {
        ReadOnlySpan<char> title = marker[1..].Trim();
        if (version != LasVersion.V30)
            return new(LegacyKind(title), title.ToString());

        int pipe = title.IndexOf('|');
        var words = (pipe < 0 ? title : title[..pipe]).Trim();
        int end = 0;
        while (end < words.Length && !char.IsWhiteSpace(words[end]))
            end++;
        string name = words[..end].ToString();
        string association = pipe < 0 ? "" : title[(pipe + 1)..].Trim().TrimStart('~').ToString();
        int bracket = name.IndexOf('[');
        string stem = bracket < 0 ? name : name[..bracket];
        var kind = stem.ToUpperInvariant() switch
        {
            "VERSION" => LasSection.Version,
            "WELL" => LasSection.Well,
            "CURVE" or "LOG_DEFINITION" => LasSection.Curve,
            "PARAMETER" or "LOG_PARAMETER" => LasSection.Parameter,
            "ASCII" or "LOG_DATA" => LasSection.Data,
            _ => LasSection.Other
        };
        if (bracket >= 0 && kind != LasSection.Other &&
            (!stem.StartsWith("Log_", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(']') ||
             !int.TryParse(name.AsSpan(bracket + 1, name.Length - bracket - 2),
                 System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                 out int number) || number < 1))
            throw new FormatException($"LAS line {lineNumber}: Invalid indexed section '{name}'.");
        return new(kind, name, association);
    }

    internal static LasSection LegacyKind(ReadOnlySpan<char> title)
    {
        if (title.IsEmpty)
            return LasSection.Other;
        // Only uppercase initials are reserved. Also accept common lowercase exporter spellings.
        int end = 0;
        while (end < title.Length && !char.IsWhiteSpace(title[end]))
            end++;
        ReadOnlySpan<char> word = title[..end];
        return title[0] switch
        {
            'V' => LasSection.Version, 'W' => LasSection.Well, 'C' => LasSection.Curve,
            'P' => LasSection.Parameter, 'A' => LasSection.Data,
            'v' when IsAlias(word, "Version") => LasSection.Version,
            'w' when IsAlias(word, "Well") => LasSection.Well,
            'c' when IsAlias(word, "Curve") => LasSection.Curve,
            'p' when IsAlias(word, "Parameter") => LasSection.Parameter,
            'a' when IsAlias(word, "Ascii") => LasSection.Data,
            _ => LasSection.Other
        };
    }

    private static bool IsAlias(ReadOnlySpan<char> word, string fullName) =>
        word.Length == 1 || word.Equals(fullName, StringComparison.OrdinalIgnoreCase);

    // LAS 1.2 section and comment flags can be preceded by quotation marks.
    internal static ReadOnlySpan<char> UnquoteMarker(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty || line[0] != '"')
            return line;
        var unquoted = line.Trim('"').Trim();
        return !unquoted.IsEmpty && unquoted[0] is '~' or '#' ? unquoted : line;
    }
}
