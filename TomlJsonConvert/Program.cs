using System.Diagnostics.Contracts;
using System.Text;
using Toml.Runtime;
using Toml.Tokenization;
using Toml.Parser;
using Toml.Reader;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Runtime;

namespace TomlJsonConvert;


internal class Program
{
    static void Main()
    {
        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/test-realistic-small.txt",
                                  FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: 8192,
                                  FileOptions.SequentialScan);
        using TomlStreamSource source = new(fs);
       
        TTable root = new TOMLParser(new(source, comments: TomlCommentMode.Validate)).Parse();

        var converter = new TomlJsonConverter();
        
        string output = converter.ToJson(root);

        Console.WriteLine(output);
    }
}


//Very basic class that maps TOML to JSON suitable for the burnt-sushi test suite.
public sealed class TomlJsonConverter
{
    private static string GetTypeSerializationName(TObject.TOMLType type)
    {
        return type switch
        {
            TObject.TOMLType.String => "string",
            TObject.TOMLType.Integer => "integer",
            TObject.TOMLType.Float => "float",
            TObject.TOMLType.Boolean => "bool",
            TObject.TOMLType.DateTimeOffset => "datetime",
            TObject.TOMLType.DateTimeLocal => "datetime-local",
            TObject.TOMLType.DateOnly => "date-local",
            TObject.TOMLType.TimeOnly => "time-local",
            _ => throw new ArgumentException("Type is not a supported value type.")
        };
    }


    private static string PrintValue(TObject obj) => 
    $$"""{"type": "{{GetTypeSerializationName(obj.Type)}}", "value": "{{obj}}"}""";
    

    private int _indentLevel;

    private const int _indentChange = 2;

    private StringBuilder _builder;


    public TomlJsonConverter()
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
