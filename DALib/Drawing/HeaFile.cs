using System;
using System.IO;
using System.Text;
using DALib.Abstractions;
using DALib.Data;
using DALib.Extensions;

namespace DALib.Drawing;

/// <summary>
///     Represents a light/alpha map file with the ".hea" extension. These files define per-pixel light intensity data for
///     map darkness and lantern illumination. The data is organized as horizontal strip layers that are stitched together
///     to form the full light map. Each layer covers a 1000-pixel horizontal strip (except the last which covers the
///     remainder). Light values range from 0 (fully dark) to <see cref="MAX_LIGHT_VALUE" /> (maximum brightness).
///     The RLE data uses (value, count) byte pairs per scanline
/// </summary>
public sealed class HeaFile : ISavable
{
    /// <summary>
    ///     The maximum light intensity value used in the RLE data
    /// </summary>
    public const byte MAX_LIGHT_VALUE = 0x20;

    /// <summary>
    ///     The standard horizontal strip width for each layer (except possibly the last)
    /// </summary>
    public const int LAYER_STRIP_WIDTH = 1000;

    /// <summary>
    ///     The screen width stored in the header. Always 640
    /// </summary>
    public int ScreenWidth { get; set; }

    /// <summary>
    ///     The screen height stored in the header. Always 480
    /// </summary>
    public int ScreenHeight { get; set; }

    /// <summary>
    ///     The tile pixel width used for isometric rendering
    /// </summary>
    public int TileWidth { get; set; }

    /// <summary>
    ///     The tile pixel height used for isometric rendering
    /// </summary>
    public int TileHeight { get; set; }

    /// <summary>
    ///     The total pixel width of the full stitched light map (all layers combined horizontally).
    ///     Computed as <c>28 * (TileWidth + TileHeight) + ScreenWidth * 2</c>
    /// </summary>
    public int ScanlineWidth { get; set; }

    /// <summary>
    ///     The number of scanlines (pixel rows) per layer
    /// </summary>
    public int ScanlineCount { get; set; }

    /// <summary>
    ///     The number of horizontal strip layers. Each layer covers <see cref="LAYER_STRIP_WIDTH" /> pixels except the last
    ///     which covers the remainder. Computed as <c>ceil(ScanlineWidth / 1000)</c>
    /// </summary>
    public int LayerCount { get; set; }

    /// <summary>
    ///     The horizontal pixel offset for each layer. Values are 0, 1000, 2000, etc
    /// </summary>
    public int[] Thresholds { get; set; }

    /// <summary>
    ///     The scanline offset table. Contains <c>LayerCount * ScanlineCount</c> entries laid out as
    ///     [layer0_scan0, layer0_scan1, ..., layer1_scan0, ...]. Each value is a <b>word offset</b> (multiply by 2 to get
    ///     the byte position within <see cref="RleData" />)
    /// </summary>
    public int[] ScanlineOffsets { get; set; }

    /// <summary>
    ///     The raw RLE-encoded light data. Each scanline is encoded as sequential (value, count) byte pairs where value
    ///     is the light intensity (0 = dark, <see cref="MAX_LIGHT_VALUE" /> = brightest) and count is the run length
    /// </summary>
    public byte[] RleData { get; set; }

    /// <summary>
    ///     Creates an empty HeaFile
    /// </summary>
    public HeaFile()
    {
        ScreenWidth = 640;
        ScreenHeight = 480;
        Thresholds = [];
        ScanlineOffsets = [];
        RleData = [];
    }

    private HeaFile(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Default, true);

        _ = reader.ReadInt32(); // padding, always 0

        ScreenWidth = reader.ReadInt32();
        ScreenHeight = reader.ReadInt32();
        _ = reader.ReadInt32(); // screen width repeat
        _ = reader.ReadInt32(); // screen height repeat
        TileWidth = reader.ReadInt32();
        TileHeight = reader.ReadInt32();
        ScanlineWidth = reader.ReadInt32();
        ScanlineCount = reader.ReadInt32();
        LayerCount = reader.ReadInt32();

        Thresholds = new int[LayerCount];

        for (var i = 0; i < LayerCount; i++)
            Thresholds[i] = reader.ReadInt32();

        var totalOffsets = LayerCount * ScanlineCount;
        ScanlineOffsets = new int[totalOffsets];

        for (var i = 0; i < totalOffsets; i++)
            ScanlineOffsets[i] = reader.ReadInt32();

        var dataLength = (int)(stream.Length - stream.Position);
        RleData = reader.ReadBytes(dataLength);
    }

    /// <summary>
    ///     Gets the pixel width of a specific layer's horizontal strip
    /// </summary>
    /// <param name="layerIndex">
    ///     The layer index (0 to LayerCount - 1)
    /// </param>
    public int GetLayerWidth(int layerIndex)
    {
        if ((layerIndex < 0) || (layerIndex >= LayerCount))
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        var start = Thresholds[layerIndex];
        var end = layerIndex < LayerCount - 1 ? Thresholds[layerIndex + 1] : ScanlineWidth;

        return end - start;
    }

    /// <summary>
    ///     Decodes a single scanline's RLE data for a specific layer into an array of light intensity values
    /// </summary>
    /// <param name="layerIndex">
    ///     The layer index (0 to LayerCount - 1)
    /// </param>
    /// <param name="scanlineIndex">
    ///     The scanline index (0 to ScanlineCount - 1)
    /// </param>
    /// <returns>
    ///     An array of light intensity values with length equal to the layer's strip width
    /// </returns>
    public byte[] DecodeScanline(int layerIndex, int scanlineIndex)
    {
        var layerWidth = GetLayerWidth(layerIndex);
        var pixels = new byte[layerWidth];
        DecodeScanline(layerIndex, scanlineIndex, pixels);

        return pixels;
    }

    /// <summary>
    ///     Decodes a single scanline's RLE data for a specific layer into the provided buffer
    /// </summary>
    /// <param name="layerIndex">
    ///     The layer index (0 to LayerCount - 1)
    /// </param>
    /// <param name="scanlineIndex">
    ///     The scanline index (0 to ScanlineCount - 1)
    /// </param>
    /// <param name="buffer">
    ///     A buffer of at least the layer's strip width bytes to receive the decoded light values
    /// </param>
    public void DecodeScanline(int layerIndex, int scanlineIndex, Span<byte> buffer)
    {
        if ((layerIndex < 0) || (layerIndex >= LayerCount))
            throw new ArgumentOutOfRangeException(nameof(layerIndex));

        if ((scanlineIndex < 0) || (scanlineIndex >= ScanlineCount))
            throw new ArgumentOutOfRangeException(nameof(scanlineIndex));

        var layerWidth = GetLayerWidth(layerIndex);
        buffer[..layerWidth].Clear();

        var tableIndex = layerIndex * ScanlineCount + scanlineIndex;
        var wordOffset = ScanlineOffsets[tableIndex];
        var byteOffset = wordOffset * 2;

        var pixelIndex = 0;

        for (var i = byteOffset; (i + 1 < RleData.Length) && (pixelIndex < layerWidth); i += 2)
        {
            var value = RleData[i];
            var count = RleData[i + 1];

            if (count == 0)
                continue;

            var actualCount = Math.Min(count, layerWidth - pixelIndex);

            buffer.Slice(pixelIndex, actualCount)
                  .Fill(value);

            pixelIndex += actualCount;
        }
    }

    #region LoadFrom
    /// <summary>
    ///     Loads a HeaFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file to be read
    /// </param>
    public static HeaFile FromFile(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".hea"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        return new HeaFile(stream);
    }

    /// <summary>
    ///     Loads a HeaFile with the specified fileName from the specified archive
    /// </summary>
    /// <param name="fileName">
    ///     The name of the HEA file to search for in the archive
    /// </param>
    /// <param name="archive">
    ///     The DataArchive from which to retrieve the HEA file
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if the HEA file with the specified name is not found in the archive
    /// </exception>
    public static HeaFile FromArchive(string fileName, DataArchive archive)
    {
        if (!archive.TryGetValue(fileName.WithExtension(".hea"), out var entry))
            throw new FileNotFoundException($"HEA file with the name \"{fileName}\" was not found in the archive");

        return FromEntry(entry);
    }

    /// <summary>
    ///     Loads a HeaFile for a specific map number from the specified archive
    /// </summary>
    /// <param name="mapNumber">
    ///     The map number (e.g. 500 for map lod500.map, which corresponds to 000500.hea)
    /// </param>
    /// <param name="archive">
    ///     The DataArchive (seo.dat) from which to retrieve the HEA file
    /// </param>
    /// <exception cref="FileNotFoundException">
    ///     Thrown if no HEA file exists for the specified map number
    /// </exception>
    public static HeaFile FromArchive(int mapNumber, DataArchive archive)
        => FromArchive($"{mapNumber:D6}", archive);

    /// <summary>
    ///     Loads a HeaFile from the specified archive entry
    /// </summary>
    /// <param name="entry">
    ///     The DataArchiveEntry to load the HeaFile from
    /// </param>
    public static HeaFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new HeaFile(segment);
    }
    #endregion

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path.WithExtension(".hea"),
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
        using var writer = new BinaryWriter(stream, Encoding.Default, true);

        writer.Write(0); // padding
        writer.Write(ScreenWidth);
        writer.Write(ScreenHeight);
        writer.Write(ScreenWidth); // repeat
        writer.Write(ScreenHeight); // repeat
        writer.Write(TileWidth);
        writer.Write(TileHeight);
        writer.Write(ScanlineWidth);
        writer.Write(ScanlineCount);
        writer.Write(LayerCount);

        for (var i = 0; i < LayerCount; i++)
            writer.Write(Thresholds[i]);

        var totalOffsets = LayerCount * ScanlineCount;

        for (var i = 0; i < totalOffsets; i++)
            writer.Write(ScanlineOffsets[i]);

        writer.Write(RleData);
    }
    #endregion
}
