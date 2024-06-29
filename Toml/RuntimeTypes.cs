using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Toml.Tokenization;
using static Toml.Tokenization.Constants;
using System.Globalization;
using Microsoft.Diagnostics.Runtime.Utilities;
using BenchmarkDotNet.Columns;
using Microsoft.Diagnostics.Tracing.Parsers.AspNet;


namespace Toml.Runtime;
#pragma warning disable 0809


public abstract class TObject
{
    public enum TOMLType
    {
        String, Integer, Float, Boolean,
        DateTimeOffset, DateTimeLocal, TimeOnly, DateOnly,
        Array, InlineTable,
        KeyValTable, HeaderTable, ArrayTable, Key
    }

    public virtual TOMLType Type { get; protected init; }

    public abstract TObject this[string index] { get; set; }

    public abstract TObject this[int index] { get; set; }
}



public abstract class TValue<T> : TObject, ITomlSerializeable
{
    public virtual T? Value { get; set; }

    public TOMLTokenMetadata Metadata { get; init; }

    public override string ToString() => SerializeValue();

    public override TObject this[string index] { get => throw _exception; set => throw _exception; }

    public override TObject this[int index] { get => throw _exception; set => throw _exception; }

    private static readonly InvalidOperationException _exception = new("Cannot apply indexing to a TOML value type.");

    #region Serialization 
    public abstract string SerializeValue(); //TODO: remove these from production code, especially type. Its only used for generating JSON for testing.
    public abstract string SerializeType(); //same with this, dependency chain should be reversed, maybe try a visitor or similar. LATER.
    #endregion
}


public sealed class TArray : TObject, IEnumerable<TObject>, ITomlCollection
{
    public override TObject this[string index] { get => throw new InvalidOperationException("Arrays cannot be indexed as tables."); set => throw new InvalidOperationException("Arrays cannot be indexed as tables."); }

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

    public override TObject this[string index]
    {
        get => Values[index];
        set => AddAssert(index, value);
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
    public TInteger(long value, TOMLTokenMetadata radix)
    {
        Type = TOMLType.Integer;
        Value = value;
        Metadata = radix;
    }

    public TInteger(in ReadOnlySpan<char> str, TOMLTokenMetadata radix)
    {
        Type = TOMLType.Integer;
    
        _radix = radix switch
        {
            TOMLTokenMetadata.Hex => 16,
            TOMLTokenMetadata.Binary => 2,
            TOMLTokenMetadata.Octal => 8,
            TOMLTokenMetadata.Decimal => 10,
            _ => throw new TomlInternalException("Invalid radix passed to TInteger constructor"),
        };


        Metadata = radix;
        Value = ToInteger(in str, _radix);
    }


    public TInteger(in ReadOnlySpan<char> str, TOMLTokenMetadata radix, bool isNegative = true) : this(isNegative ? str[1..] : str, radix)
    {
        if (isNegative)
            Value = -Value;
    }


    internal static long ToInteger(ref readonly ReadOnlySpan<char> str, byte radix)
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

    private byte _radix;

    public static implicit operator TInteger(long val) => new(val, TOMLTokenMetadata.Decimal);
    public static implicit operator long(TInteger val) => val.Value;
    public static implicit operator long?(TInteger? val) => val?.Value;

    public static bool operator ==(TInteger lhs, TInteger rhs) => lhs.Value == rhs.Value && lhs.Type == rhs.Type;
    public static bool operator !=(TInteger lhs, TInteger rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TInteger integer && this == integer;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(long l) => Value == l;



    public override string SerializeValue() => Value.ToString();

    public override string SerializeType() => "integer";
}


public sealed class TString : TValue<string>, IEquatable<string?>
{
    public TString(in ReadOnlySpan<char> str, TOMLTokenMetadata metadata)
    {
        Type = TOMLType.String;
        Value = str.ToString(); //Uses a special (and speedier) string constructor internally when type variable is char
        Metadata = metadata;
    }

    public TString(in string str, TOMLTokenMetadata metadata)
    {
        Type = TOMLType.String;
        Value = str;
        Metadata = metadata;
    }


#pragma warning disable CS8765
    public override string Value { get => base.Value!; set => base.Value = value; }
#pragma warning restore CS8765


    public static implicit operator TString(string str) => new(str, TOMLTokenMetadata.Basic);
    public static implicit operator TString(ReadOnlySpan<char> str) => new(in str, TOMLTokenMetadata.Basic);
    public static implicit operator string?(TString? str) => str?.Value;

    public static bool operator ==(TString lhs, TString rhs) => lhs.Value == rhs.Value && lhs?.Type == rhs.Type;
    public static bool operator !=(TString lhs, TString rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TString other && this == other;
    public override int GetHashCode() => HashCode.Combine(Value, Type);
    public bool Equals(string? other) => other == Value;


    public override string SerializeValue() => Value;

    public override string SerializeType() => "string";
}


internal sealed class TFragment : TValue<string>, IEquatable<string>
{
    public TFragment(string str, bool isDotted, TOMLTokenMetadata metadata = TOMLTokenMetadata.None)
    {
        Value = str;
        Type = TOMLType.Key;
        Metadata = metadata;
        IsDotted = isDotted;
    }


    public TFragment(in ReadOnlySpan<char> str, bool isDotted, TOMLTokenMetadata metadata = TOMLTokenMetadata.None)
    {
        Value = new(str);
        Type = TOMLType.Key;
        Metadata = metadata;
        IsDotted = isDotted;
    }

    public bool IsDotted { get; internal set; }

    /* Justification 
     * Safe to suppress nullability check on the getter, the derived type (TKey) enforces the initialization
     * of Value in the constructor. The setter is init-only, only the constructor will assign to Value.
     * Since the constructor takes a non-nullable argument, and the string constructor used to assign to Value
     * cannot return String?, Value is always definitely assigned, despite the base definition TValue<T>.Value being nullable (T?).
     * However, the compiler (rightfully) cannot verify code relying on 'proper usage' to remain correct, hence the warning.
     * (with 'proper usage' referring to the fact that the constructor definitely assigns Value).
     * The main reason for this is to avoid having to null-forgive every 'unchecked' access to Value when using TKey in the parser.
     */
#pragma warning disable CS8765
    public override string Value { get => base.Value!; set => base.Value = value; }
#pragma warning restore CS8765

    public override bool Equals(object? obj) => obj is TFragment other && this == other;
    public override int GetHashCode() => HashCode.Combine(Value, Type); //This may cause uniqueness issues if Value is null?
    public bool Equals(string? other) => other == Value;

    [Obsolete("This method always throws; only values can be serialized directly.")]
    public override string SerializeValue() => Value;

    [Obsolete("This method always throws; only values can be serialized directly.")]
    public override string SerializeType() => throw new InvalidOperationException("Internal type TFragment cannot be serialized directly.");
}



public sealed class TBool : TValue<bool>, IEquatable<bool>
{
    public TBool(bool value)
    {
        Type = TOMLType.Boolean;
        Value = value;
    }

    public override bool Equals(object? obj) => obj is TBool other && this == other;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(bool other) => other == Value;

    public static bool operator ==(TBool lhs, TBool rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TBool lhs, TBool rhs) => !(lhs == rhs);

    public static bool operator true(TBool b) => b.Value;
    public static bool operator false(TBool b) => b.Value;


    public override string SerializeValue() => Value ? "true" : "false";

    public override string SerializeType() => "bool";
}


public sealed class TFloat : TValue<double>, IEquatable<double>
{
    public TFloat(double value, TOMLTokenMetadata metadata = TOMLTokenMetadata.None)
    {
        Type = TOMLType.Float;
        Value = value;
    }


    public TFloat(in ReadOnlySpan<char> str, TOMLTokenMetadata metadata = TOMLTokenMetadata.None)
    {
        if (!double.TryParse(str, CultureInfo.InvariantCulture, out var result))
            throw new TomlRuntimeException($"Invalid float value '{str.ToString()}'");

#if CHECKED
        if(result is double.PositiveInfinity or double.NegativeInfinity)
        {
            throw new OverflowException("Value was outside the range of System.Double.");
        }
#endif
        Type = TOMLType.Float;
        Value = result;
    }

    public override bool Equals(object? obj) => obj is TFloat other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(double other) => other == Value;

    public static implicit operator TFloat(double val) => new(val);
    public static implicit operator double?(TFloat? val) => val?.Value;
    public static implicit operator double(TFloat val) => val.Value;

    public static bool operator ==(TFloat a, TFloat b) => a.Type == b.Type && a.Value == b.Value;
    public static bool operator !=(TFloat a, TFloat b) => !(a == b);

    public override string SerializeValue() => Value.ToString();

    public override string SerializeType() => "float";
}


public sealed class TDateTimeOffset : TValue<DateTimeOffset>, IEquatable<DateTimeOffset>
{
    public TDateTimeOffset(DateTimeOffset value, TOMLTokenMetadata metadata = TOMLTokenMetadata.None)
    {
        Value = value;
        Type = TOMLType.DateTimeOffset;
        Metadata = metadata;
    }

    public override bool Equals(object? obj) => obj is TDateTimeOffset other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(DateTimeOffset other) => Value == other;

    public bool HasUnkownOffset { get; init; }

    public static bool operator ==(TDateTimeOffset lhs, TDateTimeOffset rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value && lhs.HasUnkownOffset == rhs.HasUnkownOffset;
    public static bool operator !=(TDateTimeOffset lhs, TDateTimeOffset rhs) => !(lhs == rhs);


    public override string SerializeValue()
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

    public override string SerializeType() => "datetime";
}


public sealed class TDateTime : TValue<DateTime>, IEquatable<DateTime>
{
    public TDateTime(DateTime value)
    {
        Value = value;
        Type = TOMLType.DateTimeLocal;
    }

    public override bool Equals(object? obj) => obj is TDateTime other && this == other;

    public bool Equals(DateTime other) => Value == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public static bool operator ==(TDateTime lhs, TDateTime rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TDateTime lhs, TDateTime rhs) => !(lhs == rhs);


    public override string SerializeValue() => Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff");

    public override string SerializeType() => "datetime-local";
}


public sealed class TTimeOnly : TValue<TimeOnly>, IEquatable<TimeOnly>
{
    public TTimeOnly(TimeOnly value)
    {
        Value = value;
        Type = TOMLType.TimeOnly;
    }


    public bool Equals(TimeOnly other) => Value == other;

    public override bool Equals(object? obj) => obj is TTimeOnly other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public static bool operator ==(TTimeOnly lhs, TTimeOnly rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value;
    public static bool operator !=(TTimeOnly lhs, TTimeOnly rhs) => !(lhs == rhs);


    public override string SerializeValue() => Value.ToString("HH:mm:ss.fff");

    public override string SerializeType() => "time-local";
}


public sealed class TDateOnly : TValue<DateOnly>, IEquatable<DateOnly>
{
    public TDateOnly(DateOnly value)
    {
        Value = value;
        Type = TOMLType.DateOnly;
    }

    public bool Equals(DateOnly other) => Value == other;

    public override bool Equals(object? obj) => obj is TDateOnly d && d.Value == Value && d.Type == Type;

    public override int GetHashCode() => HashCode.Combine(Type, Value);


    public override string SerializeValue() => Value.ToString("yyyy-MM-dd");


    public override string SerializeType() => "date-local";
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
/// Represents unexpected exceptions that indicate an internal error in the library.
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


//Technically this violates interface segregation, but makes actual usage more convenient by enabling arbitrarily nested indexing.
//The other choice would be to make an 'AsArray()' and 'AsTable()' method, which would essentially reinterpret the TObject
//pointer to the required collection type or throw on failure.
//This would add a lot of what is basically boilerplate code to queries, so for now it's like this.
interface ITomlCollection
{
    public void Add(string key, TObject val);
    public void Add(TObject val);
    public abstract TObject this[string index] { get; set; }
    public abstract TObject this[int index] { get; set; }
}


interface ITomlSerializeable
{
    public string SerializeValue();

    public string SerializeType();
}