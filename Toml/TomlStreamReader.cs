using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Transactions;
using Toml.Runtime;
using static Toml.Extensions.TomlExtensions;
using Toml.Tokenization;
using static Toml.Tokenization.Constants;

namespace Toml;


sealed class TOMLStreamReader : IDisposable
{
    internal StreamReader BaseReader { get; init; }

    internal int Line
    {
        get => _line;
        private set { _line = value; Column = 1; }
    }

    private int _line;


    internal int Column { get; private set; }


    internal ReadOnlySpan<char> Position => $"Line {Line} Column {Column}";


    public TOMLStreamReader(Stream source)
    {
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Streams without read or seeking support cannot be used.");


        //Throw on invalid UTF8, don't emit BOM, and do not detect encoding, since it MUST be valid UTF8, otherwise it's an error.
        BaseReader = new(source, new UTF8Encoding(false, true), false);
        Column = 1;
        Line = 1;
    }


    public void SkipWhiteSpace(bool skipLineEnding = false)
    {
        while (true)
        {
            int peekResult = BaseReader.Peek();
            switch (peekResult)
            {
                case SPACE or TAB:
                    _ = BaseReader.Read();
                    ++Column;
                    break;

                case LF when skipLineEnding:
                    _ = BaseReader.Read();
                    ++Line;
                    break;

                case CR when skipLineEnding:
                    _ = BaseReader.Read();

                    if (BaseReader.Read() != LF)
                    {
                        throw new TomlReaderException($"Expected carriage return to be followed by a linefeed at (line {Line}, col {Column})");
                    }
                    ++Line;
                    break;

                case CR or LF or EOF:
                    return;

                default:
                    if (char.IsControl((char)peekResult))
                        throw new TomlReaderException($"Found unescaped control character '{GetFriendlyNameFor(peekResult)}' at (line {(Column == 0 ? Line - 1 : Line)}, col {Column})");
                    return;
            }
        }
    }

    /// <summary>
    /// Returns the next available character, consuming it. 
    /// </summary>
    /// <returns>The next available character, or -1 if no more characters are available to read.</returns>
    public int Read()
    {
        int readResult = BaseReader.Read();

        switch (readResult)
        {
            case EOF:
                break;

            case LF:
                ++Line;
                break;

            case CR:
                if (BaseReader.Read() != LF)
                    throw new TomlReaderException($"Expected carriage return to be followed by a linefeed at (line {Line}, col {Column})");
                goto case LF;

            default:
                if (char.IsControl((char)readResult))
                    throw new TomlReaderException($"Found unescaped control character '{GetFriendlyNameFor(readResult)}' at (line {(Column == 0 ? Line - 1 : Line)}");
                ++Column;
                break;
        }

        return readResult;
    }


    public bool TryRead(out char result)
    {
        int readResult = BaseReader.Read();

        if(readResult is EOF)
        {   
            result = default;
            return false;
        }

        result = (char)readResult;
        return true;
    }

    //Used in strings because TOML thought it a good idea to have 4 different types of them.
    //This returns the character without the checks in a normal Read(), so that the validation
    //can occur in the string tokenizing method instead of here.
    //Also, since the secondary buffering is only relevant for numbers and datetime types, this
    //method cannot be used with the secondary buffer.
    /// <summary>
    /// Returns the next character without checking for control characters or newlines:
    /// <para>- Returns -1 if no more characters are available, otherwise returns the character.</para>
    /// <para>- Note that CR and LF are returned as well, no check for CRLF is performed.</para>
    /// </summary>
    public int UncheckedRead()
    {
        int readResult = BaseReader.Read();

        _ = readResult == LF ? ++Line : ++Column;

        return readResult;
    }


    public int ReadBlock(Span<char> buffer)
    {
        int readResult = BaseReader.ReadBlock(buffer);
        
        int lineIndex = buffer.IndexOf(Constants.LF); //It doesn't matter whether it's a CR or CRLF line-ending; only LF causes a line increase.


        if (lineIndex != -1)
        {
            ++Line;
            Column = readResult - lineIndex + 1; //1 + because IndexOf() returns a zero-based index.
        }

        else
            Column += readResult;

        return readResult;
    }


    /// <summary>
    /// Returns the next available character, consuming it. Ignores tab or skip.
    /// </summary>
    /// <remarks>
    /// <paramref name="skipNewLine"/>: ignore LF and CRLF line-endings as well.
    /// </remarks>
    /// <returns>The next available character, or -1 if no more characters are available to read.</returns>
    public int ReadSkip(bool skipNewLine = false)
    {
        SkipWhiteSpace(skipNewLine);
        return Read();
    }


    /// <summary>
    /// Returns the next available character, without consuming it.
    /// </summary>
    /// <returns>-1 if there are no characters to be read; otherwise, the next available character.</returns>
    public int Peek() => BaseReader.Peek();


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
    /// Consumes either an LF or CRLF line ending if it matches one.
    /// </summary>
    /// <returns><see langword="true"/> if a line ending was matched; otherwise <see langword="false"/>.</returns>
    public bool MatchLineEnding()
    {
        int peekResult = BaseReader.Peek();

        if (peekResult == LF)
        {
            _ = BaseReader.Read();
            ++Line;
            return true;
        }


        if (peekResult == CR)
        {
            _ = BaseReader.Read();

            if (BaseReader.Read() != LF)
                throw new TomlReaderException($"Expected carriage return to be followed by a linefeed at (line {Line}, col {Column})");

            ++Line;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Consumes characters while they match <paramref name="c"/>.
    /// </summary>
    /// <param name="c"></param>
    public void SkipWhile(char c)
    {
        while (MatchNext(c)) ;
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

    public void Dispose() => BaseReader.Dispose();
}
