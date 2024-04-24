using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Toml.Runtime.TOMLExceptionHandler;

namespace Toml.Runtime;

#pragma warning disable IDE0290
public abstract class TObject
{
    public enum TOMLType
    {
        String, Integer, Float, Boolean,
        DateTimeOffset, LocalDateTime, LocalDate, LocalTime,
        Array, InlineTable, Table
    }

    public virtual TOMLType Type { get; protected init; }

    public abstract TObject this[string index] { get; set; }

    public abstract TObject this[int index] { get; set; }
}


public abstract class TValue<T> : TObject
{
    public abstract T? Value { get; protected init; }

    public override string ToString() => $"{(Value is null ? "null" : Value)}";

    public override TObject this[string index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); }

    public override TObject this[int index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[ErrorCode.ValueTypeIndexed]); }
}


public sealed class TArray : TObject, IEnumerable<TObject>, ITCollection
{
    public override TObject this[string index] { get => throw new InvalidOperationException("Arrays cannot be indexed as tables."); set => throw new InvalidOperationException("Arrays cannot be indexed as tables."); }

    public override TObject this[int index]
    {
        get => Values[index];
        set => Values[index] = value;
    }

    public List<TObject> Values { get; init; }

    public override TOMLType Type { get; protected init; }

    public IEnumerator<TObject> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();

    public static TArray With(TObject value) => [value];

    public static TArray WithMany(params TObject[] values) => new() { Values = [.. values] };

    public static TArray FromList(List<TObject> list) => new() { Values = list };

    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TArray(int capacity = 0)
    {
        Values = new(capacity);
        Type = TOMLType.Array;
    }

    public void Add(TObject value) => Values.Add(value);

    public void Add(string key, TObject? val) => throw new NotImplementedException("Can't add to array like a table");


    public override string ToString() => $"TOMLArray with {Values.Count} elements.";
}


public sealed class TTable : TObject, IEnumerable<KeyValuePair<string, TObject>>, ITCollection
{
    public override TObject this[int index] { get => throw new InvalidOperationException(MessageFor[ErrorCode.TableIndexedAsArray]); set => throw new InvalidOperationException(MessageFor[ErrorCode.TableIndexedAsArray]); }

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


    private void AddAssert(in ReadOnlySpan<char> key, TObject value)
    {
        if (Type is TOMLType.InlineTable)
            throw new InvalidOperationException(MessageFor[ErrorCode.InlineNoExtend]);

        Values.Add(key.ToString(), value);
    }


    public void Add(in ReadOnlySpan<char> key, TObject value) => AddAssert(in key, value);


    public static TTable With(in string key, TObject value) => new() { [key] = value };

    public static TTable WithMany(params (string, TObject)[] values)
    {
        TTable t = [];
        foreach (var (key, val) in values)
            t.Values.Add(key, val);
        return t;
    }

    public static TTable FromDictionary(Dictionary<string, TObject> dictionary) => new() { Values = dictionary };
    
    void ITCollection.Add(string key, TObject val) => Values.Add(key, val);

    void ITCollection.Add(TObject val) => throw new InvalidOperationException("Can't add to table like an array!");

    public override TOMLType Type { get; protected init; }

    public TTable(bool isInline = false)
    {
        Values = [];
        Type = isInline ? TOMLType.InlineTable : TOMLType.Table;
    }

    public override string ToString() => $"TOMLTable with {Values.Count} entries.";

    public IEnumerator<KeyValuePair<string, TObject>> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}


public sealed class TInteger : TValue<long>, IEquatable<TInteger>
{
    public override TOMLType Type { get; protected init; }

    public override long Value { get; protected init; }


    public TInteger(long value)
    {
        Type = TOMLType.Integer;
        Value = value;
    }


    public static implicit operator TInteger(long val) => new(val);
    public static implicit operator long(TInteger val) => val.Value;
    public static implicit operator long?(TInteger? val) => val?.Value;

    public override bool Equals(object? obj) => obj is TInteger integer && Type == integer.Type && Value == integer.Value;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(TInteger? other) => other?.Value == Value;
}

public sealed class TString : TValue<string>, IEquatable<TString>
{
    public override string? Value { get; protected init; }
    public override TOMLType Type { get; protected init; }

    public TString(in ReadOnlySpan<char> str)
    {
        Type = TOMLType.String;
        Value = str.ToString();
    }

    public TString(in string str)
    {
        Type = TOMLType.String;
        Value = str;
    }

    public static implicit operator TString(string str) => new(str);
    public static implicit operator TString(ReadOnlySpan<char> str) => new(str);
    public static implicit operator string?(TString? str) => str?.Value;


    public override bool Equals(object? obj) => obj is TString str && Value == str.Value && Type == str.Type;
    public override int GetHashCode() => HashCode.Combine(Value, Type);
    public bool Equals(TString? other) => other?.Value == Value;
}

public sealed class TBool : TValue<bool>, IEquatable<TBool>
{
    public override bool Value { get; protected init; }

    public override TOMLType Type { get; protected init; }


    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Fuck off")]
    public TBool(bool value)
    {
        Type = TOMLType.Boolean;
        Value = value;
    }


    public override bool Equals(object? obj) => obj is TBool other && Type == other.Type && Value == other.Value;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(TBool? other) => other?.Value == Value;
}

public sealed class TFloat(double value) : TValue<double>, IEquatable<TFloat>
{
    public override double Value { get; protected init; } = value;

    public override TOMLType Type { get; protected init; } = TOMLType.Boolean;

    public override bool Equals(object? obj) => obj is TFloat f && Type == f.Type && Value == f.Value;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(TFloat? other) => other?.Value == Value;

    public static implicit operator TFloat(double val) => new(val);
    public static implicit operator double?(TFloat? val) => val?.Value;
    public static implicit operator double(TFloat val) => val.Value;
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

interface ITCollection
{
    public void Add(string key, TObject val);
    public void Add(TObject val);

    public abstract TObject this[string index] { get; set; }

    public abstract TObject this[int index] { get; set; }
}