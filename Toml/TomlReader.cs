using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using static Toml.Extensions.TomlExtensions;
using static Toml.Tokenization.Constants;
using System.Threading.Tasks;
using Toml.Runtime;
using System.IO;

namespace Toml.Reader;


/// <summary>
/// Represents types capable of feeding input to a <see cref="TomlReader"/>.
/// </summary>
public interface ITomlReaderSource
{   
    /* Regarding UTF-8 validation
       Only the default TomlStreamSource does complete UTF-8 validation.
     
       If you provide your own implementation, you should throw a DecoderException
       to indicate an UTF-8 encoding issue in the document.
       The tokenizer catches these and reports them appropriately.

       The tokenizer does validate escape sequences and line-endings,
       as well as control characters in strings and comments.
       
       But because it works with characters and not bytes, it cannot detect an invalid
       UTF-8 byte sequence.
     */


    /* Expected behavior:
       Short version: just like StreamReader's Read().
       
       Long version:  if there is an available character to read, it should be consumed (for example moving the cursor past the character)
                      and the character should be returned as an int.
                      If there are no more characters to be read, the method should return -1.
     */
    public abstract int Read();


    /* Expected behavior:
       Short version: just like StreamReader's Peek().
       
       Long version:  if there is an available character to read, it should be returned, but not consumed, meaning you should be able to
                      call this mehtod an arbitrary amount of times without the returned character changing. 
                      The character should be returned as an int.
                      If there are no more characters to be read, the method should return -1.
     */
    public abstract int Peek();


    /* Expected behavior:
       Short version: just like StreamReader's ReadBlock().
       
       Long version: this method should read characters into the provided buffer. It should NEVER read more characters 
                     than what was asked (meaning the length of the provided buffer). If there aren't enough characters
                     available it should return as many as available.
                     Essentially, the returned amount of characters should ALWAYS be between  0 and buffer.Length - 1 (both inclusive).
                     Newlines and new line characters should be returned as well, without any special handling, and MUST NOT be removed,
                     otherwise the line and column tracker in the tokenizer will be inaccurate (especially for documents with lots of timestamps).

                     See the documentation for StreamReader.ReadBlock() for more details on expected behavior and/or implementation.
     */
    public abstract int ReadBlock(Span<char> buffer);
}



/// <summary>
/// Provides the base implementation for a reader capable of providing input for the TOML tokenizer.
/// </summary>
class TomlReader
{
    public int Line
    {
        get => _line;
        protected set { _line = value; Column = 1; }
    }

    private int _line;

    public int Column { get; protected set; }

    public ReadOnlySpan<char> Position => $"Line {Line} Column {Column}";


    protected ITomlReaderSource Source { get; init; }



    /// <summary>
    /// Initializes the line and column trackers.
    /// </summary>
    public TomlReader(ITomlReaderSource source)
    {
        Line = 1;
        Column = 1;
        Source = source;
    }


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
        while (true)
        {
            int peekResult = Source.Peek(); ;
            switch (peekResult)
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
                    {
                        throw new TomlReaderException("Expected carriage return to be followed by a linefeed.", Line, Column);
                    }
                    ++Line;
                    break;

                case CR or LF or EOF:
                    return;

                default:
                    if (char.IsControl((char)peekResult))
                        throw new TomlReaderException($"Found unescaped control character '{GetFriendlyNameFor(peekResult)}'.", Line, Column);
                    return;
            }
        }
    }


    /// <summary>
    /// Consumes characters while they match <paramref name="c"/>.
    /// </summary>
    public void SkipWhile(char c) { while (MatchNext(c)) ; }

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
                    throw new TomlReaderException($"Expected carriage return to be followed by a linefeed.", Line, Column);
                goto case LF;
                
            default:
                if (char.IsControl((char)readResult))
                    throw new TomlReaderException($"Found unescaped control character '{GetFriendlyNameFor(readResult)}'.", Line, Column);
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
    /// Does what any try-variant of an exising method would do in C#. I'm getting tired of writing these for internal types...
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
    /// Returns the next available character, consuming it. Ignores tab and space.
    /// </summary>
    /// <remarks>
    /// <paramref name="skipNewLine"/>: skip line-endings as well.
    /// </remarks>
    /// <returns>The next available character, or -1 if no more characters are available to read.</returns>
    public int ReadSkip(bool skipNewLine = false)
    {
        SkipWhiteSpace(skipNewLine);
        return Read();
    }


    /// <summary>
    /// Wraps the base readers ReabBlock method to enable tracking position.
    /// </summary>
    public int ReadBlock(Span<char> buffer)
    {
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
        switch(Source.Peek())
        {   
            case LF:
                _ = Source.Read();
                ++Line;
                return true;

            case CR:
                _ = Source.Read();

                if (Source.Read() is not LF)
                    throw new TomlReaderException($"Expected carriage return to be followed by a linefeed.", Line, Column);

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