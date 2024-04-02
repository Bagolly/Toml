using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Toml.TOMLExceptionHandler;

namespace TomlOld;

public abstract class TOMLObject
{
    public enum TOMLType
    {
        String, Integer, Float, Boolean,
        DateTimeOffset, LocalDateTime, LocalDate, LocalTime,
        Array, InlineTable, Table
    }

    public virtual TOMLType Type { get; init; }

    public abstract TOMLObject this[string index] { get; set; }

    public abstract TOMLObject this[int index] { get; set; }
}


public abstract class TOMLValue<T> : TOMLObject
{
    public abstract T? Value { get; protected init; }

    public override string ToString() => $"{(Value is null ? "null" : Value)}";

    //Disable indexing for value-like types (while still allowing abritrary indexing without casting).
    public override TOMLObject this[string index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); }

    public override TOMLObject this[int index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); }
}


public sealed class TOMLArray : TOMLObject, IEnumerable<TOMLObject>
{
    public override TOMLObject this[string index] { get => throw new InvalidOperationException("Arrays cannot be indexed as tables."); set => throw new InvalidOperationException("Arrays cannot be indexed as tables."); }

    public override TOMLObject this[int index]
    {
        get => Values[index];
        set => Values[index] = value;
    }

    public List<TOMLObject> Values { get; init; }

    public override TOMLType Type { get; init; }

    public IEnumerator<TOMLObject> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();

    public static TOMLArray With(TOMLObject value) => [value];

    public static TOMLArray WithMany(params TOMLObject[] values)
    {
        TOMLArray t = [];
        foreach (var obj in values)
            t.Values.Add(obj);

        return t;
    }

    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TOMLArray(int capacity = 0)
    {
        Values = new(capacity);
        Type = TOMLType.Array;
    }

    public void Add(TOMLObject value) => Values.Add(value);

    public override string ToString() => $"TOMLArray with {Values.Count} elements.";
}


public sealed class TOMLTable : TOMLObject, IEnumerable<KeyValuePair<string, TOMLObject>>
{
    public override TOMLObject this[int index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.TableIndexedAsArray]); set => throw new InvalidOperationException(MessageFor[ErrorCode.TableIndexedAsArray]); }

    public override TOMLObject this[string index]
    {
        get => Values[index];
        set => AddAssert(index, value);
    }

    public Dictionary<string, TOMLObject> Values { get; init; }

    private void AddAssert(in ReadOnlySpan<char> key, TOMLObject value)
    {
        if (Type is TOMLType.InlineTable)
            throw new InvalidOperationException(MessageFor[ErrorCode.InlineNoExtend]);

        Values.Add(key.ToString(), value);
    }


    public void Add(in ReadOnlySpan<char> key, TOMLObject value) => AddAssert(in key, value);


    public static TOMLTable With(in string key, TOMLObject value) => new() { [key] = value };

    public static TOMLTable WithMany(params (string, TOMLObject)[] values)
    {
        TOMLTable t = [];
        foreach (var (key, val) in values)
            t.Values.Add(key, val);
        return t;
    }


    public override TOMLType Type { get; init; }


    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TOMLTable()//needs fixing, this makes  no sense
    {
        Values = [];
        Type = TOMLType.Table;
    }

    public override string ToString() => $"TOMLTable with {Values.Count} entries.";

    public IEnumerator<KeyValuePair<string, TOMLObject>> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}


public sealed class TOMLInteger : TOMLValue<long>, IEquatable<TOMLInteger>
{
    public override TOMLType Type { get; init; }

    public override long Value { get; protected init; }


    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TOMLInteger(long value)
    {
        Type = TOMLType.Integer;
        Value = value;
    }

    public static implicit operator TOMLInteger(long val) => new(val);
    public static implicit operator long?(TOMLInteger? val) => val?.Value;

    public override bool Equals(object? obj) => obj is TOMLInteger integer && Type == integer.Type && Value == integer.Value;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(TOMLInteger? other) => other?.Value == Value;
}

public sealed class TOMLString : TOMLValue<string>, IEquatable<TOMLString>
{
    public override string? Value { get; protected init; }
    public override TOMLType Type { get; init; }

    public TOMLString(in ReadOnlySpan<char> str)
    {
        Type = TOMLType.String;
        Value = str.ToString();
    }

    public TOMLString(in string str)
    {
        Type = TOMLType.String;
        Value = str;
    }

    public static implicit operator TOMLString(string str) => new(str);
    public static implicit operator TOMLString(ReadOnlySpan<char> str) => new(str);
    public static implicit operator string?(TOMLString? str) => str?.Value;


    public override bool Equals(object? obj) => obj is TOMLString str && Value == str.Value && Type == str.Type;
    public override int GetHashCode() => HashCode.Combine(Value, Type);
    public bool Equals(TOMLString? other) => other?.Value == Value;
}

public sealed class TOMLBool : TOMLValue<bool>, IEquatable<TOMLBool>
{
    public override bool Value { get; protected init; }

    public override TOMLType Type { get; init; }


    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TOMLBool(bool value)
    {
        Type = TOMLType.Boolean;
        Value = value;
    }


    public override bool Equals(object? obj) => obj is TOMLBool other && Type == other.Type && Value == other.Value;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(TOMLBool? other) => other?.Value == Value;
}

public sealed class TOMLFloat : TOMLValue<double>, IEquatable<TOMLFloat>
{
    public override double Value { get; protected init; }

    public override TOMLType Type { get; init; }


    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TOMLFloat(double value)
    {
        Type = TOMLType.Boolean;
        Value = value;
    }


    public override bool Equals(object? obj) => obj is TOMLFloat f && Type == f.Type && Value == f.Value;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(TOMLFloat? other) => other?.Value == Value;


    public static implicit operator TOMLFloat(double val) => new(val);

    public static implicit operator double(TOMLFloat val) => val.Value;
}




sealed class TOMLTokenizer
{
    //Allows pattern-matching within class
    const char Tab = '\u0009';
    const char Space = '\u0020';
    const char NewLine = '\u000A';
    const char CR = '\u000D';
    const char Comment = '\u0023';
    const char DoubleQuote = '"';
    const char SingleQuote = '\'';
    const char UnderScore = '_';
    const char Dash = '-';
    const char EqualsSign = '=';
    const char Dot = '.';
    const char Table = '[';
    const string Empty = "";


    private TOMLTable DocumentRoot { get; init; }

    public TOMLTokenizer() => DocumentRoot = [];



    [Flags]
    public enum Tokenizer : byte
    {
        NoCommentValidation = 1 << 0,
        None = 1 << 7,
    }

    public void Process(in ReadOnlySpan<char> line, Tokenizer options = Tokenizer.None)
    {
        int seeker = 0;

        SkipWhiteSpace(ref seeker, in line);


        switch (line[seeker])
        {
            case NewLine: return;

            case Comment:
                if (options.HasFlag(Tokenizer.NoCommentValidation))
                    ProcessComment(ref seeker, in line);
                return;

            case Table:
                throw new NotImplementedException("No table handling right now ");

            default:
                if (!line[seeker].IsKey())
                    throw new FormatException(MessageFor[ErrorCode.InvalidTopLevelChar]);
                ProcessKeyValuePair(ref seeker, in line);
                return;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SkipWhiteSpace(ref int i, in ReadOnlySpan<char> line)
    {
        if (line is Empty)
            return;
        while (line[i] is Tab or Space) ++i;
    }


    private void ProcessKeyValuePair(ref int i, in ReadOnlySpan<char> line)
    {
        //check for duplicate keys (non-dot) or table names (dotted)! (right now dictionary does it already)
        Key(ref i, in line);

        SkipWhiteSpace(ref i, in line);


        if (line[i] is EqualsSign)
        {
            //parse value for key, throw on eol, ignore wp
            i++;

        }


        else if (line[i] is Dot)
        {
            //parse dotted key, must make arrays for it first
        }


        else
            throw new FormatException(MessageFor[ErrorCode.KeyInvalidSyntax]);
    }


    private static void ResolveKeyValuePair(ref int i, in ReadOnlySpan<char> line)
    {


    }

    private static void AssertNextIs(in ReadOnlySpan<char> line, ref int i, char c, string msgOnError)
    {
        SkipWhiteSpace(ref i, in line);

        if (i >= line.Length || line[i] != c)
            throw new Exception(msgOnError);
    }


    private void Key(ref int i, in ReadOnlySpan<char> line)//key statement entry point
    {
        TOMLTable nested; //this way the referenece is kept to the last added table, sparing an O(n) lookup when adding the value

        (int start, int end) key = (KeyFragment(ref i, in line), i); //parses a key "fragment". Example: the key a.b.c has 3 fragments. The key "d" has 1.


        SkipWhiteSpace(ref i, in line); //keyfragment will stop on the first whitespace; they are skipped -here- instead.

        while (line[i] is Dot)
        {
            nested = [];                    /*need to resolve stupid readonly inlines later*/
            DocumentRoot.Add(line[key.start..key.end], nested);
            Console.WriteLine($"Added table named {line[key.start..key.end]}. Next token is: {line[i]}");
            ++i; //skip dot
            key = (KeyFragment(ref i, in line), i); //get next key.
        }

        if (line[i] is EqualsSign)
        {
            ++i; //skip equals sign
                 //ParseValue(ref i, in line, nested, start);
            Console.WriteLine("EQ found, context is now Value");
        }


        //end of key parsing, next token (whitespace not whitstanding) should be an '='


        Console.ReadKey();

    }



    private static int KeyFragment(ref int i, in ReadOnlySpan<char> line) //subparts of dotted, quoted
    {
        int tokenStartPosition = i;

        //when control is passed to this method, the current character will decide
        //the type of the key: quoted literal, quoted basic or bare

        if (line[i] is DoubleQuote or SingleQuote) //Quoted key
            throw new NotImplementedException("Found quoted key; no handler!");


        while (true) //Bare key
        {
            if (i == line.Length - 1) //happens if only key is present on line
                throw new FormatException(MessageFor[ErrorCode.KeyUndefined]);

            if (line[i].IsBareKey())
                ++i;

            else if (line[i] is Tab or Space or EqualsSign or Dot) //Valid token terminators. Return, but don't consume; part of a key statement
                return tokenStartPosition;

            else
                throw new FormatException(MessageFor[ErrorCode.BareKeyInvalid]);

        }
    }


    private static void ProcessComment(ref int i, in ReadOnlySpan<char> line)
    {
        /* Because comments in TOML apply to the rest of the line,
         * comments are always terminating; there are no more tokens to process on the line.
         * This method only exists to validate that only non-tab control characters are used in comments.
         */
        while (line[i++] is not NewLine)
        {
            if (line[i] is CR && AssertCRLF(line[i..]))     //Ensure it's a CRLF line ending and not a stray carriage return
            {
                i += 2;                                     //Skip both CR and LF, then return
                return;
            }

            if (line[i] is NewLine)
            {
                ++i;                                        //For documents with normal(ized) line-endings. SKip only LF, then return
                return;
            }

            else if (line[i] is not Tab && char.IsControl(line[i]))
                throw new FormatException(MessageFor[ErrorCode.CommentInvalidControlChar]);

            i++;
        }
    }

    private static bool AssertCRLF(in ReadOnlySpan<char> line)
    {
        if (line.Length < 2)
            throw new FormatException(MessageFor[ErrorCode.CommentCRInvalid]);
        else
            return line[0] is CR && line[1] is NewLine;
    }
}

class TOMLExceptionHandler //Temporarily used to centralize handling of error strings
{
    //TODO: try move to assembly manifest
    public static readonly Dictionary<ErrorCode, string> MessageFor = new()
    {
        [ErrorCode.CommentInvalidControlChar] = "Tab (U+0020) is the only allowed control character in comments.",
        [ErrorCode.CommentCRInvalid] = "Invalid CRLF line ending or stray CR character in comment.",
        [ErrorCode.BareKeyInvalid] = "Invalid character in key.",
        [ErrorCode.KeyInvalid] = "An invalid character was found in a key token.",
        [ErrorCode.KeyUndefined] = "A key was declared, but no value was defined.",
        [ErrorCode.KeyInvalidSyntax] = "An invalid character was found after key declaration.",
        [ErrorCode.InvalidTopLevelChar] = "Only key/value pairs, comments or table declarations can be at the root of a document.",
        [ErrorCode.ArrayIndexedAsTable] = "Attempted to index an array as a table.",
        [ErrorCode.TableIndexedAsArray] = "Attempted to index a table as an array",
        [ErrorCode.ValueTypeIndexed] = "This type is not indexable.",
        [ErrorCode.InlineNoExtend] = "Inline tables cannot be extended.",
    };

    public enum ErrorCode
    {
        CommentInvalidControlChar,
        CommentCRInvalid,
        BareKeyInvalid,
        KeyInvalid,
        KeyUndefined,
        KeyInvalidSyntax,
        InvalidTopLevelChar,
        ArrayIndexedAsTable,
        TableIndexedAsArray,
        ValueTypeIndexed,
        InlineNoExtend,
    }
}


static class TOMLExtensions
{
    /// <summary> Whether the current character marks the start of a key. Currently, the range is [A-Z][a-z][0-9]-_ or the chartacters " and '. </summary>
    /// <returns><see langword="true"/> if it is; otherwise <see langword="false"/></returns>
    public static bool IsKey(this char c) => IsBareKey(c) || c is '"' or '\'';

    public static bool IsBareKey(this char c) => char.IsAsciiDigit(c) || char.IsAsciiLetter(c) || c is '-' or '_';
}
}
