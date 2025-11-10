using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Toml.Runtime;
using Toml.Tokenization;
using static Toml.Runtime.TObject;
using static Toml.Tokenization.Constants;

namespace Toml.Extensions;


public static class Extensions
{   
    public static TArray AsArray(this TObject obj)
    {
        if (obj.Type is not (TOMLType.Array or TOMLType.ArrayTable))
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TArray)obj;
    }

    public static IEnumerable<TArray> Arrays(this TObject obj)
    {
        if(obj is TArray array)
        {
            foreach(var element in array)
                if(element is TArray subarray)
                    yield return subarray;
        }

        else if(obj is TTable table)
        {
            foreach(var (_, element) in table)
                if(element is TArray subarray)
                    yield return subarray;
        }
    }

    public static IEnumerable<TTable> Tables(this TObject obj)
    {
        if (obj.Type is TOMLType.ArrayTable && obj is TArray array)
        {
            foreach (var element in array)
                if (element is TTable subtable)
                    yield return subtable;
        }

        else if (obj is TTable table)
        {
            foreach (var (_, element) in table)
                if (element is TTable subtable)
                    yield return subtable;
        }
    }

    public static IEnumerable<TObject> Values(this TObject obj)
    {
        if(obj is TTable table)
            foreach(var (_, value) in table)
                if(value.Type < TOMLType.InlineTable)
                    yield return value;
        
        if(obj is TArray array)
            foreach(var element in array)
                if(element.Type < TOMLType.InlineTable)
                    yield return element;
    }



    public static TTable AsTable(this TObject obj)
    {
        if (obj.Type is not (TOMLType.HeaderTable or TOMLType.KeyValTable or TOMLType.InlineTable))
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TTable)obj;
    }

    public static TArray? AsArrayOrDefault(this TObject obj) => obj.Type is not (TOMLType.Array or TOMLType.ArrayTable) ? default: (TArray)obj;
    
    public static TTable? AsTableOrDefault(this TObject obj) => obj.Type is not (TOMLType.HeaderTable or TOMLType.KeyValTable or TOMLType.InlineTable) ? default : (TTable)obj;

    public static T Extract<T>(this TObject obj) where T : notnull => ((TValue<T>)obj).Value;  

    public static T? ExtractOrDefault<T>(this TObject obj) where T : notnull
    {
        if(((TValue<T>)obj).Value is T val)
            return val;

        return default;
    }



    /// <summary>
    /// If <paramref name="c"/> is an ASCII control character, returns it's 3 or 2 letter acronym.
    /// Otherwise, it returns the character representation of <paramref name="c"/>.
    /// <para>If <paramref name="c"/> is -1, the method returns EOF.</para>
    /// </summary>
    internal static string GetFriendlyNameFor(int c) => c switch
    {
        EOF  => "[EOF]",
        0x7F => "[DEL] (U+007F)",
        < 33 => $"[{ASCIIControlCharFriendlyName[c]}] (U+{c:X4})", //Only these control characters have an acronym
        _    => $"{(char)c}",
    };
}
