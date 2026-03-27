namespace DALib.Drawing;

/// <summary>
///     Represents a palette index cycling range used for animated color effects such as water shimmer. Colors at palette
///     indices within the range are rotated each period to create a smooth animation effect.
/// </summary>
public sealed class PaletteCyclingEntry
{
    /// <summary>
    ///     The ending palette index of the cycling range (0-based)
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    ///     The number of frames between each rotation step
    /// </summary>
    public int Period { get; set; }

    /// <summary>
    ///     The starting palette index of the cycling range (0-based)
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    ///     Initializes a new instance of the PaletteCyclingEntry class
    /// </summary>
    public PaletteCyclingEntry() { }

    /// <summary>
    ///     Initializes a new instance of the PaletteCyclingEntry class with the specified values
    /// </summary>
    /// <param name="startIndex">
    ///     The starting palette index (0-based)
    /// </param>
    /// <param name="endIndex">
    ///     The ending palette index (0-based)
    /// </param>
    /// <param name="period">
    ///     The number of frames between each rotation step
    /// </param>
    public PaletteCyclingEntry(int startIndex, int endIndex, int period)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        Period = period;
    }
}