#pragma warning disable CA1822 //Static methods are not supported as benchmarks.

using Toml.Reader;
using Toml.Parser;
using Toml.Tokenization;
using Toml.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace TomlBenchmarking;


internal class Program
{
   static void Main() => BenchmarkRunner.Run<TomlBenchmark>();
}



public class TomlBenchmark
{
    [Benchmark(Description = "Complete Read-Tokenize-Parse from disk")]
    public TTable RunToml()
    {
        using FileStream fs = new("C:\\Users\\BAGOLY\\Desktop\\TOML Project\\TomlTest\\comment-mixed.txt",
                                    FileMode.Open, FileAccess.Read, FileShare.Read,
                                    bufferSize: 8192,
                                    FileOptions.SequentialScan);

        using TomlStreamSource source = new(fs);
        TomlConfig config = new(TomlCommentMode.Validate, Toml.Diagnostics.ErrorReportPolicy.Throw);


        return new TOMLParser(new(source, config)).Parse();
    }


    [Benchmark(Description = "Tomlyn Read-Tokenize-Parse from source string")]
    public Tomlyn.Model.TomlTable RunTomlyn()
    {
        string str = File.ReadAllText("C:\\Users\\BAGOLY\\Desktop\\TOML Project\\TomlTest\\comment-mixed.txt");

        return Tomlyn.Toml.ToModel(str, null);
    }
}

public class TomlynBenchmark
{
    [Benchmark(Description = "Tomlyn Read-Tokenize-Parse from source string")]
    public Tomlyn.Model.TomlTable Run()
    {
        string str = File.ReadAllText("C:\\Users\\BAGOLY\\Desktop\\TOML Project\\TomlTest\\comment-mixed.txt");
        
        return Tomlyn.Toml.ToModel(str, null);
    }
}
