global using System;

using System.Diagnostics;
using System.IO;
using System.Text;
using Toml.Tokenization;
using Toml.Parser;
using Toml.Extensions;

namespace Toml;


internal class Program
{
    static void Main()
    {
        /*
        TTable root = [];

        ReadOnlySpan<char> key1 = "fruit.apple";
        ReadOnlySpan<char> key2 = "animal";
        ReadOnlySpan<char> key3 = "fruit.orange";


        var subtable = ResolveKey(root, in key1); //returns the table at "ab.cde.fghij"
        subtable.Add("color", new TString("green"));

        subtable = ResolveKey(root, in key2);    //returns the table at "ab.cde.fghij.l.g"
        subtable.Add("lifespan", new TInteger(27));

        subtable = ResolveKey(root, in key3);
        subtable.Add("color", new TString("guess..."));

        
        Console.WriteLine("Test1 | fruit.apple.color: " + root["fruit"]["apple"]["color"]);
        Console.WriteLine("Test2 | animal.lifespan: " + root["animal"]["lifespan"]);
        Console.WriteLine("Test3 | fruit.orange.color: " + root["fruit"]["orange"]["color"]);
        */
        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/working.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        TOMLTokenizer t = new(fs);
        Stopwatch sw = Stopwatch.StartNew();
        var (tStream, values) = t.TokenizeFile();

      //  foreach (var tkn in t.TokenStream)
        //    Console.WriteLine(tkn);

       


        TOMLParser p = new(tStream, values);
        var root = p.Parse();
        sw.Stop();
        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms");
        //Console.ReadKey();
        Console.ForegroundColor = ConsoleColor.Red;
           foreach (var msg in t.ErrorLog.Value)
             Console.WriteLine(msg);
        Console.ResetColor();

        Console.OutputEncoding = Encoding.UTF8;



        //parser result DEBUG
        // foreach (var (key, val) in root)
        // Console.WriteLine($"{key} = {val}");

        //access DEBUG

        Console.WriteLine("RESULT>>>   " + root["array"]);

        foreach(var value in root["array"].AsArray())
            Console.WriteLine($"Type: {value.Type}, Value: {value}");

    
      //  Console.WriteLine("Format: " + root["color"]["format"]);
        //Console.WriteLine("Supports alpha: " + root["color"]["hasAlphaChannel"]);
        //Console.WriteLine("# of channels (colors + alpha): " + ((TArray)root["color"]["channels"]).Count());



        //key collection DEBUG
        // foreach(var whatinthefuck in root.Values.Keys)
          //  Console.WriteLine(whatinthefuck.Length);
        
        /*foreach (var token in t.TokenStream)
        {
            Console.WriteLine($"TOKEN: {token}");

            if (token.Metadata != TOMLTokenMetadata.None)
                Console.WriteLine($"\tMETADATA:\t " + token.Metadata.ToString());

            if (token.TokenType > TomlTokenType.TOKEN_SENTINEL)
                Console.WriteLine($"\tPOINTS TO:\t {t.Values[token.ValueIndex]}");
        }*/


        

        /*Console.WriteLine(t.TokenStream.Count + " tokens parsed.");
        Console.WriteLine(t.Values.Count + " values stored.");

        var list = t.Values.GroupBy(x => x.Type);

        foreach(var category in list)
        {
            Console.WriteLine($"Type: {category.Key} | Amount: {category.Count()}");
        }*/
    }

    public static Stream FromString(string s)//testing only
    {
        MemoryStream stream = new();
        StreamWriter writer = new(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
