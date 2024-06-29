using System.Diagnostics.Contracts;
using System.Text;
using Toml.Extensions;
using Toml.Runtime;
using Toml.Tokenization;
using Toml.Parser;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.CodeAnalysis;
using Toml.Reader;


namespace TomlJsonConvert;


internal class Program
{
    static void Main()
    {
        foreach(var dir in Directory.GetDirectories("C:/Users/BAGOLY/Desktop/TOML Project/official/toml-test-master/tests/valid"))
        {
            RunValidTests(false, Directory.GetFiles(dir));
        }

        foreach (var dir in Directory.GetDirectories("C:/Users/BAGOLY/Desktop/TOML Project/official/toml-test-master/tests/invalid"))
        {
            RunInvalidTests(false, Directory.GetFiles(dir));
        }
    }


    private static void RunInvalidTests(bool printLog, string[] testCases)
    {
        for (int i = 0; i < testCases.Length; ++i)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"Testcase {i + 1}, name '{testCases[i][(testCases[i].LastIndexOf('\\') + 1)..]}': ");
            Console.ResetColor();

            int result = ProcessFile(printLog, testCases[i], out string? msg);

            switch (result)
            {
                case 0:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nVerdict: [FAIL] Parser failed to reject the file.");
                    Console.ResetColor();
                    break;
                case -1:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nVerdict: [PASS] Parser rejected the file. Reason: {msg ?? "None given."}");
                    Console.ResetColor();
                    break;
                case -2:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nVerdict: [ERROR] The parser threw an unhandled exception. Error message: {msg ?? "No message specified."}");
                    Console.ResetColor();
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"\nVerdict: [IGNORED] Invalid return code: " + result);
                    Console.ResetColor();
                    break;
            }

            Console.WriteLine("\n\n");
        }
    }

    private static void RunValidTests(bool printLog, string[] testCases)
    {
        for (int i = 0; i < testCases.Length; ++i)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"Testcase {i + 1}, name '{testCases[i][(testCases[i].LastIndexOf('\\') + 1)..]}': ");
            Console.ResetColor();

            if (testCases[i].Last() == 'n')
            {
                Console.Write(" JSON File, skipping.\n\n");
                continue;
            }

            int result = ProcessFile(printLog, testCases[i], out string? msg);

            switch (result)
            {
                case 0:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nVerdict: [PASS] Parser found the file valid.");
                    Console.ResetColor();
                    break;
                case -1:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nVerdict: [FAIL] Parser rejected the file. Reason: {msg ?? "None given."}");
                    Console.ResetColor();
                    break;
                case -2:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nVerdict: [ERROR] Unhandled exception: {msg ?? "No message specified."}");
                    Console.ResetColor();
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"\nVerdict: [IGNORED] Invalid return code: " + result);
                    Console.ResetColor();
                    break;
            }

            Console.WriteLine("\n\n");
        }
    }


    private static int ProcessFile(bool printLog, string fPath, out string? parserErrMsg)
    {
        //Uncaught exception: -2, Invalid file: -1, Success: 0
        parserErrMsg = null;
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            using FileStream fs = new(fPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            TomlStreamSource source = new(fs);
            TOMLTokenizer t = new(source);
            var (tStream, values) = t.TokenizeFile();


            if (t.ErrorLog.Count is not 0)
            {
                if (printLog)
                {
                    Console.WriteLine($"Parsing could not start because the following syntax error{(t.ErrorLog.Count > 1 ? "s" : "")}:");

                    Console.ForegroundColor = ConsoleColor.DarkYellow;

                    Console.WriteLine($"─(*)┅> {t.ErrorLog[0]}");
                    int i = 1;
                    for (; i < t.ErrorLog.Count; ++i)
                    {
                        Console.Write($"{new(' ', i * 2 + 1)}");


                        Console.WriteLine($"┖(*)┅> {t.ErrorLog[i]}");
                    }

                    if (i != 1)
                        Console.WriteLine($"{new(' ', i * 2 + 1)}┖(*)─┅> {t.ErrorLog[^1]}");


                    Console.ResetColor();
                }

                return -1;
            }

            TOMLParser p = new(tStream, values);
            var root = p.Parse();


            return 0;
        }

        catch (TomlRuntimeException parser) { parserErrMsg = parser.Message; return -1; }
        catch (TomlReaderException reader) { parserErrMsg = reader.Message; return -1; }
        catch (Exception unhandled) { parserErrMsg = $"{unhandled.GetType()} : '{unhandled.StackTrace}'"; return -2; }
    }
}


//Just a very simple class that maps TOML to JSON suitable for the burnt-sushi test suite.
public sealed class TomlJsonConverter
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