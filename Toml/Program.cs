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

namespace Toml;



internal class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Stopwatch sw = new();

    
        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/gigatest2.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        TOMLTokenizer t = new(fs);
        
        sw.Start();
        var (tStream, values) = t.TokenizeFile();
        sw.Stop();


        if (t.ErrorLog.IsValueCreated)
        {
            Console.WriteLine("Parsing could not start because of the following errors:");

            #region Error logging
            Console.ForegroundColor = ConsoleColor.Red;
            
            foreach (var msg in t.ErrorLog.Value)
                Console.WriteLine(msg);
            
            Console.ResetColor();
            #endregion

            return;
        }

        var parser = new TOMLParser(t.TokenStream, t.Values);

        sw.Start();
        parser.Parse();
        sw.Stop();

        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds); //about 250-280ms for gigatest2.toml
    }


    private static Stream FromString(string s)//testing only
    {
        MemoryStream stream = new();
        StreamWriter writer = new(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    private static int ProcessFile(Stream stream, bool printLog)
    {
        //Uncaught exception: -2, Invalid file: -1, Success: 0
        try
        {
            using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/otherlargetest.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

            TOMLTokenizer t = new(fs);
            var (tStream, values) = t.TokenizeFile();


            if (t.ErrorLog.IsValueCreated)
            {
                if (printLog)
                {
                    Console.WriteLine("Parsing could not start because of the following errors:");

                    Console.ForegroundColor = ConsoleColor.Red;

                    foreach (var msg in t.ErrorLog.Value)
                        Console.WriteLine(msg);

                    Console.ResetColor();
                }

                return -1;
            }


            TOMLParser p = new(tStream, values);
            var root = p.Parse();


            return 0;
        }

        catch { return -2; }
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

        foreach(var element in array)
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
        TBool b  => b.Value,
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
        string s => new TString(s),
        char c => new TString([c]),
        bool b => new TBool(b),
        long or uint or int or short or ushort or byte or sbyte => new TInteger(Convert.ToInt64(obj)),
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
{
    private static string SerializeValue<T>(TValue<T> val) => $$"""{"type": "{{val.SerializeType()}}", "value": "{{val.SerializeValue()}}"}""";

    private static string PrintValue(TObject obj) => obj.Type switch
    {
        TObject.TOMLType.String => SerializeValue((TValue<string>)obj),
        TObject.TOMLType.Integer => SerializeValue((TValue<long>)obj),
        TObject.TOMLType.Float => SerializeValue((TValue<float>)obj),
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
            _builder.Append(",\n");
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
