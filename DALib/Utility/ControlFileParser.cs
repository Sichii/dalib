using System;
using System.Collections.Generic;
using System.IO;
using DALib.Definitions;
using DALib.Drawing;
using KGySoft.CoreLibraries;
using SkiaSharp;

namespace DALib.Utility;

/// <summary>
///     Provides methods for parsing Controls from a stream
/// </summary>
public sealed class ControlFileParser
{
    private TokenType CurrentToken;

    private void HandleTokenNoValue(ControlFile controlFile, ref Control? currentControl, TokenType token)
    {
        switch (token)
        {
            case TokenType.Control:
            {
                currentControl = new Control();

                break;
            }
            case TokenType.EndControl:
            {
                //if the control has images
                if (currentControl?.Images?.Count > 0)
                {
                    var images = currentControl.Images;
                    var currentIndex = 0;
                    var expandedImages = new List<(string ImageName, int FrameIndex)>();
            
                    //expand the image list
                    //the control files only give us the first and last image indexes, we need to fill in the rest outselves
                    while (true)
                    {
                        var firstEntry = images[currentIndex];
                        var startIndex = images.FindIndex(pair => pair.ImageName.Equals(firstEntry.ImageName));
                        var endIndex = images.FindLastIndex(pair => pair.ImageName.Equals(firstEntry.ImageName));

                        var startNum = images[startIndex].FrameIndex;
                        var endNum = images[endIndex].FrameIndex;
                        
                        for (var i = startNum; i <= endNum; i++)
                            expandedImages.Add((firstEntry.ImageName, i));

                        currentIndex = endIndex + 1;

                        if (currentIndex >= images.Count)
                            break;
                    }
            
                    currentControl.Images = expandedImages;
                }
                
                controlFile.TryAdd(currentControl!, throwError: false);
                currentControl = null;

                break;
            }
            case TokenType.Color:
                currentControl!.ColorIndexes = new List<int>();

                break;
            case TokenType.Value:
                break;
            case TokenType.Image:
                currentControl!.Images = new List<(string ImageName, int FrameIndex)>();

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void HandleTokenWithValue(ref Control currentControl, TokenType token, string value)
    {
        switch (token)
        {
            case TokenType.Name:
            {
                currentControl.Name = value;

                break;
            }
            case TokenType.Type:
            {
                currentControl.Type = (ControlType)int.Parse(value);

                break;
            }
            case TokenType.Rect:
            {
                var parts = value.Split(' ');

                var left = int.Parse(parts[0]);
                var top = int.Parse(parts[1]);
                var right = int.Parse(parts[2]);
                var bottom = int.Parse(parts[3]);

                currentControl.Rect = new SKRect(
                    left,
                    top,
                    right,
                    bottom);

                break;
            }

            //current means no token was parsed, and any values should be considered as part of the CurrentToken
            case TokenType.Current:
                switch (CurrentToken)
                {
                    case TokenType.Color:
                    {
                        currentControl.ColorIndexes!.Add(int.Parse(value));

                        break;
                    }
                    case TokenType.Value:
                    {
                        currentControl.ReturnValue = int.Parse(value);

                        break;
                    }
                    case TokenType.Image:
                    {
                        var parts = value.Split(' ');

                        var imageName = parts[0]
                            .Trim('"');
                        var paletteNum = int.Parse(parts[1]);

                        currentControl.Images!.Add((imageName, paletteNum));

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>
    ///     Parses Controls from a stream, adding them to the specified controlFile
    /// </summary>
    /// <param name="controlFile">
    ///     The control file object to populate with data.
    /// </param>
    /// <param name="stream">
    ///     The stream containing the control file data.
    /// </param>
    public void Parse(ControlFile controlFile, Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        Control? currentControl = null;

        while (reader.ReadLine() is { } line)
        {
            //parse the line into a token and/or value
            var token = ParseToken(line, out var value);

            //if there is no current control, and the token is not a control, something is wrong
            if ((currentControl == null) && (token != TokenType.Control))
                throw new InvalidOperationException("Invalid control file");

            //if the token was parsed by itself with no value
            if (string.IsNullOrEmpty(value))
                HandleTokenNoValue(controlFile, ref currentControl, token);
            else if (currentControl == null) //if the currentControl is null, something is wrong
                throw new InvalidOperationException("Invalid control file");
            else //if the current token was parsed with a value
                HandleTokenWithValue(ref currentControl, token, value);

            //if a new token was parsed, set it to the current token
            //on lines that do not contain a new token, such as a line that only contains a value, the tokentype will be "Current"
            //meaning, "use the current token"
            //we dont ever want to set the current token to "Current"
            if (token != TokenType.Current)
                CurrentToken = token;
        }
    }

    private TokenType ParseToken(string line, out string? value)
    {
        value = null;
        line = line.Trim();

        if (line.StartsWith("<CONTROL>", StringComparison.OrdinalIgnoreCase))
            return TokenType.Control;

        if (line.StartsWith("<ENDCONTROL>", StringComparison.OrdinalIgnoreCase))
            return TokenType.EndControl;

        if (line.StartsWith("<NAME>", StringComparison.OrdinalIgnoreCase))
        {
            value = line[8..^1];

            return TokenType.Name;
        }

        if (line.StartsWith("<TYPE>", StringComparison.OrdinalIgnoreCase))
        {
            value = line[7..];

            return TokenType.Type;
        }

        if (line.StartsWith("<RECT>", StringComparison.OrdinalIgnoreCase))
        {
            value = line[7..];

            return TokenType.Rect;
        }

        if (line.StartsWith("<COLOR>", StringComparison.OrdinalIgnoreCase))
            return TokenType.Color;

        if (line.StartsWith("<VALUE>", StringComparison.OrdinalIgnoreCase))
            return TokenType.Value;

        if (line.StartsWith("<IMAGE>", StringComparison.OrdinalIgnoreCase))
            return TokenType.Image;

        value = line;

        return TokenType.Current;
    }

    private enum TokenType
    {
        Current = 0,
        Control = 1,
        EndControl = 2,
        Name = 3,
        Type = 4,
        Rect = 5,
        Color = 6,
        Value = 7,
        Image = 8
    }
}