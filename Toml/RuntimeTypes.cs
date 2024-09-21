using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Toml.Tokenization;
using static Toml.Tokenization.Constants;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Toml.Writer;


namespace Toml.Runtime;


public abstract class TObject
{
    public enum TOMLType
    {
        String, Integer, Float, Boolean,
        DateTimeOffset, DateTimeLocal, TimeOnly, DateOnly,
        Array, InlineTable,
        KeyValTable, HeaderTable, ArrayTable, Key, Comment
    }

    //Why is there a Type field when the classes themselves are already 'unique'?
    //Eg. "isn't it redundant to store "Type = TOMLType.String" for all TString instances?"
    //No, not really. This way a simple field access on any TObject can tell what type
    //it can safely be cast down to without using the "is", "as" or explicit cast operators.
    //The contract for the Type field to always match its instance is internal, and yes,
    //reflection could completely ruin that at any time.
    //But that's like saying it is pointless to reinforce a bridge against the elements
    //because a well-placed bomb could destroy it anyway, so not too worried about that.
    public virtual TOMLType Type { get; protected init; }

    public abstract TObject this[string index] { get; set; }

    public abstract TObject this[int index] { get; set; }


    //These properties cannot be made read- or init-only, so it's not safe to expose them as public.
    internal int CommentSameLine { get; set; } = -1;

    internal int CommentBelow { get; set; } = -1;
}



public abstract class TValue<T> : TObject where T : notnull
{
    public T Value { get; set; }

    public TomlTokenMetadata Metadata { get; init; }

    public TValue(in T value) => Value = value;

    public override string? ToString() => Value.ToString();

    public override TObject this[string index] { get => throw _exception; set => throw _exception; }

    public override TObject this[int index] { get => throw _exception; set => throw _exception; }

    private static readonly InvalidOperationException _exception = new("Cannot apply indexing to a TOML value type.");
}


public sealed class TArray : TObject, IEnumerable<TObject>, ITomlCollection
{
    public override TObject this[string index] 
    { 
        get => throw new InvalidOperationException("Arrays cannot be indexed as tables."); 
        set => throw new InvalidOperationException("Arrays cannot be indexed as tables."); 
    }


    public override TObject this[int index]
    {
        get => Values[index];

        set
        {
            if (Type is TOMLType.ArrayTable && value.Type is not TOMLType.HeaderTable)
                throw new InvalidOperationException($"Cannot add a value of type {value.Type}; arraytables can only hold tables.");

            Values[index] = value;
        }
    }

    public TObject this[Index i]
    {
        get => Values[i];
        set
        {
            if (Type is TOMLType.ArrayTable && value.Type is not TOMLType.HeaderTable)
                throw new InvalidOperationException($"Cannot add a value of type {value.Type}; arraytables can only hold tables.");


            Values[i] = value;
        }
    }

    public List<TObject> this[Range r] => Values[r];

    public List<TObject> Values { get; init; }

    public IEnumerator<TObject> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();

    public static TArray With(TObject value) => [value];

    public static TArray WithMany(params TObject[] values) => new() { Values = [.. values] };

    public static TArray FromList(List<TObject> list) => new() { Values = list };

    public TArray(int capacity = 0)
    {
        Values = new(capacity);
        Type = TOMLType.Array;
    }


    public TArray(params TTable[] from)
    {
        Values = [.. from];
        Type = TOMLType.ArrayTable;
    }

    public TArray(TTable with)
    {
        Values = [with];
        Type = TOMLType.ArrayTable;
    }


    public void Add(TObject value)
    {
        if (Type is TOMLType.ArrayTable && value.Type is not TOMLType.HeaderTable)
            throw new InvalidOperationException($"Cannot add a value of type {value.Type}; arraytables can only hold tables.");

        Values.Add(value);
    }


    public void Add(string key, TObject? val) => throw new NotImplementedException("Can't add to array like a table");

    public override string ToString() => $"TOMLArray ({Values.Count} elements)";
}



public sealed class TTable : TObject, IEnumerable<KeyValuePair<string, TObject>>, ITomlCollection
{
    public override TObject this[int index] { get => throw new InvalidOperationException("Cannot index a table as an array."); set => throw new InvalidOperationException("Cannot index a table as an array."); }

    public override TObject this[string key]
    {
        get => Values[key];
        set => AddAssert(key, value);
    }

    public TObject this[in ReadOnlySpan<char> index]
    {
        get => Values[index.ToString()];
        set => AddAssert(index, value);
    }

    public Dictionary<string, TObject> Values { get; init; }


    internal TomlTableState State { get; set; }


    public bool IsInline => Type is TOMLType.InlineTable;

    private void AddAssert(in ReadOnlySpan<char> key, TObject value)
    {
        if (Type is TOMLType.InlineTable && State is TomlTableState.Closed)
            throw new TomlRuntimeException("Inline tables cannot be extended.");


        if (State is TomlTableState.Closed && value.Type is not (TOMLType.HeaderTable or TOMLType.ArrayTable))
            throw new TomlRuntimeException($"The table '{key.ToString()}' was already defined once, and can therefore only be accept new subtables, not {value.Type}s.");

        if (!Values.TryAdd(key.ToString(), value))
            throw new TomlRuntimeException($"Cannot add the key {key.ToString()}, because it already exists.");
    }


    private void AddAssert(string key, TObject value) => AddAssert(key.AsSpan(), value);


    public void Add(in ReadOnlySpan<char> key, TObject value) => AddAssert(in key, value);

    public void Add(string key, TObject value) => AddAssert(key.AsSpan(), value);

    internal void BuildKey(in ReadOnlySpan<char> key, TTable table) => Values.Add(key.ToString(), table);

    public static TTable With(in string key, TObject value) => new(TOMLType.HeaderTable) { [key] = value };

    public static TTable WithMany(params (string, TObject)[] values)
    {
        TTable t = new(TOMLType.HeaderTable);
        foreach (var (key, val) in values)
            t.Values.Add(key, val);
        return t;
    }

    public static TTable FromDictionary(Dictionary<string, TObject> dictionary) => new(TOMLType.HeaderTable) { Values = dictionary };

    void ITomlCollection.Add(string key, TObject val) => Values.Add(key, val);

    void ITomlCollection.Add(TObject val) => throw new TomlRuntimeException("Can't add to table like an array!");


    public TTable(TOMLType type)
    {
        Debug.Assert(type is TOMLType.HeaderTable or TOMLType.KeyValTable or TOMLType.InlineTable, $"Table ctor usage error, invalid type {type} provided.");
        Values = [];
        Type = type;
        State = TomlTableState.Open;
    }


    internal void CloseTable()
    {
        Debug.Assert(Type is TOMLType.HeaderTable or TOMLType.KeyValTable, "Usage error, this method must only be called on tables.");
        State = TomlTableState.Closed;
    }


    internal void CloseInlineTable()
    {
        Debug.Assert(Type is TOMLType.InlineTable, "Usage error, this method must only be called on inline tables.");
        State = TomlTableState.Closed;
    }


    public override string ToString() => $"TOMLTable ({Values.Count} entries)";

    public IEnumerator<KeyValuePair<string, TObject>> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}



public sealed class TInteger : TValue<long>, IEquatable<long>
{
    public TInteger(long value, TomlTokenMetadata radix) : base(value)
    {
        Type = TOMLType.Integer;
        Metadata = radix;
    }

    public TInteger(in ReadOnlySpan<char> str, TomlTokenMetadata radix) : base(ToInteger(str, (byte)radix))
    {
        Type = TOMLType.Integer;
        Metadata = radix;
    }


    public TInteger(in ReadOnlySpan<char> str, TomlTokenMetadata radix, bool isNegative = true) : this(isNegative ? str[1..] : str, radix)
    {
        if (isNegative)
            Value = -Value;
    }


    internal static long ToInteger(ReadOnlySpan<char> str, byte radix)
    {
        long val = 0;
        int i = 0;
#if CHECKED
checked{
#endif
        while (i < str.Length)
            val = val * radix + (str[i++] - AsciiNumOffset);  //TOML numbers use ASCII digits only.
#if CHECKED
}
#endif

        return val;
    }

    public static implicit operator TInteger(long val) => new(val, TomlTokenMetadata.Decimal);
    public static implicit operator long(TInteger val) => val.Value;
    public static implicit operator long?(TInteger? val) => val?.Value;

    public static bool operator ==(TInteger lhs, TInteger rhs) => lhs.Value == rhs.Value && lhs.Type == rhs.Type;
    public static bool operator !=(TInteger lhs, TInteger rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TInteger integer && this == integer;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(long l) => Value == l;
}


public abstract class TStringBase : TValue<string>, IEquatable<string?>
{
    public TStringBase(ReadOnlySpan<char> str) : base(new(str)) { }

    public TStringBase(string str) : base(str) { }

    public static bool operator ==(TStringBase lhs, TStringBase rhs) => lhs.Value == rhs.Value && lhs?.Type == rhs.Type;

    public static bool operator !=(TStringBase lhs, TStringBase rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TStringBase other && this == other;

    public override int GetHashCode() => HashCode.Combine(Value, Type);

    public bool Equals(string? other) => other == Value;
}



public sealed class TString : TStringBase
{
    public TString(in ReadOnlySpan<char> str, TomlTokenMetadata metadata) : base(str)
    {
        Debug.Assert(metadata is >= TomlTokenMetadata.Basic and <= TomlTokenMetadata.MultilineLiteral);
        Type = TOMLType.String;
        Metadata = metadata;
    }

    public TString(in string str, TomlTokenMetadata metadata) : base(str)
    {
        Debug.Assert(metadata is >= TomlTokenMetadata.Basic and <= TomlTokenMetadata.MultilineLiteral);
        Type = TOMLType.String;
        Metadata = metadata;
    }
}



public sealed class TComment : TStringBase
{
    public TComment(in ReadOnlySpan<char> str, int commentIndex, bool isTopLevel = false) : base(str)
    {
        Type = TOMLType.Comment;
        CommentSameLine = commentIndex;
        IsTopLevel = isTopLevel;
    }

    public TComment(in string str, int commentIndex, bool isTopLevel = false) : base(str)
    {
        Type = TOMLType.Comment;
        CommentSameLine = commentIndex;
        IsTopLevel = isTopLevel;
    }

    public bool IsTopLevel { get; init; }
}



public sealed class TKey : TStringBase
{
    public TKey(string str, bool isDotted, TomlTokenMetadata metadata = TomlTokenMetadata.None) : base(str)
    {
        Type = TOMLType.Key;
        Metadata = metadata;
        IsDotted = isDotted;
    }


    public TKey(in ReadOnlySpan<char> str, bool isDotted, TomlTokenMetadata metadata = TomlTokenMetadata.None) : base(str)
    {
        Type = TOMLType.Key;
        Metadata = metadata;
        IsDotted = isDotted;
    }

    public bool IsDotted { get; internal set; }  //This property is set after the key is constructed.
}



public sealed class TBool : TValue<bool>, IEquatable<bool>
{
    public TBool(bool value) : base(value) => Type = TOMLType.Boolean;


    public override bool Equals(object? obj) => obj is TBool other && this == other;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(bool other) => other == Value;

    public static bool operator ==(TBool lhs, TBool rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TBool lhs, TBool rhs) => !(lhs == rhs);

    public static bool operator true(TBool b) => b.Value;
    public static bool operator false(TBool b) => b.Value;
}



public sealed class TFloat : TValue<double>, IEquatable<double>
{
    public TFloat(double value, TomlTokenMetadata metadata = TomlTokenMetadata.None) : base(value)
    {
        Type = TOMLType.Float;
        Metadata = metadata;
    }


    public TFloat(in ReadOnlySpan<char> str, TomlTokenMetadata metadata = TomlTokenMetadata.None)
        : this(ToDouble(str), metadata) { }

    private static double ToDouble(ReadOnlySpan<char> str)
    {
        if (!double.TryParse(str, CultureInfo.InvariantCulture, out var result))
            throw new TomlRuntimeException($"Invalid float value '{str.ToString()}'");

        return result;
    }

    public override bool Equals(object? obj) => obj is TFloat other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(double other) => other == Value;

    public static implicit operator TFloat(double val) => new(val);
    public static implicit operator double?(TFloat? val) => val?.Value;
    public static implicit operator double(TFloat val) => val.Value;

    public static bool operator ==(TFloat a, TFloat b) => a.Type == b.Type && a.Value == b.Value;
    public static bool operator !=(TFloat a, TFloat b) => !(a == b);
}



public sealed class TDateTimeOffset : TValue<DateTimeOffset>, IEquatable<DateTimeOffset>
{
    public TDateTimeOffset(in DateTimeOffset value, TomlTokenMetadata metadata = TomlTokenMetadata.None) : base(in value)
    {
        Type = TOMLType.DateTimeOffset;
        Metadata = metadata;
    }

    public override bool Equals(object? obj) => obj is TDateTimeOffset other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(DateTimeOffset other) => Value == other;

    public bool IsUnkownTimeOffset { get; init; }

    public static bool operator ==(TDateTimeOffset lhs, TDateTimeOffset rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value && lhs.IsUnkownTimeOffset == rhs.IsUnkownTimeOffset;
    public static bool operator !=(TDateTimeOffset lhs, TDateTimeOffset rhs) => !(lhs == rhs);


    public string ToRfc3339String()
    {
        string datetime = Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        if (Value.Millisecond != 0)
            datetime += $".{Value.Millisecond}";

        datetime += Value.Offset.Hours switch
        {
            > 0 => $"+{Value.Offset}",
            < 0 => Value.Offset,
            _ => 'Z',
        };

        return datetime;
    }
}



public sealed class TDateTime : TValue<DateTime>, IEquatable<DateTime>
{
    public TDateTime(in DateTime value, TomlTokenMetadata metadata = TomlTokenMetadata.None) : base(in value)
    {
        Type = TOMLType.DateTimeLocal;
        Metadata = metadata;
    }

    public override bool Equals(object? obj) => obj is TDateTime other && this == other;

    public bool Equals(DateTime other) => Value == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public static bool operator ==(TDateTime lhs, TDateTime rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TDateTime lhs, TDateTime rhs) => !(lhs == rhs);

    public string ToRfc3339String() => Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff");
}



public sealed class TTimeOnly : TValue<TimeOnly>, IEquatable<TimeOnly>
{
    public TTimeOnly(in TimeOnly value) : base(in value) => Type = TOMLType.TimeOnly;
    

    public bool Equals(TimeOnly other) => Value == other;

    public override bool Equals(object? obj) => obj is TTimeOnly other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public static bool operator ==(TTimeOnly lhs, TTimeOnly rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TTimeOnly lhs, TTimeOnly rhs) => !(lhs == rhs);

    public string ToRfc3339String() => Value.ToString("HH:mm:ss.fff");
}



public sealed class TDateOnly : TValue<DateOnly>, IEquatable<DateOnly>
{
    public TDateOnly(in DateOnly value) : base(in value) => Type = TOMLType.DateOnly;

    public bool Equals(DateOnly other) => Value == other;

    public override bool Equals(object? obj) => obj is TDateOnly d && d.Value == Value && d.Type == Type;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public string ToRfc3339String() => Value.ToString("yyyy-MM-dd");
}



/// <summary>
/// Represents exceptions that occur during parsing or construction of TOML data types.
/// </summary>
public class TomlRuntimeException : ApplicationException
{
    public TomlRuntimeException(string msg) : base(msg) => Value = null;

    public TomlRuntimeException() : base() => Value = null;



    /// <param name="cause">The object that caused the exception.</param>
    public TomlRuntimeException(string msg, TObject? cause) : base(msg) => Value = cause;


    /// <summary>
    /// The violating object that caused the exception.
    /// </summary>
    public TObject? Value { get; init; }
}



/// <summary>
/// Represents exceptions that occur in the reader feeding input to the tokenizer.
/// </summary>
public class TomlReaderException : ApplicationException
{
    public TomlReaderException() : base()
    {
        Line = -1;
        Column = -1;
    }

    public TomlReaderException(string msg, int line, int column) : base(msg)
    {
        Line = line;
        Column = column;
    }

    /// <summary>
    /// The line where the error was encountered.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// The column where the error was encountered.
    /// </summary>
    /// <remarks>
    /// <b>Note:</b> 
    /// sometimes this value can be off by a few characters.
    /// Positional information is meant to be used in tandem with the error message itself to effectively diagnose errors.
    /// </remarks>
    public int Column { get; init; }

    public override string Message => $"{base.Message} Occured at line {Line}, column {Column}";
}



/// <summary>
/// Represents unexpected exceptions that indicate an internal error.
/// </summary>
public class TomlInternalException : ApplicationException //AKA the good old "you fucked up" exception.
{
    public TomlInternalException(string msg) : base($"[INTERNAL]: {msg}") { }

    public TomlInternalException() : base() { }
}



enum TomlTableState
{
    /// <summary>
    /// <para><b>Tables</b>: accepts key/value pairs or subtables, and its existing subtables may be extended as well.</para>
    /// <para><b>Inline tables</b>: accepts key/value pairs.</para>
    /// </summary>
    Open,

    /// <summary>
    /// <para><b>Tables</b>: only new subtables, that don't already exist, can be added. The table's subtables cannot be extended.</para>
    /// <para><b>Inline tables</b>: cannot be extended in any way.</para>
    /// </summary>
    Closed
}


// The problem this solves is grouping both array and table collections into a supertype so that compile-time type
//resolution cannot complain about arbtirary and/or mixed indexing on TObject.
// This violates interface segregation (and type safety) by requiring arrays to support key-based indexing, and
//index-based access for arrays, but makes usage more convenient by enabling arbitrarily nested indexing.
// The other choice would be to make an 'AsArray()' and 'AsTable()' method, which would downcast the TObject pointer
//to the required collection type or throw on failure.
// This would add a lot of what is basically boilerplate code to queries. The idea is that when a user loads a 
//configuration file he should already know its layout.
// If you don't have document layout information, you can't write a class to represent the document's runtime model
//so I currently find it safe to assume that unkown config files aren't just loaded into programs
//or at least not the main use case.
//Tables and arrays can already safely be walked with a foreach, and the Type field makes type information
//available even when upcast to TObject. For instances generated by the tokenizer, it guarantees that the
//type field holds the correct type, meaning downcasting based on the type field will not fail.
interface ITomlCollection
{
    public void Add(string key, TObject val);
    public void Add(TObject val);
    public abstract TObject this[string index] { get; set; }
    public abstract TObject this[int index] { get; set; }
}