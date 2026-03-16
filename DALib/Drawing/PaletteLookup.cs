using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using DALib.Data;
using DALib.Definitions;
using DALib.Extensions;

namespace DALib.Drawing;

/// <summary>
///     Represents a lookup table for palettes. This will load a PaletteTable, and a map of palette numbers to palettes
/// </summary>
public class PaletteLookup
{
    /// <summary>
    ///     The palettes in this lookup, used to colorize external images
    /// </summary>
    public IDictionary<int, Palette> Palettes { get; set; }

    /// <summary>
    ///     The palette table for this lookup, used to associate external ids to specific palettes
    /// </summary>
    public PaletteTable Table { get; set; }

    private PaletteLookup(IDictionary<int, Palette> palettes, PaletteTable table)
    {
        Palettes = palettes;
        Table = table;
    }

    /// <summary>
    ///     Freezes this palette lookup, preventing further changes and optimizing it for faster lookups
    /// </summary>
    public virtual PaletteLookup Freeze() => new FrozenPaletteLookup(this);

    /// <summary>
    ///     Gets the next available palette id
    /// </summary>
    public int GetNextPaletteId() => Palettes.Keys.Max() + 1;

    /// <summary>
    ///     Gets the palette for the specified id
    /// </summary>
    /// <param name="id">
    ///     The external id to look up the palette for
    /// </param>
    /// <param name="khanPalOverrideType">
    ///     An optional override used when working with KHAN archives that indicates if the lookup should favor male or female
    ///     overrides
    /// </param>
    public Palette GetPaletteForId(int id, KhanPalOverrideType khanPalOverrideType = KhanPalOverrideType.None)
    {
        var paletteNumber = Table.GetPaletteNumber(id, khanPalOverrideType);
        var useLuminanceBlending = false;

        if (paletteNumber >= 1000)
        {
            paletteNumber -= 1000;
            useLuminanceBlending = true;
        }

        var palette = Palettes[paletteNumber];

        if (useLuminanceBlending)
        {
            var blendedPalette = new Palette();

            for (var i = 0; i < CONSTANTS.COLORS_PER_PALETTE; i++)
            {
                var color = palette[i];

                blendedPalette[i] = color.WithLutAlpha();
            }

            return blendedPalette;
        }

        return Palettes[paletteNumber];
    }

    private sealed class FrozenPaletteLookup : PaletteLookup
    {
        public FrozenPaletteLookup(PaletteLookup paletteLookup)
            : base(paletteLookup.Palettes, paletteLookup.Table)
        {
            Palettes = Palettes.ToFrozenDictionary();
            Table = Table.Freeze();
        }

        public override PaletteLookup Freeze() => this;
    }

    #region LoadFrom
    /// <summary>
    ///     Loads a PaletteLookup from the specified archive by searching for Palettes and PaletteTables that match the given
    ///     pattern
    /// </summary>
    /// <param name="pattern">
    ///     The pattern used to find Palettes and PaletteTables for this lookup
    /// </param>
    /// <param name="archive">
    ///     The archive to extract Palettes and PaletteTables from
    /// </param>
    public static PaletteLookup FromArchive(string pattern, DataArchive archive)
        => new(Palette.FromArchive(pattern, archive), PaletteTable.FromArchive(pattern, archive));

    /// <summary>
    ///     Loads a PaletteLookup from the specified archive by searching for Palettes and PaletteTables that match the given
    ///     tablePattern and palettePattern
    /// </summary>
    /// <param name="tablePattern">
    ///     The pattern used to find paletteTables for this lookup
    /// </param>
    /// <param name="palettePattern">
    ///     The pattern used to find palettes for this lookup
    /// </param>
    /// <param name="archive">
    ///     The archive to extract Palettes and PaletteTables from
    /// </param>
    public static PaletteLookup FromArchive(string tablePattern, string palettePattern, DataArchive archive)
        => new(Palette.FromArchive(palettePattern, archive), PaletteTable.FromArchive(tablePattern, archive));
    #endregion
}