using System.Globalization;

namespace LasReader.Core;

/// <summary>Reads a single numeric well-log dataset from LAS 1.2, 2.0 or 3.0.</summary>
public static class LasFileReader
{
    /// <summary>Reads a LAS document without closing the supplied reader.</summary>
    /// <exception cref="FormatException">The log is malformed or uses an unsupported version or data type.</exception>
    public static LasDocument Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // In LAS 1.2 the version section may follow the well or curve definitions.
        // Buffer only up to VERS so the (potentially very large) sample section streams normally.
        var prefix = new List<string>();
        LasVersion version = ReadVersion(reader, prefix, out int lineNumber);
        var parser = new Parser(version);
        int headerLine = lineNumber - prefix.Count;
        foreach (string line in prefix)
            parser.Accept(line, ++headerLine);
        prefix.Clear();
        while (reader.ReadLine() is { } line)
            parser.Accept(line, ++lineNumber);
        return parser.Finish(lineNumber);
    }

    private static LasVersion ReadVersion(TextReader reader, List<string> prefix, out int lineNumber)
    {
        bool inVersion = false;
        lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            string line = lineNumber == 1 ? raw.TrimStart('\uFEFF') : raw;
            var text = LasSectionTitle.UnquoteMarker(line.AsSpan().Trim());
            if (prefix.Count == 0 && (text.IsEmpty || text[0] != '~'))
                continue;
            prefix.Add(line);
            if (text.IsEmpty || text[0] == '#')
                continue;

            if (text[0] == '~')
            {
                var title = text[1..].Trim();
                var kind = LasSectionTitle.LegacyKind(title);
                int separator = title.IndexOfAny(" \t|[");
                var name = separator < 0 ? title : title[..separator];
                if (kind == LasSection.Data || name.Equals("Log_Data", StringComparison.OrdinalIgnoreCase))
                    break;
                inVersion = kind == LasSection.Version;
                continue;
            }

            int dot = text.IndexOf('.');
            if (!inVersion || dot < 0 || !text[..dot].Trim().Equals("VERS", StringComparison.OrdinalIgnoreCase))
                continue;
            var field = text[(dot + 1)..].TrimStart();
            int colon = field.IndexOf(':');
            field = (colon < 0 ? field : field[..colon]).Trim();
            if (decimal.TryParse(field, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out decimal value))
            {
                switch (value)
                {
                    case 1.2m: return LasVersion.V12;
                    case 2m: return LasVersion.V20;
                    case 3m: return LasVersion.V30;
                }
            }
            throw Error(lineNumber, $"Unsupported VERS '{field}'. Expected 1.2, 2.0 or 3.0.");
        }
        throw Error(lineNumber, "Missing VERS in the LAS version section before log data.");
    }

    private static FormatException Error(int line, string message) => new($"LAS line {line}: {message}");

    private sealed class Parser(LasVersion version)
    {
        private readonly List<LasHeaderEntry> versionInfo = [];
        private readonly List<LasHeaderEntry> wellInfo = [];
        private readonly List<LasHeaderEntry> curveInfo = [];
        private readonly List<LasHeaderEntry> parameterInfo = [];
        private readonly List<LasRawSection> extraSections = [];
        private LasSection section;
        private List<string>? extraLines;
        private string? definition;
        private string? logParameter;
        private string nullToken = "-999.25";
        private char delimiter = ' ';
        private bool wrapped;
        private bool sawVers;
        private LasDataReader? data;

        internal void Accept(string raw, int lineNumber)
        {
            var line = raw.AsSpan().Trim();
            if (version == LasVersion.V12)
                line = LasSectionTitle.UnquoteMarker(line);

            if (!line.IsEmpty && line[0] == '~')
            {
                if (section == LasSection.Data)
                    data!.Finish(lineNumber);
                BeginSection(LasSectionTitle.Parse(line, version, lineNumber), raw, lineNumber);
                return;
            }
            if (extraLines is not null)
            {
                extraLines.Add(raw);
                return;
            }
            if (line.StartsWith("#", StringComparison.Ordinal))
                return;
            if (section == LasSection.Data)
            {
                // Leading/trailing TABs may be actual empty columns.
                if (!line.IsEmpty || (delimiter == '\t' && raw.Contains('\t')))
                    data!.ReadLine(raw.AsSpan(), lineNumber);
                return;
            }
            if (line.IsEmpty || section == LasSection.Other)
                return;

            var entry = LasHeaderParser.Parse(line, version, section, lineNumber);
            switch (section)
            {
                case LasSection.Version:
                    versionInfo.Add(entry);
                    ReadOption(entry, lineNumber);
                    break;
                case LasSection.Well:
                    wellInfo.Add(entry);
                    if (entry.Mnemonic.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        nullToken = entry.Value;
                    break;
                case LasSection.Curve:
                    curveInfo.Add(entry);
                    break;
                case LasSection.Parameter:
                    parameterInfo.Add(entry);
                    break;
            }
        }

        private void ReadOption(LasHeaderEntry entry, int lineNumber)
        {
            switch (entry.Mnemonic.ToUpperInvariant())
            {
                case "VERS":
                    if (sawVers)
                        throw Error(lineNumber, "Multiple VERS declarations are ambiguous.");
                    sawVers = true;
                    break;
                case "WRAP":
                    wrapped = entry.Value.ToUpperInvariant() switch
                    {
                        "YES" => true, "NO" => false,
                        _ => throw Error(lineNumber, "WRAP must be YES or NO.")
                    };
                    break;
                case "DLM" when version == LasVersion.V30:
                    delimiter = entry.Value.ToUpperInvariant() switch
                    {
                        "" or "SPACE" => ' ', "COMMA" => ',', "TAB" => '\t',
                        _ => throw Error(lineNumber, "DLM must be SPACE, COMMA, or TAB.")
                    };
                    break;
            }
        }

        private void BeginSection(LasSectionTitle title, string raw, int lineNumber)
        {
            section = title.Kind;
            extraLines = null;
            if (section == LasSection.Other)
            {
                extraLines = [];
                extraSections.Add(new LasRawSection(raw, extraLines.AsReadOnly()));
                return;
            }
            if (data is not null)
                throw Error(lineNumber, section == LasSection.Data
                    ? "Multiple log datasets are unsupported. Open one log dataset at a time."
                    : $"Unexpected section '{title.Name}' after log data. Additional log definitions are unsupported.");

            switch (section)
            {
                case LasSection.Parameter when version == LasVersion.V30:
                    if (logParameter is not null)
                        throw Error(lineNumber, "Multiple log parameter sections are unsupported.");
                    logParameter = title.Name;
                    break;
                case LasSection.Curve:
                    if (definition is not null)
                        throw Error(lineNumber, "Multiple log curve definitions are unsupported.");
                    definition = title.Name;
                    break;
                case LasSection.Data:
                    if (!sawVers || curveInfo.Count == 0)
                        throw Error(lineNumber, "Declare VERS and at least one curve before the log data section.");
                    if (version == LasVersion.V30)
                        ValidateAssociation(title, lineNumber);
                    data = new LasDataReader(curveInfo.Count, version, delimiter, wrapped, nullToken, lineNumber);
                    break;
            }
        }

        private void ValidateAssociation(LasSectionTitle title, int lineNumber)
        {
            bool legacy = title.Name.Equals("ASCII", StringComparison.OrdinalIgnoreCase);
            string suffix = legacy ? "" : title.Name["Log_Data".Length..];
            // An indexed log may explicitly reference a shared, unindexed definition.
            string expected = legacy ? "Curve" : "Log_Definition" + suffix;
            if (!legacy && title.Association.Equals("Log_Definition", StringComparison.OrdinalIgnoreCase))
                expected = "Log_Definition";
            if (!string.Equals(definition, expected, StringComparison.OrdinalIgnoreCase) ||
                ((!legacy || title.Association.Length > 0) &&
                 !title.Association.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw Error(lineNumber, $"The {title.Name} association must refer to the matching {expected} section.");
            if (logParameter is not null && logParameter.Contains('[') &&
                !logParameter.Equals("Log_Parameter" + suffix, StringComparison.OrdinalIgnoreCase))
                throw Error(lineNumber, $"Log parameter association '{logParameter}' does not match {title.Name}.");
        }

        internal LasDocument Finish(int lineNumber)
        {
            if (data is null)
                throw Error(lineNumber, "No supported numeric log data section (~ASCII or ~Log_Data) was found.");
            data.Finish(lineNumber);
            var curves = new LasCurve[curveInfo.Count];
            for (int i = 0; i < curves.Length; i++)
                curves[i] = new LasCurve(curveInfo[i], data.Columns[i], data.ExactValues[i]);
            var warnings = LasIndexValidation.Check(wellInfo, curves[0], version, nullToken);
            if (data.ExactValues.Any(values => values is { Count: > 0 }))
                warnings.Add("Some values have more decimal precision than the plot can display.");
            return new LasDocument(versionInfo, wellInfo, parameterInfo, curves, data.NullValue, extraSections, warnings);
        }
    }
}
