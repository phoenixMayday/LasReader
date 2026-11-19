namespace LasReader.Core;

public sealed class LasDocument
{
    internal LasDocument(
        List<LasHeaderEntry> versionInfo, List<LasHeaderEntry> wellInfo,
        List<LasHeaderEntry> parameterInfo, LasCurve[] curves, double nullValue,
        List<LasRawSection> additionalSections, List<string> warnings)
    {
        VersionInfo = versionInfo.AsReadOnly();
        WellInfo = wellInfo.AsReadOnly();
        ParameterInfo = parameterInfo.AsReadOnly();
        Curves = Array.AsReadOnly(curves);
        NullValue = nullValue;
        AdditionalSections = additionalSections.AsReadOnly();
        Warnings = warnings.AsReadOnly();
    }

    public IReadOnlyList<LasHeaderEntry> VersionInfo { get; }
    public IReadOnlyList<LasHeaderEntry> WellInfo { get; }
    public IReadOnlyList<LasHeaderEntry> ParameterInfo { get; }
    /// <summary>Other and ancillary sections in file order, including their unparsed contents.</summary>
    public IReadOnlyList<LasRawSection> AdditionalSections { get; }
    /// <summary>Nonfatal discrepancies between the headers and the measured data.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Curves in file order, starting with the depth or other index curve.</summary>
    public IReadOnlyList<LasCurve> Curves { get; }

    /// <summary>The numeric sentinel used for missing samples in the file.</summary>
    public double NullValue { get; }
}
