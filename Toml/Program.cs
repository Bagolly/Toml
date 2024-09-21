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

namespace Toml;



internal class Program
{
    static void Main()
    {
        //C:\Users\BAGOLY\Desktop\Toml\TomlTesting\te
        Console.WriteLine(Directory.Exists("../../../../TomlTesting/tests/invalid"));


        _ = 1;

        Console.OutputEncoding = Encoding.UTF8;
        Stopwatch sw = new();


        /*
            current big task: implementing deserialization. first need to resolve the comment problem.
            some other things not covered by metadata: 
            - datetime separator 'T' or whitespace? currently not saved, should use 'T' by default, maybe make optional.
            - float exponents?                      currently not saved, so it is subject to .net formatting. Look into using 'r' for round trip.
            - casing for keys?                      should use existing names by default, but serializing a .net object? what are the rules for that?
            - multiline and/or literal strings? the metadata should help, but might be some issues for example leading new line, line ending backslash, etc.

            other big tasks:
            -streaming (SAX) parser interface
         */

        //  BuildBenchMark();

        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/big.toml",
                                  FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: 8192,
                                  FileOptions.SequentialScan);
        using TomlStreamSource source = new(fs);


        TOMLTokenizer t = new(source, comments: TomlCommentMode.Store);

        sw.Start();
        t.TokenizeFile();
        sw.Stop();

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


        if (t.ErrorLog.Count is not 0)
        {
            Console.WriteLine("Parsing could not start because of the following errors:");

            #region Error logging
            Console.ForegroundColor = ConsoleColor.Red;

            foreach (var msg in t.ErrorLog)
                Console.WriteLine(msg);

            Console.ResetColor();
            #endregion

            return;
        }


        var parser = new TOMLParser(t.TokenStream, t.Values);


        Console.WriteLine(t.TokenStream.Count);


        sw.Start();
        TTable root = parser.Parse();
        sw.Stop();


        /*
        TomlJsonMapper mapper = new();
        string result = mapper.ToJson(root);
        using StreamWriter swriter = new("C:/Users/BAGOLY/Desktop/output.json");
        swriter.WriteLine(result);
        */


        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms"); //about 250-280ms for gigatest2.toml
    }

    private static void BuildBenchMark()
    {
        StringBuilder sb = new(8192 - 1);
        Random r = new();

        for (int i = 0; i < 100_000; ++i)
        {
            switch (r.NextDouble())
            {
                case > 0.66:
                    sb.Append($"key{i} = 0x{Convert.ToString(r.Next(), 16)}\n");
                    break;
                case > 0.33:
                    sb.Append($"key{i} = 0b{Convert.ToString(r.Next(), 2)}\n");
                    break;
                default:
                    sb.Append($"key{i} = 0o{Convert.ToString(r.Next(), 8)}\n");
                    break;
            }
        }

        using StreamWriter sw = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/synth.txt");

        sw.Write(sb);
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


public sealed class TomlJsonMapper
{                                               //todo: handle escaped chars for valid json
    private static string SerializeValue<T>(TValue<T> val) where T : notnull
        => $$"""{"type": "{{val.Type}}", "value": "{{val.Value}}"}""";

    private static string PrintValue(TObject obj) => obj.Type switch
    {
        TObject.TOMLType.String => SerializeValue((TValue<string>)obj),
        TObject.TOMLType.Integer => SerializeValue((TValue<long>)obj),
        TObject.TOMLType.Float => SerializeValue((TValue<double>)obj),
        TObject.TOMLType.Boolean => SerializeValue((TValue<bool>)obj),
        TObject.TOMLType.DateTimeOffset => SerializeValue((TValue<DateTimeOffset>)obj),
        TObject.TOMLType.DateTimeLocal => SerializeValue((TValue<DateTime>)obj),
        TObject.TOMLType.DateOnly => SerializeValue((TValue<DateOnly>)obj),
        TObject.TOMLType.TimeOnly => SerializeValue((TValue<TimeOnly>)obj),
        _ => throw new ArgumentException($"Objects with type <{obj.Type}> cannot be serialized as a value.", nameof(obj)),
    };


    private int _indentLevel;

    private const int _indentChange = 2;


    private StringBuilder _builder;


    public TomlJsonMapper()
    {
        _indentLevel = 0;
        _builder = new(64);
    }


    public string ToJson(TTable root)
    {
        PrintTable(root);

        return _builder.ToString();
    }


    private void PrintTable(TTable table)
    {
        if (table.Values.Count == 0) //short circuit on empty tables
        {
            _builder.Append("{ }");
            return;
        }

        _builder.Append("{\n");
        _indentLevel += _indentChange;


        foreach (var (key, val) in table)
        {
            PrintKeyValuePair(key, val);
        }

        _builder.Remove(_builder.Length - 2, 2); //Remove trailing comma and linefeed on last key/value pair.
        _builder.Append('\n');
        _indentLevel -= _indentChange;

        Append('}');
    }

    private void PrintArray(TArray array)
    {
        if (array.Values.Count == 0) //short circuit on empty arrays
        {
            _builder.Append("[ ]");
            return;
        }

        _builder.Append('[');
        _indentLevel += _indentChange;

        foreach (var element in array)
        {
            _builder.Append($"\n{GetIndent()}");
            PrintObject(element);
            _builder.Append(',');
        }

        _builder.Remove(_builder.Length - 1, 1); //Remove trailing comma on last element.
        _indentLevel -= _indentChange;

        _builder.Append($"\n{GetIndent()}]");
    }


    private void PrintKeyValuePair(string key, TObject value) //Print table elements
    {
        Append($"\"{key}\": ");

        PrintObject(value);

        _builder.Append(",\n");
    }

    private void PrintObject(TObject value) //Prints any TObject
    {
        if (value is TTable subtable)
            PrintTable(subtable);


        else if (value is TArray array)
            PrintArray(array);

        else
            _builder.Append(PrintValue(value));
    }


    //Returns a string padding for the current indent level.
    private string GetIndent() => new string(' ', _indentLevel);


    private void Append(char c)
    {
        _builder.Append(GetIndent());
        _builder.Append(c);
    }


    private void Append(string s)
    {
        _builder.Append(GetIndent());
        _builder.Append(s);
    }
}
