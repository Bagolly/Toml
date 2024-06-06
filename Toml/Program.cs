global using System;

using System.Diagnostics;
using System.IO;
using System.Text;
using Toml.Tokenization;
using Toml.Parser;
using Toml.Extensions;
using Toml.Runtime;

namespace Toml;


internal class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
      
        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/working.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        TOMLTokenizer t = new(fs);
        Stopwatch sw = Stopwatch.StartNew();
        var (tStream, values) = t.TokenizeFile();
         

       // foreach(var tkn in t.TokenStream)
         // Console.WriteLine(tkn + " ");

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


        TOMLParser p = new(tStream, values);
        var root = p.Parse();

        foreach(var dt in root)
            Console.WriteLine(dt);

        sw.Stop();
        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms");
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
            using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/working.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

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
