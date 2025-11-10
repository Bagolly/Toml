using System.Diagnostics;
using System.IO;
using System.Text;
using Toml.Tokenization;
using Toml.Parser;
using Toml.Extensions;
using Toml.Runtime;
using BenchmarkDotNet.Toolchains.Roslyn;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CommandLine;
using System.Collections;
using Toml.Reader;
using System;

namespace Toml;



internal class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Stopwatch sw = new();


        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/test-realistic-small.txt",
                                  FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: 8192,
                                  FileOptions.SequentialScan);

        // using TomlStreamSource source = new(fs);

        TomlStringSource source = new("key = -9223372036854775808");

        var config = new TomlConfig(TomlCommentMode.Store, Diagnostics.ErrorReportPolicy.Throw, Diagnostics.ErrorSeverity.Error);

        TOMLTokenizer t = new(source, config);

        sw.Start();
        _ = t.TokenizeFile();
        sw.Stop();

        /*
        if (t.Comments?.Count > 0)
        {
            if (t.Comments[0].IsTopLevel)
            {
                Console.WriteLine("Found top-level comments: ");

                for (int i = 0; i < t.Comments.Count && t.Comments[i].IsTopLevel; i++)
                    Console.WriteLine(t.Comments[i]);
            }
            else
            {
                Console.WriteLine("No top-level comments were found.");
            }

            Console.WriteLine("=====");

            foreach (var v in t.Values)
            {
                Console.WriteLine($"Type: {v.Type} | Value: {v} ");
                if (v.CommentSameLine is not -1)
                    Console.WriteLine("\tAlso has attached same-line comment: " + t.Comments![v.CommentSameLine]);
                if (v.CommentBelow is not -1)
                    Console.WriteLine("\tAlso has attached below-line comment: " + t.Comments![v.CommentBelow]);
            }
        }

        else
            Console.WriteLine("No comments were found.");
        */

        if (t.Logger.HasErrors)
        {
            Console.WriteLine("Parsing could not start because of the following errors:");

            #region Error logging
            Console.ForegroundColor = ConsoleColor.Red;

            foreach (var msg in t.Logger.Errors)
                Console.WriteLine(msg);
        
            Console.ResetColor();
            #endregion

            return;
        }


        var parser = new TOMLParser(t);


        Console.WriteLine(t.TokenStream.Count);


        sw.Start();
        TTable root = parser.Parse();
        sw.Stop();


        /* optional convert to json
        TomlJsonMapper mapper = new();
        string result = mapper.ToJson(root);
        using StreamWriter swriter = new("C:/Users/BAGOLY/Desktop/output.json");
        swriter.WriteLine(result);
        */


        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms"); //about 250-280ms for gigatest2.toml

    }
}


//This type maps TOML structures directly to C#-equivalent ones, and vica versa. (Only used for testing)
public static class TomlDirectMapper
{
    //TOML to C#
    public static Dictionary<string, object> MapDocument(TTable root) => MapTable(root);

    //C# to TOML
    public static TTable MapDocument(Dictionary<string, object> root) => MapTable(root);


    //TOML to C#
    private static Dictionary<string, object> MapTable(TTable table)
    {
        Dictionary<string, object> result = new(table.Values.Count);

        foreach (var (key, val) in table.Values)
            result.Add(key, MapObject(val));

        return result;
    }

    //C# to TOML
    private static TTable MapTable(IDictionary<string, object> root)
    {
        TTable result = new(TObject.TOMLType.HeaderTable);

        foreach (var (key, val) in root)
            result.Add(key, MapObject(val));

        return result;
    }

    //TOML to C#
    private static List<object> MapArray(TArray array)
    {
        List<object> result = new(array.Values.Count);

        foreach (var element in array.Values)
            result.Add(MapObject(element));

        return result;
    }

    //C# to TOML
    private static TArray MapArray(IList array)
    {
        TArray result = new(array.Count);

        foreach (var element in array)
            result.Add(MapObject(element));

        return result;
    }

    //TOML to C#
    private static object MapObject(TObject obj) => obj switch
    {
        TTable table => MapTable(table),
        TArray array => MapArray(array),
        _ => MapValue(obj),
    };

    //C# to TOML
    private static TObject MapObject(object obj) => obj switch
    {
        IDictionary<string, object> table => MapTable(table),
        IList array => MapArray(array),
        _ => MapValue(obj),
    };

    //TOML to C#
    private static object MapValue(TObject obj) => obj switch
    {
        TString s => s.Value,
        TBool b => b.Value,
        TInteger i => i.Value,
        TFloat f => f.Value,
        TDateOnly d => d.Value,
        TTimeOnly t => t.Value,
        TDateTime dt => dt.Value,
        TDateTimeOffset dto => dto.Value,
        TArray array => MapArray(array),
        TTable table when table.Type is TObject.TOMLType.InlineTable => MapTable((TTable)obj),
        _ => throw new ArgumentException("Not a TOML value type: " + obj.Type),
    };

    //C# to TOML
    private static TObject MapValue(object obj) => obj switch
    {
        string s => new TString(s, TomlTokenMetadata.Basic),
        char c => new TString([c], TomlTokenMetadata.Basic),
        bool b => new TBool(b),
        long or uint or int or short or ushort or byte or sbyte => new TInteger(Convert.ToInt64(obj), TomlTokenMetadata.Decimal),
        float or double => new TFloat(Convert.ToDouble(obj)),
        DateOnly d => new TDateOnly(d),
        TimeOnly t => new TTimeOnly(t),
        DateTime dt => new TDateTime(dt),
        DateTimeOffset dto => new TDateTimeOffset(dto),
        IList a => MapArray(a),
        IDictionary<string, object> inlineTable => MapTable(inlineTable),
        _ => throw new ArgumentException("Cannot map to TOML value type: " + obj.GetType()),
    };
}

