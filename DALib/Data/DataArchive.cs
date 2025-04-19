using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using DALib.Abstractions;
using DALib.Comparers;
using DALib.Definitions;
using DALib.Extensions;
using KGySoft.CoreLibraries;

namespace DALib.Data;

/// <summary>
///     Represents a DarkAges data archive that can be used for storing and manipulating data entries.
/// </summary>
public class DataArchive : KeyedCollection<string, DataArchiveEntry>, ISavable, IDisposable
{
    /// <summary>
    ///     Whether the archive has been disposed.
    /// </summary>
    private int Disposed;

    /// <summary>
    ///     The base stream of the archive
    /// </summary>
    protected Stream BaseStream { get; set; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DataArchive" /> class.
    /// </summary>
    protected DataArchive(Stream stream, bool newFormat = false)
        : base(StringComparer.OrdinalIgnoreCase)
    {
        BaseStream = stream;

        using var reader = new BinaryReader(BaseStream, Encoding.Default, true);

        var expectedNumberOfEntries = reader.ReadInt32() - 1;

        for (var i = 0; i < expectedNumberOfEntries; ++i)
        {
            var startAddress = reader.ReadInt32();

            var nameLength = newFormat ? 12 : CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH;

            var nameBytes = new byte[nameLength];
            _ = reader.Read(nameBytes, 0, nameLength);

            var name = Encoding.ASCII.GetString(nameBytes);
            var nullChar = name.IndexOf('\0');

            if (nullChar > -1)
                name = name[..nullChar];

            if (newFormat)
                _ = reader.ReadBytes(20); //idk

            var endAddress = reader.ReadInt32();

            stream.Seek(-4, SeekOrigin.Current);

            try
            {
                Add(
                    new DataArchiveEntry(
                        this,
                        name,
                        startAddress,
                        endAddress - startAddress));
            } catch
            {
                if (!newFormat)
                    throw;
            }
        }
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (Interlocked.CompareExchange(ref Disposed, 1, 0) == 1)
            return;

        BaseStream.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Compiles the contents of the specified directory into a new archive.
    /// </summary>
    /// <param name="fromDir">
    ///     The directory to compile into an archive
    /// </param>
    /// <param name="toPath">
    ///     The destination path of the archive
    /// </param>
    public static void Compile(string fromDir, string toPath)
    {
        const int HEADER_LENGTH = 4;
        const int ENTRY_HEADER_LENGTH = 4 + CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH;

        var files = Directory.GetFiles(fromDir);
        var dataStreams = new List<Stream>();

        using var dat = File.Create(toPath.WithExtension(".dat"));
        using var writer = new BinaryWriter(dat, Encoding.Default, true);

        writer.Write(files.Length + 1);

        //add the file header length
        //plus the entry header length * number of entries
        //plus 4 bytes for the final entry's end address (which could also be considered the total number of bytes)
        var address = HEADER_LENGTH + files.Length * ENTRY_HEADER_LENGTH + 4;

        foreach (var file in files)
        {
            var nameStr = Path.GetFileName(file);

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (nameStr.Length > CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH)
                throw new InvalidOperationException("Entry name is too long, must be 13 characters or less");

            if (nameStr.Length < CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH)
                nameStr = nameStr.PadRight(CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH, '\0');

            //get bytes for the name field (binaryWriter.Write(string) doesn't work for this)
            var nameStrBytes = Encoding.ASCII.GetBytes(nameStr);

            writer.Write(address);
            writer.Write(nameStrBytes);

            var dataStream = File.Open(
                file,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.SequentialScan,
                    Share = FileShare.ReadWrite,
                    BufferSize = 8192
                });
            dataStreams.Add(dataStream);

            address += (int)dataStream.Length;
        }

        writer.Write(address);

        foreach (var stream in dataStreams)
        {
            stream.CopyTo(dat);
            stream.Dispose();
        }
    }

    /// <summary>
    ///     Extracts the contents of the current archive to the specified directory.
    /// </summary>
    /// <param name="dir">
    ///     The directory to which the contents will be extracted.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the archive is already disposed.
    /// </exception>
    public virtual void ExtractTo(string dir)
    {
        ThrowIfDisposed();

        foreach (var entry in this)
        {
            var path = Path.Combine(dir, entry.EntryName);

            using var stream = File.Create(path);
            using var segment = GetEntryStream(entry);

            segment.CopyTo(stream);
        }
    }

    /// <summary>
    ///     Returns a collection of data archive entries that match the given pattern and extension.
    /// </summary>
    /// <param name="pattern">
    ///     The pattern to match the entry name against.
    /// </param>
    /// <param name="extension">
    ///     The extension to match the entry name against.
    /// </param>
    /// <returns>
    ///     A collection of <see cref="DataArchiveEntry" /> objects that match the pattern and extension.
    /// </returns>
    public virtual IEnumerable<DataArchiveEntry> GetEntries(string pattern, string extension)
    {
        foreach (var entry in this)
        {
            if (!entry.EntryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!entry.EntryName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return entry;
        }
    }

    /// <summary>
    ///     Retrieves all entries with a specified extension.
    /// </summary>
    /// <param name="extension">
    ///     The extension to filter the entries with.
    /// </param>
    /// <returns>
    ///     An <see cref="IEnumerable{T}" /> of <see cref="DataArchiveEntry" /> containing all entries with the specified
    ///     extension.
    /// </returns>
    public virtual IEnumerable<DataArchiveEntry> GetEntries(string extension)
    {
        foreach (var entry in this)
        {
            if (!entry.EntryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return entry;
        }
    }

    /// <summary>
    ///     Gets a stream that contains the data for the specified entry.
    /// </summary>
    public virtual Stream GetEntryStream(DataArchiveEntry entry) => BaseStream.Slice(entry.Address, entry.FileSize);

    /// <inheritdoc />
    protected override string GetKeyForItem(DataArchiveEntry item) => item.EntryName;

    /// <summary>
    ///     Patches the DataArchive by appending a new entry that replaces an existing entry with the same name, or adds a new
    ///     entry if no existing entry has the same name.
    /// </summary>
    /// <param name="entryName">
    ///     The name of the entry to be patched.
    /// </param>
    /// <param name="item">
    ///     The item to be patched.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the DataArchive is not in memory.
    /// </exception>
    public virtual void Patch(string entryName, ISavable item)
    {
        ThrowIfDisposed();

        //if an entry exists with the same name
        //grab its index, so we can replace it and preserve order
        var index = -1;

        if (TryGetValue(entryName, out var existingEntry))
            index = IndexOf(existingEntry);

        BaseStream.Seek(0, SeekOrigin.End);
        var address = (int)BaseStream.Length;

        item.Save(BaseStream);

        var length = (int)BaseStream.Length - address;

        //create a new entry (this entry will be appended to the end of the archive)
        var entry = new DataArchiveEntry(
            this,
            entryName,
            address,
            length);

        //if index is not -1, replace the existing entry
        if (index != -1)
            this[index] = entry;
        else //otherwise add new entry
            Add(entry);
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Disposed == 1, this);

    #region SaveTo
    /// <summary>
    ///     Sorts the archive entries in a custom ordering
    /// </summary>
    /// <remarks>
    ///     This all looks really complicated, but what's happening is conceptually simple...
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 if the entry name is parsable as an integer, sort it as an integer
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 analyze the all entries in the archive and group them by their "prefix"
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 for each group, find the common numeric identifier length
    ///                 <list type="bullet">
    ///                     <item>
    ///                         <description>
    ///                             this isnt an exact process because there can be entries with /no/ numbers, and entries
    ///                             /with/ numbers for the same prefix
    ///                         </description>
    ///                     </item>
    ///                     <item>
    ///                         <description>
    ///                             there can also be entries with different lengths of numeric identifiers because they have
    ///                             numeric tails (think khan archive entries ending in 01, 02, etc)
    ///                         </description>
    ///                     </item>
    ///                 </list>
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 sort by prefix (underscore are considered "less than" other characters)
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 then by common numeric identifier (no identifier is smallest)
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 then by tail (if there is a tail)
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 then by extension
    ///             </description>
    ///         </item>
    ///     </list>
    /// </remarks>
    public void Sort()
    {
        ThrowIfDisposed();

        //split entries entry names based on a regex
        //group the entries by their "prefix"
        //the prefix is all of the letters in the entry name up till the first digit
        var entryPartsGroupedByPrefix = Items.Select(
                                                 entry =>
                                                 {
                                                     var entryName = Path.GetFileNameWithoutExtension(entry.EntryName);
                                                     var extension = Path.GetExtension(entry.EntryName);

                                                     //if the entry name is a number, we can't split it
                                                     //just sort it based on that number as a string
                                                     if (int.TryParse(entryName, out _))
                                                         return new
                                                         {
                                                             Entry = entry,
                                                             Prefix = entryName,
                                                             NumericId = "",
                                                             Tail = "",
                                                             Extension = extension
                                                         };

                                                     var parts = RegexCache.EntryNameRegex
                                                                           .Matches(entryName)[0].Groups;

                                                     return new
                                                     {
                                                         Entry = entry,
                                                         Prefix = parts[1].Value,
                                                         NumericId = parts[2].Value,
                                                         Tail = parts[3].Value,
                                                         Extension = extension
                                                     };
                                                 })
                                             .GroupBy(parts => parts.Prefix, StringComparer.OrdinalIgnoreCase)
                                             .ToList();

        //look at each prefix grouping and extract a common length for the numeric part of the entry name
        //sometimes there will be a combination of entries with and without numeric identifiers
        //we prefer to store a non-zero length if possible
        var prefixToCommonIdentifierLength = entryPartsGroupedByPrefix.ToDictionary(
            group => group.Key,
            group =>
            {
                //order by numeric id length, take the first 3 entries
                var first3 = group.Select(parts => parts.NumericId.Length)
                                  .OrderBy(len => len)
                                  .Take(3)
                                  .ToList();

                //prefer to store a non-zero id length
                return first3 switch
                {
                    { Count: 0 }      => 0,
                    { Count: 1 }      => first3[0],
                    { Count: 2 or 3 } => first3.FirstOrDefault(num => num > 0),
                    _                 => throw new UnreachableException("We take 3, handling counts 0-3 should be all conditions")
                };
            },
            StringComparer.OrdinalIgnoreCase);

        //now that we know the common length for the id for each prefix
        //adjust the numeric id and tail to have the correct pieces of the entry name
        var correctedParts = entryPartsGroupedByPrefix.SelectMany(group => group)
                                                      .Select(
                                                          parts =>
                                                          {
                                                              var commonLength = prefixToCommonIdentifierLength[parts.Prefix];

                                                              //regex parsed more digits for this entry than some of the others with same prefix
                                                              //so we move those extra digits to the tail
                                                              if (parts.NumericId.Length > commonLength)
                                                                  return parts with
                                                                  {
                                                                      NumericId = parts.NumericId[..commonLength],
                                                                      Tail = parts.NumericId[commonLength..] + parts.Tail
                                                                  };

                                                              return parts;
                                                          });

        Items.Clear();

        var orderedEntries = correctedParts

                             //SORT BY PREFIX, UNDERSCORES ARE SPECIAL
                             .OrderBy(parts => parts.Prefix, PreferUnderscoreIgnoreCaseStringComparer.Instance)

                             //THEN BY COMMON NUMERIC IDENTIFIER
                             .ThenBy(
                                 parts =>
                                 {
                                     //grab the common length of the identifier for the prefix
                                     var commonIdentifierLength = prefixToCommonIdentifierLength[parts.Prefix];

                                     //if it's 0, or the numeric id is shorter than the common length, return -1
                                     //the numeric id can be shorter than the length if...
                                     //an entry was found WITH a numeric id, and another entry was found WITHOUT a numeric id
                                     //we prefer to take a numeric id if one seems like it exists
                                     if ((commonIdentifierLength == 0) || (parts.NumericId.Length < commonIdentifierLength))
                                         return -1;

                                     //parse the numeric id and sort by it
                                     return int.Parse(parts.NumericId);
                                 })

                             //THEN BY TAIL, UNDERSCORES ARE SPECIAL
                             .ThenBy(parts => parts.Tail, PreferUnderscoreIgnoreCaseStringComparer.Instance)

                             //THEN BY EXTENSION, UNDERSCORES ARE SPECIAL BUT PROBABLY DONT ACTUALLY MATTER
                             .ThenBy(parts => parts.Extension, PreferUnderscoreIgnoreCaseStringComparer.Instance)
                             .Select(parts => parts.Entry)
                             .ToList();

        Items.AddRange(orderedEntries);
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">
    ///     Thrown if the object is already disposed.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if the path is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown if the path is empty or contains invalid characters.
    /// </exception>
    /// <exception cref="PathTooLongException">
    ///     Thrown if the path exceeds the maximum length allowed.
    /// </exception>
    public virtual void Save(string path)
    {
        ThrowIfDisposed();

        using var buffer = new MemoryStream();
        Save(buffer);

        // ReSharper disable once ConvertToUsingDeclaration
        using var stream = File.Open(
            path.WithExtension(".dat"),
            new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Options = FileOptions.SequentialScan,
                Share = FileShare.ReadWrite,
                BufferSize = 81920
            });

        buffer.Seek(0, SeekOrigin.Begin);
        buffer.CopyTo(stream);
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">
    ///     Thrown if the object has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if an entry name is too long (must be 13 characters or less).
    /// </exception>
    public virtual void Save(Stream stream)
    {
        ThrowIfDisposed();

        const int HEADER_LENGTH = 4;
        const int ENTRY_HEADER_LENGTH = 4 + CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH;
        using var writer = new BinaryWriter(stream, Encoding.Default, true);

        writer.Write(Count + 1);

        //add the file header length
        //plus the entry header length * number of entries
        //plus 4 bytes for the final entry's end address (which could also be considered the total number of bytes)
        var address = HEADER_LENGTH + Count * ENTRY_HEADER_LENGTH + 4;

        Sort();

        foreach (var entry in Items)
        {
            //reconstruct the name field with the required terminator
            var nameStr = entry.EntryName;

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (nameStr.Length > CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH)
                throw new InvalidOperationException("Entry name is too long, must be 13 characters or less");

            if (nameStr.Length < CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH)
                nameStr = nameStr.PadRight(CONSTANTS.DATA_ARCHIVE_ENTRY_NAME_LENGTH, '\0');

            //get bytes for the name field (binaryWriter.Write(string) doesn't work for this)
            var nameStrBytes = Encoding.UTF8.GetBytes(nameStr);

            writer.Write(address);
            writer.Write(nameStrBytes);

            address += entry.FileSize;
        }

        writer.Write(address);

        foreach (var entry in Items)
        {
            using var segment = entry.ToStreamSegment();
            segment.CopyTo(stream);
        }
    }
    #endregion

    #region LoadFrom
    /// <summary>
    ///     Loads a DataArchive from a directory that contains already extracted entries. The resulting archive is not memory
    ///     mapped, and will be fully loaded into memory
    /// </summary>
    /// <param name="dir">
    ///     The directory path.
    /// </param>
    /// <returns>
    ///     A new DataArchive object.
    /// </returns>
    public static DataArchive FromDirectory(string dir)
    {
        //create a buffer with a count of 0 entries
        var buffer = new MemoryStream();
        buffer.Write(new byte[4]);
        buffer.Seek(0, SeekOrigin.Begin);

        //create a new in-memory archive
        //this will read the stream, finding 0 entries
        var archive = new DataArchive(buffer);
        buffer.Seek(0, SeekOrigin.End); //just incase

        //start the address at 4, since the first 4 bytes are the entry count
        var address = 4;

        //enumerate the directory, copying each file into the memory archive
        //maintain accurate address offsets for each file so that the archive is usable
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            using var stream = File.Open(
                file,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.SequentialScan,
                    Share = FileShare.ReadWrite,
                    BufferSize = 8192
                });

            stream.CopyTo(buffer);

            var entryName = Path.GetFileName(file);
            var length = (int)stream.Length;

            var entry = new DataArchiveEntry(
                archive,
                entryName,
                address,
                length);

            address += length;

            archive.Add(entry);
        }

        buffer.Seek(0, SeekOrigin.Begin);

        return archive;
    }

    /// <summary>
    ///     Loads a DataArchive from the specified path
    /// </summary>
    /// <param name="path">
    ///     The path to the file to create the DataArchive from.
    /// </param>
    /// <param name="memoryMapped">
    ///     Indicates whether to open the archive using a <see cref="MemoryMappedFile" />. An archive opened this way is not
    ///     fully loaded into memory, and can not be patched or saved. Using a memory mapped file is more performant for reads,
    ///     and uses less memory. Default is true, set it to false if you want to patch or save the archive.
    /// </param>
    /// <param name="newformat">
    ///     Indicates whether to use the new format when reading the archive. Default is false.
    /// </param>
    /// <returns>
    ///     A new instance of the DataArchive class.
    /// </returns>
    public static DataArchive FromFile(string path, bool memoryMapped = true, bool newformat = false)
    {
        if (memoryMapped)
        {
            //use a memory mapped file
            var fs = File.Open(
                path.WithExtension(".dat"),
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.RandomAccess,
                    Share = FileShare.ReadWrite,
                    BufferSize = 8192
                });

            var mappedFile = MemoryMappedFile.CreateFromFile(
                fs,
                null,
                0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                false);

            return new MemoryMappedDataArchive(mappedFile, newformat);
        }

        //load the file into memory
        using var fileStream = File.Open(
            path.WithExtension(".dat"),
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Options = FileOptions.RandomAccess,
                Share = FileShare.ReadWrite,
                BufferSize = 8192
            });

        var buffer = new MemoryStream((int)fileStream.Length);
        fileStream.CopyTo(buffer);

        buffer.Seek(0, SeekOrigin.Begin);

        return new DataArchive(buffer, newformat);
    }
    #endregion
}