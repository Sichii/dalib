using System;
using System.Collections.Generic;
using System.IO;

namespace DALib.IO;

/// <summary>
///     Provides methods for compression and decompression
/// </summary>
public static class Compression
{
    /// <summary>
    ///     Decompresses HPF data in-place.
    /// </summary>
    /// <param name="buffer">
    ///     The buffer containing the HPF data to decompress.
    /// </param>
    public static void DecompressHpf(ref Span<byte> buffer)
    {
        // method written by Eru/illuvatar

        uint k = 7;
        uint val = 0;
        uint i;
        uint l = 0;
        var m = 0;

        var hpfSize = buffer.Length;
        var intermediaryBuffer = new byte[hpfSize * 10];
        Span<byte> rawBytes = intermediaryBuffer;

        var intOdd = new uint[256];
        var intEven = new uint[256];
        var bytePair = new byte[513];

        for (i = 0; i < 256; i++)
        {
            intOdd[i] = 2 * i + 1;
            intEven[i] = 2 * i + 2;

            bytePair[i * 2 + 1] = (byte)i;
            bytePair[i * 2 + 2] = (byte)i;
        }

        while (val != 0x100)
        {
            val = 0;

            while (val <= 0xFF)
            {
                if (k == 7)
                {
                    l++;
                    k = 0;
                } else
                    k++;

                val = (buffer[4 + (int)l - 1] & (1 << (int)k)) != 0 ? intEven[val] : intOdd[val];
            }

            var val3 = val;
            uint val2 = bytePair[val];

            while ((val3 != 0) && (val2 != 0))
            {
                i = bytePair[val2];
                var j = intOdd[i];

                if (j == val2)
                {
                    j = intEven[i];
                    intEven[i] = val3;
                } else
                    intOdd[i] = val3;

                if (intOdd[val2] == val3)
                    intOdd[val2] = j;
                else
                    intEven[val2] = j;

                bytePair[val3] = (byte)i;
                bytePair[j] = (byte)val2;
                val3 = i;
                val2 = bytePair[val3];
            }

            val += 0xFFFFFF00;

            if (val == 0x100)
                continue;

            rawBytes[m] = (byte)val;
            m++;
        }

        buffer = rawBytes[..m];
    }
    
    public static byte[] CompressHpf(Span<byte> buffer)
    {
        Span<uint> intOdd   = stackalloc uint[256];
        Span<uint> intEven  = stackalloc uint[256];
        Span<byte> bytePair = stackalloc byte[513];

        for (uint i = 0; i < 256; i++)
        {
            intOdd   [(int)i] = 2 * i + 1;
            intEven  [(int)i] = 2 * i + 2;
            bytePair [(int)(i * 2 + 1)] = (byte)i;
            bytePair [(int)(i * 2 + 2)] = (byte)i;
        }

        var bits = new List<bool>(buffer.Length * 8);

        for (int byteIndex = 0; byteIndex <= buffer.Length; byteIndex++)
        {
            uint symbol = byteIndex < buffer.Length ? buffer[byteIndex] : 0x100u;
            uint targetNode = symbol + 0x100;
            uint currentNode = 0;

            while (currentNode != targetNode)
            {
                if (IsNodeInSubtree(targetNode, intOdd[(int)currentNode], intOdd, intEven))
                {
                    bits.Add(false);
                    currentNode = intOdd[(int)currentNode];
                }
                else if (IsNodeInSubtree(targetNode, intEven[(int)currentNode], intOdd, intEven))
                {
                    bits.Add(true);
                    currentNode = intEven[(int)currentNode];
                }
                else
                    throw new InvalidDataException($"Cannot reach node {targetNode} from {currentNode}");
            }

            uint val  = targetNode;
            uint val3 = val;
            uint val2 = bytePair[(int)val];

            while ((val3 != 0) && (val2 != 0))
            {
                byte idx = bytePair[(int)val2];
                uint j   = intOdd[(int)idx];

                if (j == val2)
                {
                    j = intEven[(int)idx];
                    intEven[(int)idx] = val3;
                }
                else
                {
                    intOdd[(int)idx] = val3;
                }

                if (intOdd[(int)val2] == val3)
                    intOdd[(int)val2] = j;
                else
                    intEven[(int)val2] = j;

                bytePair[(int)val3] = idx;
                bytePair[(int)j] = (byte)val2;
                val3 = idx;
                val2 = bytePair[(int)val3];
            }
        }

        var compressedSize = (bits.Count + 7) / 8;
        var compressedData = new byte[compressedSize];

        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                int byteIdx = i / 8;
                int bitIdx  = i % 8;
                compressedData[byteIdx] |= (byte)(1 << bitIdx);
            }
        }

        var output = new byte[4 + compressedSize];
        output[0] = 0x55;
        output[1] = 0xAA;
        output[2] = 0x02;
        output[3] = 0xFF;

        compressedData.CopyTo(output.AsSpan(4));
        return output;
    }

    private static bool IsNodeInSubtree(uint target, uint root, Span<uint> intOdd, Span<uint> intEven)
    {
        if (root == target) return true;
        if (root > 0xFF) return false;
        return IsNodeInSubtree(target, intOdd[(int)root], intOdd, intEven) ||
               IsNodeInSubtree(target, intEven[(int)root], intOdd, intEven);
    }
}