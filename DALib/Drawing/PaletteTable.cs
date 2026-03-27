#region
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;
#endregion

namespace DALib.Drawing;

/// <summary>
///     Represents a palette table used to map external ids to palette ids
/// </summary>
/// <remarks>
///     As a palette table is populated, newer entries override older ones. This is intended behavior. In my opinion this
///     makes it meaningless to store and search through all of the entries. You could search through them in reverse order
///     and return the first one you find, but even still... It should be faster this way, as a dictionary, where each id
///     is mapped to a palette number
/// </remarks>
public class PaletteTable : ISavable
{
    /// <summary>
    ///     Palette cycling definitions keyed by palette number. Each palette may have multiple cycling ranges that define
    ///     which palette indices rotate to create animated effects like water shimmer (e.g. canals water).
    /// </summary>
    protected IDictionary<int, List<PaletteCyclingEntry>> CyclingEntries { get; set; } = new Dictionary<int, List<PaletteCyclingEntry>>();

    /// <summary>
    ///     The entries in the palette table
    /// </summary>
    /// <remarks>
    ///     Regardless of order, single value entries override range entries. These are the range entries
    /// </remarks>
    protected IDictionary<int, int> Entries { get; set; } = new Dictionary<int, int>();

    /// <summary>
    ///     The single value overrides used when preferring female overrides
    /// </summary>
    protected IDictionary<int, int> FemaleOverrides { get; set; } = new Dictionary<int, int>();

    /// <summary>
    ///     The single value overrides used when preferring male overrides
    /// </summary>
    protected IDictionary<int, int> MaleOverrides { get; set; } = new Dictionary<int, int>();

    /// <summary>
    ///     The single value overrides
    /// </summary>
    /// <remarks>
    ///     Regardless of order, single value entries override range entries. These are the single value entries
    /// </remarks>
    protected IDictionary<int, int> Overrides { get; set; } = new Dictionary<int, int>();

    /// <summary>
    ///     Initializes a new instance of the PaletteTable class
    /// </summary>
    public PaletteTable() { }

    private PaletteTable(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (string.IsNullOrEmpty(line))
                continue;

            var vals = line.Split(' ');

            if ((vals.Length < 2) || !int.TryParse(vals[0], out var min) || !int.TryParse(vals[1], out var paletteNumOrMax))
                continue;

            switch (vals.Length)
            {
                case 2:
                {
                    Overrides[min] = paletteNumOrMax;

                    break;
                }
                case 3 when int.TryParse(vals[2], out var paletteNumber):
                {
                    switch (paletteNumber)
                    {
                        case -1: //male
                            MaleOverrides[min] = paletteNumOrMax;

                            break;
                        case -2: //female
                            FemaleOverrides[min] = paletteNumOrMax;

                            break;
                        default:
                            for (var i = min; i <= paletteNumOrMax; ++i)
                                Entries[i] = paletteNumber;

                            break;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    ///     Adds a new entry to the palette table
    /// </summary>
    /// <param name="id">
    ///     The external id for which the specified palette number is mapped to
    /// </param>
    /// <param name="paletteNumber">
    ///     The id of the palette
    /// </param>
    /// <param name="overrideType">
    ///     The type of override to favor if this palette association is for a KHAN archive
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// </exception>
    public virtual void Add(int id, int paletteNumber, KhanPalOverrideType overrideType = KhanPalOverrideType.None)
    {
        switch (overrideType)
        {
            case KhanPalOverrideType.Male:
                MaleOverrides[id] = paletteNumber;

                break;
            case KhanPalOverrideType.Female:
                FemaleOverrides[id] = paletteNumber;

                break;
            case KhanPalOverrideType.None:
                Overrides[id] = paletteNumber;

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overrideType), overrideType, null);
        }
    }

    /// <summary>
    ///     Freezes this palette table, preventing further changes and optimizing it for faster lookups
    /// </summary>
    public virtual PaletteTable Freeze() => new FrozenPaletteTable(this);

    /// <summary>
    ///     Gets the cycling entries for the specified palette number
    /// </summary>
    /// <param name="paletteNumber">
    ///     The palette number to look up cycling entries for
    /// </param>
    /// <returns>
    ///     The list of cycling entries for the palette, or null if the palette has no cycling
    /// </returns>
    public IReadOnlyList<PaletteCyclingEntry>? GetCyclingEntries(int paletteNumber)
        => CyclingEntries.TryGetValue(paletteNumber, out var entries) ? entries : null;

    /// <summary>
    ///     Gets the palette number for the specified id
    /// </summary>
    /// <param name="id">
    ///     The external id for which to find the associated palette
    /// </param>
    /// <param name="overrideType">
    ///     The type of override to favor if working with a KHAN archive
    /// </param>
    public int GetPaletteNumber(int id, KhanPalOverrideType overrideType = KhanPalOverrideType.None)
    {
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if ((overrideType == KhanPalOverrideType.Male) && MaleOverrides.TryGetValue(id, out var malePaletteNumber))
            return malePaletteNumber;

        if ((overrideType == KhanPalOverrideType.Female) && FemaleOverrides.TryGetValue(id, out var femalePaletteNumber))
            return femalePaletteNumber;

        if (Overrides.TryGetValue(id, out var paletteNumber))
            return paletteNumber;

        if (Entries.TryGetValue(id, out paletteNumber))
            return paletteNumber;

        return 0;
    }

    /// <summary>
    ///     Merges the specified palette table into this one
    /// </summary>
    /// <param name="other">
    ///     Another palette table
    /// </param>
    public virtual void Merge(PaletteTable other)
    {
        foreach (var kvp in other.MaleOverrides)
            MaleOverrides[kvp.Key] = kvp.Value;

        foreach (var kvp in other.FemaleOverrides)
            FemaleOverrides[kvp.Key] = kvp.Value;

        foreach (var kvp in other.Overrides)
            Overrides[kvp.Key] = kvp.Value;

        foreach (var kvp in other.Entries)
            Entries[kvp.Key] = kvp.Value;

        foreach (var kvp in other.CyclingEntries)
            CyclingEntries[kvp.Key] = kvp.Value;
    }

    /// <summary>
    ///     Removes the specified id from the palette table (removes it from all collections)
    /// </summary>
    /// <param name="id">
    ///     The external id to remove
    /// </param>
    public virtual void Remove(int id)
    {
        MaleOverrides.Remove(id);
        FemaleOverrides.Remove(id);
        Overrides.Remove(id);
        Entries.Remove(id);
    }

    private sealed class FrozenPaletteTable : PaletteTable
    {
        public FrozenPaletteTable(PaletteTable table)
        {
            base.Merge(table);

            MaleOverrides = MaleOverrides.ToFrozenDictionary();
            FemaleOverrides = FemaleOverrides.ToFrozenDictionary();
            Entries = Entries.ToFrozenDictionary();
            Overrides = Overrides.ToFrozenDictionary();
            CyclingEntries = CyclingEntries.ToFrozenDictionary();
        }

        public override void Add(int id, int paletteNumber, KhanPalOverrideType overrideType = KhanPalOverrideType.None)
            => throw new NotSupportedException("The collection is frozen");

        public override PaletteTable Freeze() => this;

        public override void Merge(PaletteTable other) => throw new NotSupportedException("The collection is frozen");

        public override void Remove(int id) => throw new NotSupportedException("The collection is frozen");
    }

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".tbl"),
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        Save(stream);
    }

    /// <inheritdoc />
    public void Save(Stream stream)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);

        //construct a dictionary of all entries, with overrides applied
        var entries = Entries.ToDictionary();

        foreach (var kvp in Overrides)
            entries[kvp.Key] = kvp.Value;

        //order entries by key so theyre written in ascending order
        var orderedEntries = entries.OrderBy(kvp => kvp.Key);

        foreach (var set in orderedEntries.GroupBy(kvp => kvp.Value))
        {
            var orderedKeys = set.Select(kvp => kvp.Key)
                                 .Order()
                                 .ToArray();
            var ranges = new List<Range>();

            //extract ranges of consecutive keys for the same palette
            for (var i = 0; i < orderedKeys.Length; ++i)
            {
                var start = orderedKeys[i];

                while ((i < (orderedKeys.Length - 1)) && ((orderedKeys[i] + 1) == orderedKeys[i + 1]))
                    i++;

                var end = orderedKeys[i];
                ranges.Add(new Range(start, end));
            }

            foreach (var range in ranges)
            {
                var paletteId = set.Key;

                writer.WriteLine(
                    range.Start.Value == range.End.Value
                        ? $"{range.Start.Value} {paletteId}"
                        : $"{range.Start.Value} {range.End.Value} {paletteId}");
            }
        }

        //write male and female overrides at the end
        foreach (var set in MaleOverrides.OrderBy(kvp => kvp.Key))
            writer.WriteLine($"{set.Key} {set.Value} -1");

        foreach (var set in FemaleOverrides.OrderBy(kvp => kvp.Key))
            writer.WriteLine($"{set.Key} {set.Value} -2");
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Loads a palette table from the specified archive by searching for PaletteTables that match the given pattern
    /// </summary>
    /// <param name="pattern">
    ///     The pattern to match
    /// </param>
    /// <param name="archive">
    ///     The archive from which to extract PaletteTables
    /// </param>
    /// <returns>
    /// </returns>
    public static PaletteTable FromArchive(string pattern, DataArchive archive)
    {
        var table = new PaletteTable();

        foreach (var entry in archive.GetEntries(pattern, ".tbl"))
            if (entry.TryGetNumericIdentifier(out var associatedPaletteNumber))
            {
                var cyclingEntries = ParseCyclingFile(entry);

                if (cyclingEntries.Count > 0)
                    table.CyclingEntries[associatedPaletteNumber] = cyclingEntries;
            } else
            {
                var tablePart = FromEntry(entry);

                table.Merge(tablePart);
            }

        return table;
    }

    private static List<PaletteCyclingEntry> ParseCyclingFile(DataArchiveEntry entry)
    {
        var entries = new List<PaletteCyclingEntry>();

        using var segment = entry.ToStreamSegment();
        using var reader = new StreamReader(segment, Encoding.UTF8, leaveOpen: true);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();

            if (string.IsNullOrEmpty(line))
                continue;

            var vals = line.Split(' ');

            if ((vals.Length == 3)
                && int.TryParse(vals[0], out var start)
                && int.TryParse(vals[1], out var end)
                && int.TryParse(vals[2], out var period))
                entries.Add(new PaletteCyclingEntry(start, end, period));
        }

        return entries;
    }

    /// <summary>
    ///     Loads a palette table from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the palette table from
    /// </param>
    public static PaletteTable FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new PaletteTable(segment);
    }

    /// <summary>
    ///     Loads a palette table from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read.
    /// </param>
    public static PaletteTable FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".tbl"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new PaletteTable(stream);
    }
    #endregion
}