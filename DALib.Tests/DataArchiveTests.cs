using System.Text;
using DALib.Data;

namespace DALib.Tests;

public class DataArchiveTests : IDisposable
{
    private readonly string TempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");

    public void Dispose()
    {
        if (File.Exists(TempPath))
            File.Delete(TempPath);
    }

    private static byte[] BuildArchive(int nameLength, params (string Name, byte[] Data)[] files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(files.Length + 1);

        var address = 4 + (files.Length + 1) * (4 + nameLength);

        foreach (var (name, data) in files)
        {
            var nameBytes = new byte[nameLength];
            Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);

            writer.Write(address);
            writer.Write(nameBytes);

            address += data.Length;
        }

        writer.Write(address);
        writer.Write(new byte[nameLength]);

        foreach (var (_, data) in files)
            writer.Write(data);

        return stream.ToArray();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void FromFile_ShouldReadFullNames_WhenEntriesHave32ByteNames(bool memoryMapped, bool newFormat)
    {
        File.WriteAllBytes(
            TempPath,
            BuildArchive(
                32,
                ("mu000038901.epf", [1, 2, 3]),
                ("mu000038902.epf", [4, 5]),
                ("palm001.pal", [6])));

        using var archive = DataArchive.FromFile(TempPath, memoryMapped, newFormat);

        archive.Select(entry => entry.EntryName)
               .Should()
               .Equal("mu000038901.epf", "mu000038902.epf", "palm001.pal");

        archive["mu000038901.epf"].FileSize.Should().Be(3);
        archive["mu000038902.epf"].FileSize.Should().Be(2);
        archive["palm001.pal"].FileSize.Should().Be(1);

        using var entryStream = archive.GetEntryStream(archive["mu000038902.epf"]);
        using var copy = new MemoryStream();
        entryStream.CopyTo(copy);

        copy.ToArray().Should().Equal(4, 5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromFile_ShouldReadNames_WhenEntriesHave13ByteNames(bool memoryMapped)
    {
        File.WriteAllBytes(
            TempPath,
            BuildArchive(
                13,
                ("mu38901.epf", [1, 2, 3]),
                ("palm001.pal", [4])));

        using var archive = DataArchive.FromFile(TempPath, memoryMapped);

        archive.Select(entry => entry.EntryName)
               .Should()
               .Equal("mu38901.epf", "palm001.pal");

        archive["mu38901.epf"].FileSize.Should().Be(3);
        archive["palm001.pal"].FileSize.Should().Be(1);
    }
}
