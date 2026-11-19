# LAS Viewer

Open a Log ASCII Standard well log and compare channels in vertical tracks. Each track has its own horizontal scale. The tracks share the first curve as their vertical index, with larger values toward the bottom.

## Run on Windows

Build with the .NET 10 SDK or a newer SDK that can target .NET 10. To open the synthetic example from the solution directory, run

```powershell
dotnet run --project LasViewer/LasViewer.csproj -- Samples/Alder-01.las
```

Use **Open LAS…** or **Ctrl+O** to load another file. The viewer initially shows up to three channels. Check the channels you want to see, and use the ↑ and ↓ buttons to set their left-to-right order. Click a channel’s colour swatch to choose a colour for its trace, heading, and reader values.

Each channel initially uses the minimum and maximum across the full log. Check **Fit X to visible interval** on a channel to adjust its horizontal scale as you zoom or pan. Scroll the mouse wheel to zoom around the pointer, drag vertically to pan while zoomed, and double-click to restore the full interval. The right-hand scrollbar shows the visible fraction of the log and its position. Drag its thumb to move to another interval at the same zoom.

Hover over the plot to read the index and channel values along a horizontal line. Between samples, the reader interpolates along each trace. A dash indicates a missing measurement or a gap. Click to pin a reader at that index, then hover or click elsewhere to compare. **Remove last** removes the newest pin, and **Clear all readers** removes all pins. Pins stay at their indices as you zoom, pan, and reorder channels.

The framework-dependent executable requires the .NET 10 Desktop Runtime on Windows. The chart uses WPF drawing primitives and has no third-party chart dependency or chart licensing fee. [Microsoft’s .NET licensing overview](https://dotnet.microsoft.com/en-us/platform/free)

## Reuse the reader

`LasReader.Core` is an independent .NET 10 class library. Reference its project or assembly and pass any `TextReader` to the parser.

```csharp
using LasReader.Core;

using var input = File.OpenText("well.las");
var log = LasFileReader.Read(input);
Console.WriteLine($"Index: {log.Curves[0].Mnemonic} [{log.Curves[0].Unit}]");
foreach (var channel in log.Curves.Skip(1))
    Console.WriteLine($"{channel.Mnemonic} [{channel.Unit}] — {channel.Values.Count} samples");
```

The reader handles one scalar numeric log from LAS 1.2, 2.0, or 3.0. It supports legacy wrapped records, the LAS 3.0 `~Log_Definition` and `~Log_Data` section names, and space-, comma-, or tab-delimited LAS 3.0 data. Missing measurements appear as `double.NaN`. Unsupported column types, multiple log datasets, and malformed records raise `FormatException`.

Curves retain their source order and units. `Values[i]` is the double-precision value used for plotting. `GetValueText(i)` returns the numeric value in invariant text, preserving extra decimal precision, or null for a missing sample. Unparsed ancillary sections are available through `AdditionalSections`. `Warnings` reports discrepancies between measured indices and header declarations, and a warning also appears in the viewer.

## Benchmarks and format coverage

[Viewer performance measurements](docs/plot-performance.md) give pan, zoom and hover timings by sample and channel count. The [LAS compatibility audit](docs/las-compatibility.md) records verified behaviour against the CWLS 1.2, 2.0 and 3.0 well-log specifications.

Run the reader regression checks with `dotnet run --project tests/LasReader.Core.Tests -c Release`.
