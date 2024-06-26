﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using DALib.Abstractions;
using DALib.Extensions;
using DALib.Memory;

namespace DALib.Data;

/// <summary>
///     Represents a collection of metadata entries stored in a file format. Can be used to view or manipulate entries.
/// </summary>
public sealed class MetaFile : Collection<MetaFileEntry>, ISavable
{
    /// <summary>
    ///     Represents a file that contains meta data entries
    /// </summary>
    public MetaFile() { }

    private MetaFile(Stream stream)
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(949)!;
        using var reader = new BinaryReader(stream, encoding, true);

        var entryCount = reader.ReadUInt16(true);

        for (var i = 0; i < entryCount; i++)
        {
            var entryName = reader.ReadString8(encoding);
            var propertyCount = reader.ReadUInt16(true);
            var properties = new List<string>();

            for (var j = 0; j < propertyCount; ++j)
            {
                var propertyValue = reader.ReadString16(encoding, true);
                properties.Add(propertyValue);
            }

            var entry = new MetaFileEntry(entryName, properties);

            Add(entry);
        }
    }

    #region LoadFrom
    /// <summary>
    ///     Loads a MetaFile from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path of the file.
    /// </param>
    /// <param name="isCompressed">
    ///     A value indicating whether the file is compressed.
    /// </param>
    public static MetaFile FromFile(string path, bool isCompressed)
    {
        using var stream = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });

        if (!isCompressed)
            return new MetaFile(stream);

        using var decompressor = new ZLibStream(stream, CompressionMode.Decompress);

        return new MetaFile(decompressor);
    }
    #endregion

    #region SaveTo
    /// <inheritdoc />
    public void Save(string path)
    {
        using var stream = File.Open(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite
            });
        using var compressor = new ZLibStream(stream, CompressionMode.Compress);

        Save(compressor);
    }

    /// <inheritdoc />
    public void Save(Stream stream)
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(949)!;
        var writer = new SpanWriter(encoding);

        writer.WriteUInt16((ushort)Count);

        foreach (var entry in this)
        {
            writer.WriteString8(entry.Key);
            writer.WriteUInt16((ushort)entry.Properties.Count);

            foreach (var property in entry.Properties)
                writer.WriteString16(property);
        }

        stream.Write(writer.ToSpan());
    }
    #endregion
}