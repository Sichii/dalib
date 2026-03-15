using DALib.IO;
using Xunit.Abstractions;

namespace DALib.Tests;

public class CompressionTests
{
    private readonly ITestOutputHelper _output;

    public CompressionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Compression_Decompression_RoundTrip_ShouldPreserveData()
    {
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        var compressed = Compression.CompressHpf(testData);
        var decompressedSpan = new Span<byte>(compressed);
        Compression.DecompressHpf(ref decompressedSpan);
        var decompressed = decompressedSpan.ToArray();

        decompressed.Should().BeEquivalentTo(testData);
        _output.WriteLine($"Original: {testData.Length} bytes");
        _output.WriteLine($"Compressed: {compressed.Length} bytes");
        _output.WriteLine($"Decompressed: {decompressed.Length} bytes");
    }

    [Fact]
    public void Compression_ShouldProduceValidHeader()
    {
        var testData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };

        var compressed = Compression.CompressHpf(testData);

        compressed.Length.Should().BeGreaterThanOrEqualTo(4);
        var signature = BitConverter.ToUInt32(compressed, 0);
        signature.Should().Be(0xFF02AA55);
    }

    [Fact]
    public void Compression_ShouldReduceDataSize()
    {
        var testData = new byte[1000];
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        var compressed = Compression.CompressHpf(testData);

        compressed.Length.Should().BeLessThan(testData.Length + 4);
        _output.WriteLine($"Original: {testData.Length} bytes");
        _output.WriteLine($"Compressed: {compressed.Length} bytes");
        _output.WriteLine($"Compression ratio: {(1.0 - (double)compressed.Length / testData.Length):P2}");
    }

    [Theory]
    [InlineData("stc00000.hpf")]
    [InlineData("stc00001.hpf")]
    [InlineData("stc00002.hpf")]
    public void RealHpfFile_RoundTrip_ShouldPreserveData(string fileName)
    {
        var possiblePaths = new[]
        {
            Path.Combine("..", "..", "..", "TestData", fileName),
            Path.Combine("TestData", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData", fileName)
        };

        string? filePath = null;
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                filePath = path;
                break;
            }
        }

        filePath.Should().NotBeNull($"Test data file {fileName} must exist. Run ExtractTestData.cs to generate test data.");
        File.Exists(filePath).Should().BeTrue($"Test data file not found at expected location: {filePath}");

        var originalBytes = File.ReadAllBytes(filePath);
        _output.WriteLine($"Testing {Path.GetFileName(filePath)} ({originalBytes.Length:N0} bytes)");

        var signature = BitConverter.ToUInt32(originalBytes, 0);
        Assert.Equal(0xFF02AA55u, signature);

        var decompressSpan = new Span<byte>(originalBytes.ToArray());
        Compression.DecompressHpf(ref decompressSpan);
        var decompressed = decompressSpan.ToArray();

        var recompressed = Compression.CompressHpf(decompressed);

        recompressed.Length.Should().Be(originalBytes.Length);

        var redecompressSpan = new Span<byte>(recompressed.ToArray());
        Compression.DecompressHpf(ref redecompressSpan);
        var redecompressed = redecompressSpan.ToArray();

        redecompressed.Should().BeEquivalentTo(decompressed);

        _output.WriteLine($"   Original compressed: {originalBytes.Length:N0} bytes");
        _output.WriteLine($"   Decompressed: {decompressed.Length:N0} bytes");
        _output.WriteLine($"   Recompression: {recompressed.Length:N0} bytes");
    }

    [Fact]
    public void HpfDecompression_ShouldProduceValidImageData()
    {
        var possiblePaths = new[]
        {
            Path.Combine("..", "..", "..", "TestData", "stc00000.hpf"),
            Path.Combine("TestData", "stc00000.hpf"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData", "stc00000.hpf")
        };

        string? testFile = null;
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                testFile = path;
                break;
            }
        }

        testFile.Should().NotBeNull("Test data file stc00000.hpf must exist. Run ExtractTestData.cs to generate test data.");
        File.Exists(testFile).Should().BeTrue($"Test data file not found at expected location: {testFile}");

        var compressedBytes = File.ReadAllBytes(testFile);

        var decompressSpan = new Span<byte>(compressedBytes.ToArray());
        Compression.DecompressHpf(ref decompressSpan);
        var decompressed = decompressSpan.ToArray();

        decompressed.Should().NotBeNull();
        decompressed.Length.Should().BeGreaterThan(0);

        decompressed.Length.Should().BeGreaterThan(100);
        decompressed.Length.Should().BeLessThan(1000000);

        decompressed.Any(b => b != 0).Should().BeTrue("Decompressed data should contain non-zero bytes");

        _output.WriteLine($"   Compressed: {compressedBytes.Length:N0} bytes");
        _output.WriteLine($"   Decompressed: {decompressed.Length:N0} bytes");
        _output.WriteLine($"   Ratio: {(1.0 - (double)compressedBytes.Length / decompressed.Length) * 100:F2}%");
    }

    [Fact]
    public void EmptyData_ShouldHandleGracefully()
    {
        var emptyData = Array.Empty<byte>();

        var compressed = Compression.CompressHpf(emptyData);
        var decompressSpan = new Span<byte>(compressed);
        Compression.DecompressHpf(ref decompressSpan);
        var decompressed = decompressSpan.ToArray();

        decompressed.Should().BeEquivalentTo(emptyData);
        compressed.Length.Should().BeGreaterThanOrEqualTo(4);
        _output.WriteLine($"Empty data compressed to {compressed.Length} bytes (includes header + termination)");
    }

    [Fact]
    public void LargeData_ShouldCompressEfficiently()
    {
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        var compressed = Compression.CompressHpf(largeData);

        compressed.Length.Should().BeLessThan(largeData.Length);
        var ratio = (1.0 - (double)compressed.Length / largeData.Length) * 100;
        ratio.Should().BeGreaterThan(0);

        _output.WriteLine($"Large data compression:");
        _output.WriteLine($"   Original: {largeData.Length:N0} bytes");
        _output.WriteLine($"   Compressed: {compressed.Length:N0} bytes");
        _output.WriteLine($"   Ratio: {ratio:F2}%");
    }

    [Theory]
    [InlineData("stc00000.hpf")]
    [InlineData("stc00001.hpf")]
    [InlineData("stc00002.hpf")]
    public void CompressedOutput_ShouldMatchOriginalByteByByte(string fileName)
    {
        var possiblePaths = new[]
        {
            Path.Combine("..", "..", "..", "TestData", fileName),
            Path.Combine("TestData", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData", fileName)
        };

        string? filePath = null;
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                filePath = path;
                break;
            }
        }

        filePath.Should().NotBeNull($"Test data file {fileName} must exist. Run ExtractTestData.cs to generate test data.");
        File.Exists(filePath).Should().BeTrue($"Test data file not found at expected location: {filePath}");

        var originalCompressedBytes = File.ReadAllBytes(filePath);
        _output.WriteLine($"Testing {Path.GetFileName(filePath)} ({originalCompressedBytes.Length:N0} bytes)");

        var signature = BitConverter.ToUInt32(originalCompressedBytes, 0);
        Assert.Equal(0xFF02AA55u, signature);

        var decompressSpan = new Span<byte>(originalCompressedBytes.ToArray());
        Compression.DecompressHpf(ref decompressSpan);

        var decompressedBytes = decompressSpan.ToArray();
        var recompressedBytes = Compression.CompressHpf(decompressedBytes);

        recompressedBytes.Length.Should().Be(originalCompressedBytes.Length, "Recompressed data must be same length as original");
        recompressedBytes.Should().BeEquivalentTo(originalCompressedBytes, "Compressed output must match original byte-for-byte");

        for (int i = 0; i < originalCompressedBytes.Length; i++)
        {
            if (originalCompressedBytes[i] != recompressedBytes[i])
            {
                Assert.Fail($"Byte mismatch at position {i}: expected 0x{originalCompressedBytes[i]:X2}, got 0x{recompressedBytes[i]:X2}");
            }
        }

        var originalCrc = CalculateCrc32(originalCompressedBytes);
        var recompressedCrc = CalculateCrc32(recompressedBytes);
        originalCrc.Should().Be(recompressedCrc, "CRC32 checksums must match");

        _output.WriteLine($"   Original CRC32:   0x{originalCrc:X8}");
        _output.WriteLine($"   Recompressed CRC32: 0x{recompressedCrc:X8}");
        _output.WriteLine($"   All {originalCompressedBytes.Length:N0} bytes identical");
    }

    private static uint CalculateCrc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
