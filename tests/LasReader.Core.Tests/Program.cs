using LasReader.Core;

(string Name, Action Check)[] checks =
[
    (nameof(Version20HeadersAndAliases), Version20HeadersAndAliases),
    (nameof(Version12QuotedMarkersAndLateVersion), Version12QuotedMarkersAndLateVersion),
    (nameof(Version30LegacyNames), Version30LegacyNames),
    (nameof(Version30LongNames), Version30LongNames),
    ("Version30CommaFields", () => MissingDelimitedFields("COMMA", ",")),
    ("Version30TabFields", () => MissingDelimitedFields("TAB", "\t")),
    (nameof(UnsupportedTypes), UnsupportedTypes),
    (nameof(InvalidAssociations), InvalidAssociations),
    (nameof(IndexedLog), IndexedLog),
    (nameof(IndexedLogWithSharedDefinition), IndexedLogWithSharedDefinition),
    (nameof(AdditionalSections), AdditionalSections),
    (nameof(PreciseNullComparison), PreciseNullComparison),
    (nameof(PreciseScientificNotation), PreciseScientificNotation),
    (nameof(InvalidWrappedRecords), InvalidWrappedRecords),
    (nameof(DescendingWrappedRecords), DescendingWrappedRecords),
    (nameof(VariableStepRecords), VariableStepRecords),
    (nameof(IndexHeaderWarnings), IndexHeaderWarnings),
    (nameof(RequiredVersion), RequiredVersion),
];

int failures = 0;
foreach ((string name, Action check) in checks)
{
    try
    {
        check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void Version20HeadersAndAliases()
{
    LasDocument log = Read("""
        ~version information
        VERS. 2.0 : LAS version
        WRAP. NO : One row per sample
        DLM. COMMA : Producer setting
        ~well information
        WELL. North: A-1 : Well name
        DATE. 2026-11-19T10:11:12 : Log timestamp
        ~calculated notes
        Text from a user-defined section.
        ~curve information
        DEPT.M : Depth
        GR.API : Gamma ray
        ~ascii log data
        1 10
        2 20
        """);

    Equal(new LasHeaderEntry("WELL", "", "North: A-1", "Well name"), log.WellInfo[0]);
    Equal(new LasHeaderEntry("DATE", "", "2026-11-19T10:11:12", "Log timestamp"), log.WellInfo[1]);
    Equal("COMMA", log.VersionInfo.Single(entry => entry.Mnemonic == "DLM").Value);
    Equal(2, log.Curves.Count);
    Samples(log, 0, 1, 2);
    Samples(log, 1, 10, 20);
}

static void Version12QuotedMarkersAndLateVersion()
{
    LasDocument log = Read("""
          "~Well Information"
        WELL. WELL: Alder-01
        COMP. COMPANY: Alder Logging
        STRT.M 1 : Start
        NULL. -999.25 : Null value
          "# A legacy comment with no header fields"
          "~Parameter Information"
        BHT. DEGC 35 : Bottom hole temperature
          "~Curve Information"
        DEPT.M : Depth
        GR. API 12 34 56 00 : Gamma ray
        ~Version Information
        VERS. 1.2 : LAS version
        WRAP. NO : One row per sample
          "~ASCII Log Data"
        1 -999.25
        2 35
        """);

    Equal("Alder-01", log.WellInfo.Single(entry => entry.Mnemonic == "WELL").Value);
    Equal("Alder Logging", log.WellInfo.Single(entry => entry.Mnemonic == "COMP").Value);
    Equal(new LasHeaderEntry("STRT", "M", "1", "Start"), log.WellInfo.Single(entry => entry.Mnemonic == "STRT"));
    Equal(new LasHeaderEntry("BHT", "DEGC", "35", "Bottom hole temperature"), log.ParameterInfo.Single());
    Equal(new LasHeaderEntry("GR", "API", "12 34 56 00", "Gamma ray"), log.Curves[1].Header);
    Equal(-999.25, log.NullValue);
    Samples(log, 1, double.NaN, 35);
}

static void Version30LegacyNames()
{
    string source = Version30("", "1 7", longNames: false)
        .Replace("~ASCII", "~ASCII DEPT GR", StringComparison.Ordinal);
    LasDocument log = Read(source);
    Samples(log, 0, 1);
    Samples(log, 1, 7);
}

static void Version30LongNames()
{
    string source = Version30("SPACE", "1 7", "GR.API : Gamma ray {F} | RUN").Replace("~Log_Definition", """
        ~Custom_Parameter
        FAKE.M 7 : A custom field
        ~Processing_Parameter
        FILTER. 3 : A processing field
        ~Log_Definition
        """, StringComparison.Ordinal);
    LasDocument log = Read(source);

    Equal(new LasHeaderEntry("RUN", "", "1", "Run number"), log.ParameterInfo.Single());
    Equal(2, log.Curves.Count);
    Equal("F", log.Curves[1].Header.Format);
    Equal("Gamma ray", log.Curves[1].Description);
    Equal("RUN", log.Curves[1].Header.Association);
    Samples(log, 0, 1);
    Samples(log, 1, 7);
}

static void MissingDelimitedFields(string delimiter, string separator)
{
    string definition = "GR.API : Gamma ray\nRHOB.G/C3 : Bulk density";
    string rows = string.Join(separator, "1", "", "1.25") + "\n" + string.Join(separator, "2", "25", "");
    LasDocument log = Read(Version30(delimiter, rows, definition));

    Samples(log, 0, 1, 2);
    Samples(log, 1, double.NaN, 25);
    Samples(log, 2, 1.25, double.NaN);
    Reject(Version30(delimiter, string.Join(separator, "", "3", "1.25"), definition), "index");
    Reject(Version30(delimiter, string.Join(separator, "-999.25", "3", "1.25"), definition), "index");
}

static void UnsupportedTypes()
{
    Reject(Version20("NO", "1 001#east"));
    Samples(Read(Version20("NO", "1 7 # trailing field note")), 1, 7);
    Reject(Version30("COMMA", "1,001#east", "LABEL. : Well label {S}"), "unsupported");
    Reject(Version30("COMMA", "1,2026", "DATE. : Date {YYYY}"), "unsupported");
    Reject(Version30("COMMA", "1,25,26", """
        GR[1].API : First gamma ray element {AF;0ms}
        GR[2].API : Second gamma ray element {AF;5ms}
        """), "unsupported");
    Reject(Version30("COMMA", "1,001#east"), "finite number");
}

static void InvalidAssociations()
{
    string source = Version30("SPACE", "1 7");
    Reject(source.Replace("| Log_Definition", "| Other_Definition", StringComparison.Ordinal), "association");
    Reject(source.Replace(" | Log_Definition", "", StringComparison.Ordinal), "association");
    Reject(source + "\n~Log_Data | Log_Definition\n2 8", "multiple");
    Reject(source + "\n~Log_Definition\nDEPT.M : Other definition", "definition");
}

static void IndexedLog()
{
    string source = Version30("SPACE", "1 7")
        .Replace("Log_Parameter", "Log_Parameter[1]", StringComparison.Ordinal)
        .Replace("Log_Definition", "Log_Definition[1]", StringComparison.Ordinal)
        .Replace("Log_Data", "Log_Data[1]", StringComparison.Ordinal);
    Samples(Read(source), 1, 7);
    Reject(source.Replace("| Log_Definition[1]", "| Log_Definition[2]", StringComparison.Ordinal));
    Reject(source + "\n~Log_Data[2] | Log_Definition[1]\n2 8", "multiple");
}

static void IndexedLogWithSharedDefinition()
{
    string source = Version30("SPACE", "1 7")
        .Replace("~Log_Data |", "~Log_Data[1] |", StringComparison.Ordinal);
    LasDocument log = Read(source);
    Samples(log, 0, 1);
    Samples(log, 1, 7);
    Reject(source + "\n~Log_Data[2] | Log_Definition\n2 8", "multiple");
}

static void AdditionalSections()
{
    string source = Version20("NO", "1 7").Replace("~ASCII", """
        ~Other
          These remarks include indentation and a colon: keep both.

        # Keep this raw comment too.
        ~calculated notes
        24. Raw value
        ~ASCII
        """, StringComparison.Ordinal);
    LasDocument log = Read(source);

    Equal(2, log.AdditionalSections.Count);
    Equal("~Other", log.AdditionalSections[0].Title);
    Equal(3, log.AdditionalSections[0].Lines.Count);
    Equal("  These remarks include indentation and a colon: keep both.", log.AdditionalSections[0].Lines[0]);
    Equal("", log.AdditionalSections[0].Lines[1]);
    Equal("# Keep this raw comment too.", log.AdditionalSections[0].Lines[2]);
    Equal("~calculated notes", log.AdditionalSections[1].Title);
    Equal("24. Raw value", log.AdditionalSections[1].Lines.Single());
}

static void PreciseNullComparison()
{
    LasDocument log = Read(Version20("NO", """
        1 -9.992500e2
        2 -999.25000000000001
        3 9007199254740992
        4 9007199254740993
        """));

    Samples(log, 1, double.NaN, -999.25, 9007199254740992d, 9007199254740992d);
    Equal<string?>(null, log.Curves[1].GetValueText(0));
    Equal("-999.25000000000001", log.Curves[1].GetValueText(1));
    Equal("9007199254740992", log.Curves[1].GetValueText(2));
    Equal("9007199254740993", log.Curves[1].GetValueText(3));
}

static void PreciseScientificNotation()
{
    LasDocument log = Read(Version20("NO", """
        1 10000000000000001e-16
        2 1e-400
        3 0
        """, "DEPT.M : Depth\nRATIO. : Unitless ratio"));

    Equal("", log.Curves[1].Unit);
    Samples(log, 1, 1, 0, 0);
    Equal("10000000000000001e-16", log.Curves[1].GetValueText(0));
    Equal("1e-400", log.Curves[1].GetValueText(1));
    Equal("0", log.Curves[1].GetValueText(2));
}

static void InvalidWrappedRecords()
{
    Reject(Version20("YES", "1000\n1001\n12 13"), "index");
    Reject(Version20("YES", "1000 2"), "index");
    Reject(Version20("YES", "1000\n2 3"), "too many values");
    string curves = "DEPT.M : Depth\nGR.API : Gamma ray\nRHOB.G/C3 : Bulk density";
    Reject(Version20("YES", "1000\n2", curves), "incomplete");
    Reject(Version20("YES", "1000\n2\n~Other\nEnd of log", curves), "incomplete");
}

static void DescendingWrappedRecords()
{
    LasDocument log = Read(Version20("YES", """
        1000
        10
        1.25
        999
        20 1.5
        """, "DEPT.M : Depth\nGR.API : Gamma ray\nRHOB.G/C3 : Bulk density", """
        STRT.M 1000 : Start
        STOP.M 999 : Stop
        STEP.M -1 : Step
        """));

    Samples(log, 0, 1000, 999);
    Samples(log, 1, 10, 20);
    Samples(log, 2, 1.25, 1.5);
}

static void VariableStepRecords()
{
    LasDocument log = Read(Version20("NO", "1 10\n2 20\n4 30", indexHeader: """
        STRT.M 1 : Start
        STOP.M 4 : Stop
        STEP.M 0 : Variable step
        """));

    Samples(log, 0, 1, 2, 4);
    Samples(log, 1, 10, 20, 30);
    Equal(0, log.Warnings.Count);
}

static void IndexHeaderWarnings()
{
    LasDocument log = Read(Version20("NO", "1 10\n2 20\n4 30", indexHeader: """
        STRT.M 0 : Incorrect start
        STOP.M 5 : Incorrect stop
        STEP.M 1 : Incorrect step
        """));

    foreach (string field in new[] { "STRT", "STOP", "STEP" })
    {
        if (!log.Warnings.Any(warning => warning.Contains(field, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Expected a warning mentioning {field}.");
    }

    Samples(log, 0, 1, 2, 4);
}

static void RequiredVersion()
{
    string source = Version20("NO", "1 7");
    Reject(source[source.IndexOf("~Well", StringComparison.Ordinal)..], "version");
    Reject(source.Replace("VERS. 2.0 : LAS version", "", StringComparison.Ordinal), "VERS");
    Reject(source.Replace("VERS. 2.0", "VERS. 9.0", StringComparison.Ordinal), "VERS");
}

static string Version20(string wrap, string rows,
    string curves = "DEPT.M : Depth\nGR.API : Gamma ray", string indexHeader = "") => $"""
    ~Version
    VERS. 2.0 : LAS version
    WRAP. {wrap} : Wrapping
    ~Well
    {indexHeader}
    NULL. -999.25 : Null value
    ~Curve
    {curves}
    ~ASCII
    {rows}
    """;

static string Version30(string delimiter, string rows, string curves = "GR.API : Gamma ray", bool longNames = true)
{
    string parameterSection = longNames ? "Log_Parameter" : "Parameter";
    string definitionSection = longNames ? "Log_Definition" : "Curve";
    string dataSection = longNames ? "Log_Data | Log_Definition" : "ASCII";
    return $"""
        ~Version
        VERS. 3.0 : LAS version
        WRAP. NO : One row per sample
        DLM. {delimiter} : Data delimiter
        ~Well
        NULL. -999.25 : Null value
        ~{parameterSection}
        RUN. 1 : Run number
        ~{definitionSection}
        DEPT.M : Depth
        {curves}
        ~{dataSection}
        {rows}
        """;
}

static LasDocument Read(string source)
{
    using var reader = new StringReader(source);
    return LasFileReader.Read(reader);
}

static void Reject(string source, string? expectedMessage = null)
{
    try
    {
        Read(source);
    }
    catch (FormatException exception)
    {
        if (expectedMessage is null || exception.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException($"Expected a format error mentioning '{expectedMessage}', got '{exception.Message}'.", exception);
    }

    throw new InvalidOperationException("Expected an invalid LAS file to be rejected.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void Samples(LasDocument log, int curve, params double[] expected)
{
    if (!expected.SequenceEqual(log.Curves[curve].Values))
    {
        string actual = string.Join(", ", log.Curves[curve].Values);
        throw new InvalidOperationException($"Expected curve {curve} [{string.Join(", ", expected)}], got [{actual}].");
    }
}
