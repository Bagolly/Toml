using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using static Toml.Extensions.Extensions;
using static Toml.Tokenization.Constants;
using System.Threading.Tasks;
using Toml.Runtime;
using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommandLine;
using Toml.Diagnostics;

namespace Toml.Reader;


/// <summary>
/// Represents types capable of feeding input to a <see cref="TomlReader"/>.
/// </summary>
public interface ITomlReaderSource
{   
    /* Regarding UTF-8 validation
       Only the default TomlStreamSource does complete UTF-8 validation, while
       TomlStringSource assumes a valid UTF-16 string, as that's the contract 
       of System.String.
     
       If you provide your own implementation, you are responsible for
       indicating an UTF-8 encoding issue in the document. 
       Since StreamReader throws a DecoderException, this is caught, then wrapped internally.
       The tokenizer then reports them appropriately.

       The tokenizer validates all escape sequences, line-endings,
       and control characters, but because it works with characters and 
       not bytes, it cannot detect an invalid UTF-8 byte sequence.
     */


    /* Expected behavior:
       Short version: just like StreamReader's Read().
       
       Long version:  if there is an available character to read, it should be consumed (for example moving the cursor past the character)
                      and the character should be returned as an int.
                      If there are no more characters to be read, the method should return -1.
     */
    public int Read();


    /* Expected behavior:
       Short version: just like StreamReader's Peek().
       
       Long version:  if there is an available character to read, it should be returned, but not consumed, meaning you should be able to
                      call this mehtod an arbitrary amount of times without the returned character changing. 
                      The character should be returned as an int.
                      If there are no more characters to be read, the method should return -1.
     */
    public int Peek();


    /* Expected behavior:
       Short version: should work just like StreamReader's ReadBlock().
       
       Long version: this method should read characters into the provided buffer. It should NEVER read more characters 
                     than what was asked (meaning the length of the provided buffer). If there aren't enough characters
                     available, it should return as many as available.
                     The returned amount of characters should ALWAYS be between  0 and buffer.Length - 1 (both inclusive).
                     Newlines and new line characters should be returned as well, without any special handling, and MUST NOT be removed,
                     otherwise the line and column tracker in the tokenizer will be inaccurate.

                     See the documentation for StreamReader.ReadBlock() for more details.
                     Refer to TomlStreamSource and/or TomlStringSource for reference implementations.
     */
    public int ReadBlock(Span<char> buffer);
}



/// <summary>
/// Represents a reader for providing input to the TOML tokenizer.
/// </summary>
sealed class TomlReader
{
    public int Line
    {
        get => _line;
        private set { _line = value; Column = 1; }
    }

    private int _line;

    public int Column { get; private set; }

    public TomlDiagnosticsManager Logger { get; }

    private ITomlReaderSource Source { get; init; }

    /// <summary>
    /// Initializes the line and column trackers.
    /// </summary>
    public TomlReader(ITomlReaderSource source, TomlDiagnosticsManager logger)
    {
        Line = 1;
        Column = 1;
        Source = source;
        Logger = logger;
    }

    private void LogError(string message, ErrorSeverity s = ErrorSeverity.Error) 
        => Logger.Add(new TomlSyntaxError(Line, Column, message, s, ErrorDomain.Reader));
    

    #region Skip

    /// <summary>
    /// Consumes characters until the end of file, end of line, or a non-whitespace character is reached.
    /// </summary>
    /// <remarks>
    /// <paramref name="skipLineEndings"/>: skip line-endings as well.
    /// </remarks>
    /// <exception cref="TomlReaderException"></exception>
    public void SkipWhiteSpace(bool skipLineEnding = false)
    {
        int peekResult;
        while (true) 
            switch (peekResult = Source.Peek())
            {
                case SPACE or TAB:
                    _ = Source.Read();
                    ++Column;
                    break;

                case LF when skipLineEnding:
                    _ = Source.Read();
                    ++Line;
                    break;

                case CR when skipLineEnding:
                    _ = Source.Read();

                    if (Source.Read() is not LF)
                        LogError("Expected carriage return to be followed by a linefeed.");
                        
                    ++Line;
                    break;

                case CR or LF or EOF:
                    return;

                default:
                    if (char.IsControl((char)peekResult))
                    {
                        LogError($"Found unescaped control character '{GetFriendlyNameFor(peekResult)}'.");
                        
                        //Try to recover by skipping if in aggregate mode.
                        _ = Source.Read();
                    }
                    return;
            }
    }

    #endregion



    #region Read

    /// <summary>
    /// Returns the next available character, consuming it. 
    /// </summary>
    /// <returns>The next available character, or -1 if no more characters are available to read.</returns>
    public int Read()
    {
        int readResult = Source.Read();

        switch (readResult)
        {
            case EOF:
                break;

            case LF:
                ++Line;
                break;

            case CR:
                if (Source.Read() is not LF)
                    LogError("Expected carriage return to be followed by a linefeed.");

                goto case LF;
                
            default:
                if (char.IsControl((char)readResult))
                {
                    LogError($"Found unescaped control character '{GetFriendlyNameFor(readResult)}'.");
                    readResult = Read(); //Try to recover by reading to next when in aggregate mode.
                }

                ++Column;
                break;
        }

        return readResult;
    }


    /// <summary>
    /// Returns the next character without checking for control characters or newlines:
    /// <para>- Returns -1 if no more characters are available, otherwise returns the character.</para>
    /// <para>- Note that CR and LF are returned as well, no check for line endings is performed.</para>
    /// </summary>
    public int UncheckedRead()
    {
        int readResult = Source.Read();

        _ = readResult is LF ? ++Line : ++Column;

        return readResult;
    }


    /// <summary>
    /// Does what any try-variant of an exising method would do in C#.
    /// </summary>
    public bool TryRead(out char result)
    {
        int readResult = Source.Read();

        if (readResult is EOF)
        {
            result = default;
            return false;
        }

        result = (char)readResult;
        return true;
    }


    /// <summary>
    /// Wraps the base readers ReadBlock method to enable tracking position.
    /// </summary>
    public unsafe int ReadBlock(Span<char> buffer)
    {
        Debug.Assert(buffer.Length <= 512, "Possible misuse; the provided buffer is unusually large. Please double check usage!");

        int readResult = Source.ReadBlock(buffer);

        int lineIndex = buffer.IndexOf(LF); //It doesn't matter whether it's a CR or CRLF line-ending; only a line feed causes a line increase.

    
        if (lineIndex is not -1)
        {
            ++Line;
            Column = readResult - lineIndex + 1; //1 + because IndexOf() returns a zero-based index.
        }

        else
            Column += readResult;

        return readResult;
    }

    #endregion



    #region Match

    /// <summary>
    /// Consumes either an LF or CRLF line ending if it matches one.
    /// </summary>
    /// <returns><see langword="true"/> if a line ending was matched; otherwise <see langword="false"/>.</returns>
    public bool MatchLineEnding()
    {
        //should EOF count? Probably. But changing now could break previous code, so this stays.

        //Because this method is used in certain loops, this short circuit should help with most calls,
        //since the common case is no line endings.
        if (Source.Peek() > CR)
            return false;


        switch(Source.Peek())
        {   
            case LF:
                _ = Source.Read();
                ++Line;
                return true;

            case CR:
                _ = Source.Read();

                if (Source.Read() is not LF)
                    LogError("Expected carriage return to be followed by a linefeed.");

                ++Line;
                return true;
            
            default:
                return false;
        }
    }


    /// <summary>
    /// Consumes the next character from the stream if it matches <paramref name="c"/>.
    /// </summary>
    /// <returns> <see langword="true"/> if <paramref name="c"/> matches the next character; otherwise <see langword="false"/>.</returns>
    public bool MatchNext(char c)
    {
        if (Peek() == c)
        {
            _ = Read();
            return true;
        }

        return false;
    }


    /// <summary>
    /// Consumes characters while they match <paramref name="c"/>, and returns the number of matches.
    /// </summary>
    /// <returns>The number of sequential occurrences of <paramref name="c"/> from the stream's current position, or 0 if no characters matched.</returns>
    public int MatchedCountOf(char c)
    {
        int cnt = 0;

        while (MatchNext(c))
            ++cnt;

        return cnt;
    }


    /// <summary>
    /// Consumes the next character from the stream if it matches <paramref name="c"/>. Ignores tab or space.
    /// </summary>
    /// <returns> <see langword="true"/> if <paramref name="c"/> matches the next character; otherwise <see langword="false"/>.</returns>
    public bool MatchNextSkip(char c)
    {
        SkipWhiteSpace();

        return MatchNext(c);
    }

    #endregion



    #region Peek

    /// <summary>
    /// Returns the next available character, without consuming it.
    /// </summary>
    /// <returns>-1 if there are no characters to be read; otherwise, the next available character.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek() => Source.Peek();



    /// <summary>
    /// Returns the next available character, without consuming it. Ignores tab or space.
    /// </summary>
    /// <remarks>
    /// <paramref name="skipNewLine"/>: ignore LF and CRLF line-endings as well.
    /// </remarks>
    /// <returns>-1 if there are no characters to be read; otherwise, the next available character.</returns>
    public int PeekSkip(bool skipNewLine = false)
    {
        SkipWhiteSpace(skipNewLine);
        return Peek();
    }
    #endregion
}