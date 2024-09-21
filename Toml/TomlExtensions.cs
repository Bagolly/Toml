using Toml.Runtime;
using static Toml.Runtime.TObject;
using static Toml.Tokenization.Constants;

namespace Toml.Extensions;


public static class TomlExtensions
{   
    public static TArray AsArray(this TObject obj)
    {
        if (obj.Type is not (TOMLType.Array or TOMLType.ArrayTable))
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TArray)obj;
    }

    public static TTable AsTable(this TObject obj)
    {
        if (obj.Type is not (TOMLType.HeaderTable or TOMLType.KeyValTable or TOMLType.InlineTable))
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TTable)obj;
    }

    public static TArray? AsArrayOrDefault(this TObject obj) => obj.Type is not (TOMLType.Array or TOMLType.ArrayTable) ? default: (TArray)obj;
    
    public static TTable? AsTableOrDefault(this TObject obj) => obj.Type is not (TOMLType.HeaderTable or TOMLType.KeyValTable or TOMLType.InlineTable) ? default : (TTable)obj;



    /// <summary>
    /// If <paramref name="c"/> is an ASCII control character, returns it's 3 or 2 letter acronym.
    /// Otherwise, it returns the character representation of <paramref name="c"/>.
    /// <para>If <paramref name="c"/> is -1, the method returns EOF.</para>
    /// </summary>
    internal static string GetFriendlyNameFor(int c) => c switch
    {
        EOF  => "End of File",
        0x7F => "[DEL] (U+007F)",
        < 33 => $"[{ASCIIControlCharFriendlyName[c]}] (U+{c:X4})", //Only these have an acronym
        _    => $"{(char)c}",
    };
}
