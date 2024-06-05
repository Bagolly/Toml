using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Toml.Runtime.TObject;
using Toml.Runtime;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Toml.Extensions;

public static class Extensions
{
    public static TArray AsArray(this TObject obj)
    {
        if (obj.Type is not (TOMLType.Array or TOMLType.ArrayTable))
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TArray)obj;
    }

    public static TTable AsTable(this TObject obj)
    {
        if (obj.Type is not TOMLType.Table)
            throw new InvalidCastException($"The object was not an array, but '{obj.Type}'.");

        return (TTable)obj;
    }

    public static TArray? AsArrayOrDefault(this TObject obj) => obj.Type is not (TOMLType.Array or TOMLType.ArrayTable) ? default: (TArray)obj;
    
    public static TTable? AsTableOrDefault(this TObject obj) => obj.Type is not TOMLType.Table ? default : (TTable)obj;
}
