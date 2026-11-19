namespace LasReader.Core;

public sealed record LasHeaderEntry(string Mnemonic, string Unit, string Value, string Description)
{
    /// <summary>The LAS 3.0 format text inside the braces, or null if absent.</summary>
    public string? Format { get; init; }

    /// <summary>The raw LAS 3.0 association list after the bar, or null if absent.</summary>
    public string? Association { get; init; }
}
