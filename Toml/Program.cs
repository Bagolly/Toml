global using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Toml.Tokenization;
using Toml.Runtime;

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
        sw.Stop();
        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms");
        Console.ReadKey();
        //Console.ForegroundColor = ConsoleColor.Red;
        //   foreach (var msg in t.ErrorLog.Value)
        //     Console.WriteLine(msg);
        //Console.ResetColor();

        Console.OutputEncoding = Encoding.UTF8;

        foreach (var token in t.TokenStream)
        {
            Console.WriteLine($"TOKEN: {token}");

            if (token.Metadata != TOMLTokenMetadata.None)
                Console.WriteLine($"\tMETADATA:\t " + token.Metadata.ToString());

            if (token.TokenType > TomlTokenType.STRUCTURAL_TOKEN_SENTINEL)
                Console.WriteLine($"\tPOINTS TO:\t {t.Values[token.ValueIndex]}");
        }

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

    private static IEnumerable<(int start, int end)> GetKey(string str)
    {
        int start = 0;


        for (int i = 0; i < str.Length; i++)
        {
            if (i == str.Length - 1) //if not dotted
                yield return (start, i + 1);

            else if (str[i] == '.')
            {
                yield return (start, i);
                ++i;
                start = i;
            }
            else
                continue;
        }
    }

    //Constructs a hierarchy of tables from a given key and
    //returns a reference to the last added table (the active "scope").
    //DO NOT PASS the key to the value you want to add. Instead, provide the path (all but the last fragment),
    //then add the value to the table using the returned reference.
    private static TTable ResolveKey(TTable root, in ReadOnlySpan<char> str)
    {
        TTable containingTable = root;
        int start = 0;

        if (!str.Contains('.'))
            ProcessFragment(in str, start, str.Length);

        else
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (i == str.Length - 1)
                    ProcessFragment(in str, start, i + 1);

                if (str[i] == '.')
                {
                    ProcessFragment(in str, start, i);
                    ++i;
                    start = i;
                }
            }
        }

        return containingTable;

        void ProcessFragment(in ReadOnlySpan<char> str, int start, int end)
        {
            if (containingTable.Values.ContainsKey(str[start..end].ToString()))
                containingTable = (TTable)containingTable[str[start..end]];
            else
            {
                TTable subtable = [];
                containingTable.Values.Add(str[start..end].ToString(), subtable);
                containingTable = subtable;
            }
        }
    }
}
