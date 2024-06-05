﻿using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Toml.Tokenization;
using static Toml.Runtime.TOMLExceptionHandler;
#pragma warning disable IDE0290


namespace Toml.Runtime;


public abstract class TObject
{
    public enum TOMLType
    {
        String, Integer, Float, Boolean,
        DateTimeOffset, DateTimeLocal, TimeOnly, DateOnly,
        Array, InlineTable, Table, ArrayTable, Key
    }

    public virtual TOMLType Type { get; protected init; }

    public abstract TObject this[string index] { get; set; }

    public abstract TObject this[int index] { get; set; }
}


public abstract class TValue<T> : TObject
{
    public virtual T? Value { get; protected init; }

    public override string ToString() => $"{(Value is null ? "null" : Value)}";

    public override TObject this[string index] { get => throw new InvalidOperationException(MessageFor[RuntimeError.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[RuntimeError.ValueTypeIndexed]); }

    public override TObject this[int index] { get => throw new InvalidOperationException(MessageFor[RuntimeError.ValueTypeIndexed]); set => throw new InvalidOperationException(MessageFor[RuntimeError.ValueTypeIndexed]); }
}


public sealed class TArray : TObject, IEnumerable<TObject>, ITCollection
{
    public override TObject this[string index] { get => throw new InvalidOperationException("Arrays cannot be indexed as tables."); set => throw new InvalidOperationException("Arrays cannot be indexed as tables."); }

    public override TObject this[int index]
    {
        get => Values[index];
        set
        {
            if (Type == TOMLType.ArrayTable && value.Type != TOMLType.Table) //Most common case.
                throw new InvalidOperationException($"Cannot add a value of type {value.Type}; arraytables can only hold tables.");

            Values[index] = value;
        }
    }

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
        Values = [..from];
        Type = TOMLType.ArrayTable;
    }


    public TArray(TTable from)
    {
        Values = [from];
        Type = TOMLType.ArrayTable;
    }


    public void Add(TObject value)
    {   
        if (Type == TOMLType.ArrayTable && value.Type != TOMLType.Table)
            throw new InvalidOperationException($"Cannot add an element of type {value.Type}; arraytables may only contain tables.");

        Values.Add(value);
    }
    public void Add(string key, TObject? val) => throw new NotImplementedException("Can't add to array like a table");

    public TObject GetLast() => Values[Values.Count - 1]; //Used in parser; a reference to an arraytable is a reference to its last defined table.

    public override string ToString() => $"TOMLArray with {Values.Count} elements.";
}


public sealed class TTable : TObject, IEnumerable<KeyValuePair<string, TObject>>, ITCollection
{
    public override TObject this[int index] { get => throw new InvalidOperationException(MessageFor[RuntimeError.TableIndexedAsArray]); set => throw new InvalidOperationException(MessageFor[RuntimeError.TableIndexedAsArray]); }

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
            throw new InvalidOperationException(MessageFor[RuntimeError.InlineNoExtend]);

        Values.Add(key.ToString(), value);
    }

    private void AddAssert(string key, TObject value)
    {
        if (Type is TOMLType.InlineTable)
            throw new InvalidOperationException(MessageFor[RuntimeError.InlineNoExtend]);

        Values.Add(key.ToString(), value);
    }


    public void Add(in ReadOnlySpan<char> key, TObject value) => AddAssert(in key, value);

    public void Add(string key, TObject value) => AddAssert(key, value);

    internal void BuildKey(in ReadOnlySpan<char> key, TTable table) => Values.Add(key.ToString(), table);

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

    public TTable(bool isInline = false)
    {
        Values = [];
        Type = isInline ? TOMLType.InlineTable : TOMLType.Table;
    }

    public override string ToString() => $"TOMLTable with {Values.Count} entries.";

    public IEnumerator<KeyValuePair<string, TObject>> GetEnumerator() => Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}


public sealed class TInteger : TValue<long>, IEquatable<long>
{
    public TInteger(long value)
    {
        Type = TOMLType.Integer;
        Value = value;
    }

    public TInteger(in ReadOnlySpan<char> str, TOMLTokenMetadata? radix = null)
    {
        Type = TOMLType.Integer;

        Value = ToInteger(in str, radix switch
        {
            TOMLTokenMetadata.Hex => 16,
            TOMLTokenMetadata.Binary => 2,
            TOMLTokenMetadata.Octal => 8,
            _ => 10,
        });
    }

    internal static long ToInteger(ref readonly ReadOnlySpan<char> str, byte radix) //base is taken lol
    {
        long val = 0;
        int i = 0;

        while (i < str.Length)
            val = val * radix + (str[i++] - 0x30);  //TOML uses ASCII digits only.

        return val;
    }

    public static implicit operator TInteger(long val) => new(val);
    public static implicit operator long(TInteger val) => val.Value;
    public static implicit operator long?(TInteger? val) => val?.Value;

    public static bool operator ==(TInteger lhs, TInteger rhs) => lhs.Value == rhs.Value && lhs.Type == rhs.Type;
    public static bool operator !=(TInteger lhs, TInteger rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TInteger integer && this == integer;
    public override int GetHashCode() => HashCode.Combine(Type, Value);
    public bool Equals(long l) => Value == l;
}


public sealed class TString : TValue<string>, IEquatable<string?>
{
    public TString(in ReadOnlySpan<char> str)
    {
        Type = TOMLType.String;
        Value = str.ToString(); //Uses a special (and speedier) string constructor internally when type variable is char
    }

    public TString(in string str)
    {
        Type = TOMLType.String;
        Value = str;
    }

    public static implicit operator TString(string str) => new(str);
    public static implicit operator TString(ReadOnlySpan<char> str) => new(in str);
    public static implicit operator string?(TString? str) => str?.Value;

    public static bool operator ==(TString lhs, TString rhs) => lhs.Value == rhs.Value && lhs?.Type == rhs.Type;
    public static bool operator !=(TString lhs, TString rhs) => !(lhs == rhs);

    public override bool Equals(object? obj) => obj is TString other && this == other;
    public override int GetHashCode() => HashCode.Combine(Value, Type);
    public bool Equals(string? other) => other == Value;
}


internal sealed class TFragment : TValue<string>, IEquatable<string>
{
    public TFragment(in ReadOnlySpan<char> str, int length)
    {
        Value = new(str[..str.Length]);
        Type = TOMLType.Key;
        Length = length; //Array's length will be different (unless string length is exactly a power of 2)
    }

    public int Length { get; init; }


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
    public override string Value { get => base.Value!; protected init => base.Value = value; }
#pragma warning restore CS8765

    public override bool Equals(object? obj) => obj is TFragment other && this == other;
    public override int GetHashCode() => HashCode.Combine(Value, Type); //This may cause uniqueness issues if Value is null?
    public bool Equals(string? other) => other == Value;
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
}


public sealed class TFloat : TValue<double>, IEquatable<double>
{
    public TFloat(double value)
    {
        Type = TOMLType.Float;
        Value = value;
    }

    public TFloat(in ReadOnlySpan<char> str) : this(double.Parse(str)) { }

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
    public TDateTimeOffset(DateTimeOffset value)
    {
        Value = value;
        Type = TOMLType.DateTimeOffset;
    }

    public override bool Equals(object? obj) => obj is TDateTimeOffset other && this == other;

    public override int GetHashCode() => HashCode.Combine(Type, Value);

    public bool Equals(DateTimeOffset other) => Value == other;

    public bool IsUnknownLocal { get; init; }

    public static bool operator ==(TDateTimeOffset lhs, TDateTimeOffset rhs) => lhs.Type == rhs.Type && lhs.Value == rhs.Value && lhs.IsUnknownLocal == rhs.IsUnknownLocal;
    public static bool operator !=(TDateTimeOffset lhs, TDateTimeOffset rhs) => !(lhs == rhs);
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
}



class TOMLExceptionHandler
{
    //TODO: see about moving to assembly manifest
    public static readonly Dictionary<RuntimeError, string> MessageFor = new()
    {
        [RuntimeError.CommentInvalidControlChar] = "Tab (U+0020) is the only allowed control character in comments.",
        [RuntimeError.CommentCRInvalid] = "Invalid CRLF line ending or stray carriage return in comment.",
        [RuntimeError.BareKeyInvalid] = "Invalid character in key.",
        [RuntimeError.KeyInvalid] = "An invalid character was found in a key token.",
        [RuntimeError.KeyUndefined] = "A key was declared, but no value was defined.",
        [RuntimeError.KeyInvalidSyntax] = "An invalid character was found after key declaration.",
        [RuntimeError.InvalidTopLevelChar] = "Only key/value pairs, comments or table declarations can be at the root of a document.",
        [RuntimeError.ArrayIndexedAsTable] = "Attempted to index an array as a table.",
        [RuntimeError.TableIndexedAsArray] = "Attempted to index a table as an array",
        [RuntimeError.ValueTypeIndexed] = "This type is not indexable.",
        [RuntimeError.InlineNoExtend] = "Inline tables cannot be extended.",
    };

    public enum RuntimeError
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

//Technically this violates interface segregation, but makes actual usage more convenient by enabling arbitrarily nested indexing.
//The other choice would be to make an 'AsArray()' and 'AsTable()' method, which would essentially reinterpret the TObject
//pointer to the required collection type or throw on failure.
//This would add a lot of what is basically boilerplate code to queries, so for now it's like this.
interface ITCollection
{
    public void Add(string key, TObject val);
    public void Add(TObject val);
    public abstract TObject this[string index] { get; set; }
    public abstract TObject this[int index] { get; set; }
}


