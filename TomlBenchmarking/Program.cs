using System.Text;
using Toml;
using Toml.Reader;
using Toml.Parser;
using Toml.Tokenization;
using Toml.Runtime;
using BenchmarkDotNet;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace TomlBenchmarking;

#pragma warning disable CA1822 //Static methods are not supported as benchmarks.

internal class Program
{
    static void Main() => BenchmarkRunner.Run<TomlBenchmark>();
}


public class TomlBenchmark
{
    [Benchmark]
    public TTable SetupTokenizer()
    {
        using FileStream fs = new("C:/Users/BAGOLY/Desktop/TOML Project/TomlTest/gigatest2.txt",
                                  FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: 8192,
                                  FileOptions.SequentialScan);

        using TomlStreamSource source = new(fs);

        return new TOMLParser(new(source, comments: TomlCommentMode.Store)).Parse();
    }
}
