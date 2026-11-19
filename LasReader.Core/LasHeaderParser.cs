using System.Globalization;
using System.Text.RegularExpressions;

namespace LasReader.Core;

internal static class LasHeaderParser
{
    private static readonly HashSet<string> LegacyWellTextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMP", "WELL", "FLD", "LOC", "PROV", "CNTY", "STAT", "CTRY", "SRVC", "DATE", "UWI", "API"
    };

    private static readonly HashSet<string> LegacySpacedUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "M", "CM", "MM", "F", "FT", "IN", "DEGC", "DEGF", "K/M3", "KG/M3", "G/C3", "G/CM3",
        "S", "MS", "US", "US/M", "US/FT", "OHMM", "C3", "V/V", "VOL/VOL", "MV", "API", "GAPI",
        "PSI", "KPA", "MPA", "%"
    };

    private static readonly Regex ScalarNumericFormat = new(
        @"\A(?:F(?:[0-9]+\.[0-9]+)?|I[0-9]*|E(?:0+(?:\.0+)?E\+0+)?)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static LasHeaderEntry Parse(ReadOnlySpan<char> line, LasVersion version, LasSection section, int lineNumber)
    {
        string? format = null;
        string? association = null;
        if (version == LasVersion.V30)
        {
            // Set aside the format before looking for the last colon, since time formats use colons.
            int bar = line.LastIndexOf('|');
            if (bar >= 0 && line[..bar].Contains(':'))
            {
                association = line[(bar + 1)..].Trim().ToString();
                line = line[..bar];
            }

            int closingBrace = line.LastIndexOf('}');
            int openingBrace = closingBrace < 0 ? -1 : line[..closingBrace].LastIndexOf('{');
            if (openingBrace >= 0)
            {
                format = line[(openingBrace + 1)..closingBrace].Trim().ToString();
                line = line[..openingBrace];
            }
        }

        int colon = version == LasVersion.V12 ? line.IndexOf(':') : line.LastIndexOf(':');
        ReadOnlySpan<char> fields = colon < 0 ? line : line[..colon];
        int dot = fields.IndexOf('.');
        if (dot < 0 || fields[..dot].Trim().IsEmpty)
            throw new FormatException($"Line {lineNumber}: Expected a LAS header entry in the form MNEMONIC.UNIT VALUE : DESCRIPTION.");

        string mnemonic = fields[..dot].Trim().ToString();
        ReadOnlySpan<char> unitAndValue = fields[(dot + 1)..];
        int unitEnd = TokenLength(unitAndValue);
        ReadOnlySpan<char> unit = unitAndValue[..unitEnd];
        ReadOnlySpan<char> value = unitAndValue[unitEnd..].Trim();
        ReadOnlySpan<char> description = colon < 0 ? [] : line[(colon + 1)..].Trim();

        if (version == LasVersion.V12)
        {
            if (section == LasSection.Well && LegacyWellTextFields.Contains(mnemonic) && colon >= 0)
            {
                ReadOnlySpan<char> label = value;
                value = description;
                description = label;
            }
            else if (unit.IsEmpty && section is LasSection.Well or LasSection.Parameter or LasSection.Curve)
            {
                // Keep unitless descriptions such as "Rig 17" intact. Curve units may precede API codes.
                int tokenEnd = TokenLength(value);
                ReadOnlySpan<char> candidate = value[..tokenEnd];
                ReadOnlySpan<char> number = value[tokenEnd..].TrimStart();
                bool indexHeader = section == LasSection.Well && mnemonic.ToUpperInvariant() is "STRT" or "STOP" or "STEP";
                if (!candidate.IsEmpty && (indexHeader || LegacySpacedUnits.Contains(candidate.ToString())) &&
                    (section == LasSection.Curve || (double.TryParse(number, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed))))
                {
                    unit = candidate;
                    value = number;
                }
            }
        }

        if (version == LasVersion.V30 && section == LasSection.Curve)
        {
            if (mnemonic.EndsWith(']') && mnemonic.Contains('['))
                throw new FormatException($"Line {lineNumber}: LAS 3.0 channel '{mnemonic}' is an unsupported array channel.");
            if (!string.IsNullOrEmpty(format) && !ScalarNumericFormat.IsMatch(format))
                throw new FormatException($"Line {lineNumber}: LAS 3.0 channel '{mnemonic}' has unsupported format '{{{format}}}'. Only scalar numeric channels are supported.");
        }

        return new LasHeaderEntry(mnemonic, unit.ToString(), value.ToString(), description.ToString())
        {
            Format = format,
            Association = association
        };
    }

    private static int TokenLength(ReadOnlySpan<char> value)
    {
        int length = 0;
        while (length < value.Length && !char.IsWhiteSpace(value[length]))
            length++;

        return length;
    }
}
