namespace LasReader.Core;

public sealed record LasRawSection(string Title, IReadOnlyList<string> Lines);
