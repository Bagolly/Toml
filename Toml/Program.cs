global using System;
using static System.Char;
using Toml.Runtime;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Collections.Frozen;
using System.Text.Json;
using System.Linq;
using System.IO.Pipes;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using BenchmarkDotNet.Columns;
using Microsoft.CodeAnalysis.CSharp.Syntax;
#pragma warning disable IDE0290


namespace Toml;

internal class Program
{
    static void Main()
    {   /*
        TTable root = [];

        ReadOnlySpan<char> key1 = "fruit.apple";
        ReadOnlySpan<char> key2 = "animal";
        ReadOnlySpan<char> key3 = "fruit.orange";


        var subtable = ResolveKey(root, in key1); //returns the table at "ab.cde.fghij"
        subtable.Add("color", new TString("green"));

        subtable = ResolveKey(root, in key2);    //returns the table at "ab.cde.fghij.l.g"
        subtable.Add("lifespan", new TInteger(27));

        subtable = ResolveKey(root, in key3);
        subtable.Add("color", new TString("guess..."));


        Console.WriteLine("Test1 | fruit.apple.color: " + root["fruit"]["apple"]["color"]);
        Console.WriteLine("Test2 | animal.lifespan: " + root["animal"]["lifespan"]);
        Console.WriteLine("Test3 | fruit.orange.color: " + root["fruit"]["orange"]["color"]);
        */

        using FileStream fs = new("C:/Users/BAGOLY/Desktop/a.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        TOMLTokenizer t = new(fs);


        //StreamReader sr = new("C:\\Users\\BAGOLY\\Desktop\\a.txt");
    }


    private static IEnumerable<(int start, int end)> GetKey(string str)
    {
        int start = 0;


        for (int i = 0; i < str.Length; i++)
        {
            if (i == str.Length - 1) //if not dotted
                yield return (start, i + 1);

            else if (str[i] == '.')
            {
                yield return (start, i);
                ++i;
                start = i;
            }
            else
                continue;
        }
    }

    //Constructs a hierarchy of tables from a given key and
    //returns a reference to the last added table (the active "scope").
    //DO NOT PASS the key to the value you want to add. Instead, provide the path (all but the last fragment),
    //then add the value to the table using the returned reference.
    private static TTable ResolveKey(TTable root, in ReadOnlySpan<char> str)
    {
        TTable containingTable = root;
        int start = 0;

        if (!str.Contains('.'))
            ProcessFragment(in str, start, str.Length);

        else
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (i == str.Length - 1)
                    ProcessFragment(in str, start, i + 1);

                if (str[i] == '.')
                {
                    ProcessFragment(in str, start, i);
                    ++i;
                    start = i;
                }
            }
        }

        return containingTable;

        void ProcessFragment(in ReadOnlySpan<char> str, int start, int end)
        {
            if (containingTable.Values.ContainsKey(str[start..end].ToString()))
                containingTable = (TTable)containingTable[str[start..end]];
            else
            {
                TTable subtable = [];
                containingTable.Values.Add(str[start..end].ToString(), subtable);
                containingTable = subtable;
            }
        }
    }



    private static void SkipWhiteSpace(string s, ref int i)
    {
        if (s is "")
            return;

        while (s[i++] is '\t' or ' ') ;

        i -= 1;
    }
}


sealed class TOMLTokenizer
{
    #region Alphabet
    internal const char EOF = '\0';
    internal const char Tab = '\t';
    internal const char Space = ' ';
    internal const char CR = '\r';
    internal const char LF = '\n';
    internal const char Comment = '#';
    internal const char DoubleQuote = '"';
    internal const char SingleQuote = '\'';
    internal const char UnderScore = '_';
    internal const char Dash = '-';
    internal const char EqualsSign = '=';
    internal const char Dot = '.';
    internal const char Comma = ',';
    internal const char SquareOpen = '[';
    internal const char SquareClose = ']';
    internal const char CurlyOpen = '{';
    internal const char CurlyClose = '}';
    internal const string Empty = "";
    #endregion

    private TOMLStreamReader Reader { get; init; }

    private Queue<TOMLToken> TokenStream { get; init; }

    Lazy<List<string>> TempErrorLogger { get; init; }

    public TOMLTokenizer(Stream source, int capacity = 32)
    {
        Reader = new(source);
        TokenStream = new(capacity);
        TempErrorLogger = new();
    }

    public Queue<TOMLToken> TokenizeFile()
    {
        while (Reader.Peek() is not EOF)
        {
            Tokenize();
        }

        TokenStream.Enqueue(new(TOMLTokenType.Eof, -1, -1));
        return TokenStream;
    }

    private void Synchronize(int syncMinimum) //Skips syncMinimum amount of characters
    {
        while (syncMinimum != 0)
        {
            if (Reader.Read() is EOF)
                break;

            --syncMinimum;
        }
    }

    private void Synchronize(char syncChar) //Reads until the next available char is syncChar
    {
        while (Reader.Peek() != syncChar)
        {
            if (Reader.Read() is EOF)
                break;
        }
    }


    private void Tokenize()
    {
        char readResult = Reader.ReadSkip();

        if (readResult is EOF) //Empty source file
            return;

        switch (readResult)
        {
            case Comment: TokenizeComment(); return;

            case SquareOpen: TokenizeTable(); return;

            default:
                if (!IsKey(readResult))
                {
                    TempErrorLogger.Value.Add("no clue what this char is here for bro");
                    //some possible scenarios: if its eq then probably a missing key decl, sort it out later
                    //can switch on the violating char when the tokenizer and rules are well in place
                    Synchronize(LF);
                }
                TokenizeKeyValuePair();
                return;
        }

    }

    private void TokenizeComment()
    {
        char c;

        while ((c = Reader.ReadSkip()) is not EOF)
        {
            if (IsControl(c) && c is not Tab)
            {
                TempErrorLogger.Value.Add("Tab is the only control character valid inside a comment.");
                Synchronize(LF);
            }

            if (c is LF)
                break;
        }
    }

    private void TokenizeTable()
    {
        if (Reader.MatchNextSkip(SquareOpen))
        {
            TokenizeArrayTable();
            return;
        }

        else
            TokenizeKey();


        if (Reader.PeekSkip() is not SquareClose)
        {
            TempErrorLogger.Value.Add("Unterminated table declaration.");
            Synchronize(1);
        }
    }

    private void TokenizeArrayTable()
    {
        TokenizeKey();

        if (Reader.PeekSkip() != SquareClose || Reader.PeekNextSkip() != SquareClose)
        {
            TempErrorLogger.Value.Add("Unterminated arraytable declaration.");
            Synchronize(2);
        }
    }


    private void TokenizeKeyValuePair()
    {
        TokenizeKey();

        if (!Reader.MatchNextSkip(EqualsSign))
        {
            TempErrorLogger.Value.Add("Key was not followed by = character.");
        }

        TokenizeValue();

        if (Reader.PeekSkip() is not LF or Comment)
        {
            TempErrorLogger.Value.Add("Found trailing characters after keyvaluepair.");
            Synchronize(LF);
        }
    }


    private void TokenizeKey()
    {
        char c = Reader.ReadSkip();

        if (c is EOF)
        {
            TempErrorLogger.Value.Add("Expected key but the end of file was reached");
            return;
        }

        do
        {
            switch (c)
            {
                case DoubleQuote: TokenizeString(); break;
                case SingleQuote: TokenizeLiteralString(); break;
                default: TokenizeBareKey(); break;
            }

            //key building logic here OR add keys separately and handle them in the parser
        } while (Reader.PeekSkip() is Dot);
    }


    private void TokenizeArray()
    {
        do
        {
            TokenizeValue();

        } while (Reader.MatchNextSkip(Comma));

        if (!Reader.MatchNextSkip(SquareClose))
        {
            TempErrorLogger.Value.Add("Unterminated array");
            Synchronize(1); //dont currently have a better idea for a syncpoint, so skip one then see where it goes..
        }
    }


    private void TokenizeValue()
    {

    }

    private void TokenizeBareKey()
    {
        char c;
        
        //ref structs with proper Dispose() methods are pattern matched and handled without needing an explicit IDisposable declaration (ref structs cannot implement interfaces)
        using ValueStringBuilder vsb = new(stackalloc char[128]); 

        while ((c = Reader.Peek()) is not EOF)
        {
            if (IsBareKey(c))
                vsb.Append(c);

            else if (c is Space or Tab or EqualsSign)//key token finished
                return;

            else
            {
                TempErrorLogger.Value.Add($"Bare keys cannot contain the character '{c}'");
                Synchronize(LF); //if key failed, consider the entire line invalid
                return;
            }
        }

        if (true)
            Console.WriteLine("string val " + vsb.ToString());
    }


    private void TokenizeString()
    {

    }

    private void TokenizeMultiLineString()
    {

    }


    private void TokenizeLiteralString()
    {

    }

    private void TokenizeLiteralMultiLineString()
    {

    }

    private void TokenizeInteger()
    {

    }


    private void TokenizeFloat()
    {

    }


    private void TokenizeBool()
    {

    }

    private void TokenizeDateTime()
    {

    }


    private static bool IsBareKey(char c) => IsAsciiLetter(c) || IsAsciiDigit(c) || c is '-' or '_';
    private static bool IsKey(char c) => IsBareKey(c) || c is '"' or '\'';
    /*private static TTable ResolveKey(TTable root, in ReadOnlySpan<char> str)
    {
        TTable containingTable = root;
        int start = 0;

        if (!str.Contains('.'))
            ProcessFragment(in str, start, str.Length);

        else
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (i == str.Length - 1)
                    ProcessFragment(in str, start, i + 1);

                if (str[i] == '.')
                {
                    ProcessFragment(in str, start, i);
                    ++i;
                    start = i;
                }
            }
        }

        return containingTable;

        void ProcessFragment(in ReadOnlySpan<char> str, int start, int end)
        {
            if (containingTable.Values.ContainsKey(str[start..end].ToString()))
                containingTable = (TTable)containingTable[str[start..end]];
            else
            {
                TTable subtable = [];
                containingTable.Values.Add(str[start..end].ToString(), subtable);
                containingTable = subtable;
            }
        }}*/

}
enum TOMLTokenType
{
    Comment,
    Key,
    Table,
    Array,
    String,
    Integer,
    Float,
    Bool,
    DateTime,
    Undefined,
    Eof,
}

[Flags]
enum TOMLTokenMetadata
{
    QuotedKey = 1,
    Multiline = 2,
    Literal = 4,
    MultilineLiteral = Multiline | Literal,
    DateOnly = 8,
    TimeOnly = 16,
    DateTimeLocal = 32,
    DateTimeOffset = 64,
    Hex = 128,
    Octal = 256,
    Binary = 512,
}


readonly record struct TOMLToken : IEquatable<TOMLToken>
{
    internal TOMLTokenType TokenType { get; init; }

    internal (int Line, int Column) Position { get; init; }

    public TOMLToken(TOMLTokenType type, int ln, int col)
    {
        TokenType = type;
        Position = (ln, col);
    }
}



sealed class TOMLStreamReader : IDisposable
{
    internal StreamReader BaseReader { get; init; }

    internal int Line { get; private set; }

    internal int Column { get; private set; }

    public TOMLStreamReader(Stream source)
    {
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Streams without read or seeking support cannot be used.");

        BaseReader = new(source, Encoding.UTF8); //Encoding is UTF-8 by default.
        Line = 0;
        Column = 0;
    }

    private void SkipWhiteSpace()
    {
        while (BaseReader.Peek() is ' ' or '\t')
            BaseReader.Read();
    }


    /// <summary>
    /// Returns the next available character, consuming it. 
    /// </summary>
    /// <returns>The next available character, or <see langword="EOF"/>('\0') if no more characters are available to read.</returns>
    public char Read()
    {
        int readResult = BaseReader.Read();
        switch (readResult)
        {
            case -1: return '\0';

            case '\n':
                ++Line;
                Column = 0;
                return (char)readResult;

            default:
                ++Column;
                return (char)readResult;
        }
    }


    /// <summary>
    /// Returns the next available character, consuming it. Ignores tab or skip.
    /// </summary>
    /// <returns>The next available character, or <see langword="EOF"/>('\0') if no more characters are available to read.</returns>
    public char ReadSkip()
    {
        SkipWhiteSpace();
        return Read();
    }


    /// <summary>
    /// Returns the next available character, without consuming it.
    /// </summary>
    /// <returns><see langword="EOF"/> ('\0') if there are no characters to be read; otherwise, the next available character.</returns>
    public char Peek()
    {
        int peekResult = BaseReader.Peek();
        return peekResult is -1 ? '\0' : (char)peekResult;
    }


    /// <summary>
    /// Returns the next available character, without consuming it. Ignores tab or space.
    /// </summary>
    /// <returns><see langword="EOF"/> ('\0') if there are no characters to be read; otherwise, the next available character.</returns>
    public char PeekSkip()
    {
        SkipWhiteSpace();
        return Peek();
    }


    /// <summary>
    /// Returns the character after the next available character, without consuming it. Ignores tab or space.
    /// </summary>
    /// <returns>The character after the next available character, or <see langword="EOF"/> ('\0') if the end of the stream is reached first.</returns>
    public char PeekNext()
    {
        if (BaseReader.Peek() == -1)
            return '\0';

        BaseReader.Read();

        int peekResult = BaseReader.Peek();
        BaseReader.BaseStream.Seek(-2, SeekOrigin.Current);

        if (peekResult is -1)
            return '\0';

        return (char)peekResult;
    }


    /// <summary>
    /// Returns the character after the next available character, without consuming it. Ignores tab or space.
    /// </summary>
    /// <returns>The character after the next available character, or <see langword="EOF"/> ('\0') if the end of the stream is reached first.</returns>
    public char PeekNextSkip()
    {
        SkipWhiteSpace();
        return PeekNext();
    }


    /// <summary>
    /// Consumes the next character from the stream if it matches <paramref name="c"/>.
    /// </summary>
    /// <returns> <see langword="true"/> if <paramref name="c"/> matches the next character; otherwise <see langword="false"/>.</returns>
    public bool MatchNext(char c)
    {
        if (BaseReader.Peek() == c)
        {
            BaseReader.Read();

            if (c == '\n')
            {
                ++Line;
                Column = 0;
            }

            else
                ++Column;

            return true;
        }

        return false;
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


//.NET internal code
public ref struct ValueStringBuilder //MUST BE PASSED AS REF WHEN ARGUMENT!
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _appendedCharCount;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _appendedCharCount = 0;
    }

    public ValueStringBuilder(int initialCapacity)
    {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        _chars = _arrayToReturnToPool;
        _appendedCharCount = 0;
    }

    public int Length
    {
        readonly get => _appendedCharCount;
        set
        {
            Debug.Assert(value >= 0);
            Debug.Assert(value <= _chars.Length);
            _appendedCharCount = value;
        }
    }

    public readonly int Capacity => _chars.Length;

    public void EnsureCapacity(int capacity)
    {
        Debug.Assert(capacity >= 0); //Not expected to be called with a negative value

        if ((uint)capacity > (uint)_chars.Length)// If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
            Grow(capacity - _appendedCharCount);
    }


    /// <summary>
    /// Get a pinnable reference to the builder.
    /// Does not ensure there is a null char after <see cref="Length"/>
    /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
    /// the explicit method call, and write eg "fixed (char* c = builder)"
    /// </summary>
    public readonly ref char GetPinnableReference() => ref MemoryMarshal.GetReference(_chars);


    /// <summary>
    /// Get a pinnable reference to the builder.
    /// </summary>
    /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
    public ref char GetPinnableReference(bool terminate)
    {
        if (terminate)
        {
            EnsureCapacity(Length + 1);
            _chars[Length] = '\0';
        }

        return ref MemoryMarshal.GetReference(_chars);
    }

    public ref char this[int index]
    {
        get
        {
            Debug.Assert(index < _appendedCharCount);
            return ref _chars[index];
        }
    }

    public override string ToString()
    {
        string s = _chars.Slice(0, _appendedCharCount).ToString();
        Dispose();
        return s;
    }

    public readonly Span<char> RawChars => _chars;


    /// <summary>
    /// Returns a span around the contents of the builder.
    /// </summary>
    /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
    public ReadOnlySpan<char> AsSpan(bool terminate)
    {
        if (terminate)
        {
            EnsureCapacity(Length + 1);
            _chars[Length] = '\0';
        }

        return _chars[.._appendedCharCount];
    }

    public readonly ReadOnlySpan<char> AsSpan() => _chars[.._appendedCharCount];
    public readonly ReadOnlySpan<char> AsSpan(int start) => _chars[start.._appendedCharCount];
    public readonly ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);


    public bool TryCopyTo(Span<char> destination, out int charsWritten)
    {
        if (_chars[.._appendedCharCount].TryCopyTo(destination))
        {
            charsWritten = _appendedCharCount;
            Dispose();
            return true;
        }
        else
        {
            charsWritten = 0;
            Dispose();
            return false;
        }
    }


    public void Insert(int index, char value, int count)
    {
        if (_appendedCharCount > _chars.Length - count)
            Grow(count);

        int remaining = _appendedCharCount - index;

        _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
        _chars.Slice(index, count).Fill(value);

        _appendedCharCount += count;
    }


    public void Insert(int index, string? s)
    {
        if (s is null)
            return;

        int count = s.Length;

        if (_appendedCharCount > (_chars.Length - count))
            Grow(count);


        int remaining = _appendedCharCount - index;

        _chars.Slice(index, remaining).CopyTo(_chars[(index + count)..]);
        s.CopyTo(_chars[index..]);

        _appendedCharCount += count;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char c)
    {
        int pos = _appendedCharCount;

        if ((uint)pos < (uint)_chars.Length)
        {
            _chars[pos] = c;
            _appendedCharCount = pos + 1;
        }

        else
            GrowAndAppend(c);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(string? s)
    {
        if (s is null)
            return;

        int pos = _appendedCharCount;

        if (s.Length == 1 && (uint)pos < (uint)_chars.Length) // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
        {
            _chars[pos] = s[0];
            _appendedCharCount = pos + 1;
        }

        else
            AppendSlow(s);
    }


    private void AppendSlow(string s)
    {
        int pos = _appendedCharCount;

        if (pos > _chars.Length - s.Length)
            Grow(s.Length);

        s.CopyTo(_chars[pos..]);
        _appendedCharCount += s.Length;
    }


    public void Append(char c, int count)
    {
        if (_appendedCharCount > _chars.Length - count)
            Grow(count);


        Span<char> dst = _chars.Slice(_appendedCharCount, count);

        for (int i = 0; i < dst.Length; i++)
            dst[i] = c;

        _appendedCharCount += count;
    }


    public unsafe void Append(char* value, int length)
    {
        int pos = _appendedCharCount;

        if (pos > _chars.Length - length)
            Grow(length);

        Span<char> dst = _chars.Slice(_appendedCharCount, length);

        for (int i = 0; i < dst.Length; i++)
            dst[i] = *value++;

        _appendedCharCount += length;
    }


    public void Append(ReadOnlySpan<char> value)
    {
        int pos = _appendedCharCount;

        if (pos > _chars.Length - value.Length)
            Grow(value.Length);

        value.CopyTo(_chars[_appendedCharCount..]);
        _appendedCharCount += value.Length;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> AppendSpan(int length)
    {
        int origPos = _appendedCharCount;

        if (origPos > _chars.Length - length)
            Grow(length);

        _appendedCharCount = origPos + length;
        return _chars.Slice(origPos, length);
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char c)
    {
        Grow(1);
        Append(c);
    }


    /// <summary>
    /// Resize the internal buffer either by doubling current buffer size or
    /// by adding <paramref name="additionalCapacityBeyondPos"/> to
    /// <see cref="_pos"/> whichever is greater.
    /// </summary>
    /// <param name="additionalCapacityBeyondPos">
    /// Number of chars requested beyond current position.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacityBeyondPos)
    {
        Debug.Assert(additionalCapacityBeyondPos > 0);
        Debug.Assert(_appendedCharCount > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

        // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative
        char[] poolArray = ArrayPool<char>.Shared.Rent((int)Math.Max((uint)(_appendedCharCount + additionalCapacityBeyondPos), (uint)_chars.Length * 2));

        _chars[.._appendedCharCount].CopyTo(poolArray);

        char[]? toReturn = _arrayToReturnToPool;
        _chars = _arrayToReturnToPool = poolArray;

        if (toReturn != null)
            ArrayPool<char>.Shared.Return(toReturn);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        char[]? toReturn = _arrayToReturnToPool;
        this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
        if (toReturn != null)
            ArrayPool<char>.Shared.Return(toReturn);
    }
}
