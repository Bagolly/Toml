using System.IO;
using System.Text;
using Toml.Runtime;
using Toml.Reader;
using static Toml.Extensions.TomlExtensions;
using static Toml.Tokenization.Constants;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;


namespace Toml.Reader;

sealed class TomlStreamSource : ITomlReaderSource, IDisposable
{
    private StreamReader BaseReader { get; init; }

    public TomlStreamSource(Stream source)
    {
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("Streams without read or seeking support cannot be used.");


        //Throw on invalid UTF8, don't emit BOM, and do not detect encoding; it MUST be valid UTF8, otherwise it's an error that should be caught.
        BaseReader = new(source, new UTF8Encoding(false, true), false);
    }


    public int Read() => BaseReader.Read();


    public int Peek() => BaseReader.Peek();

    public int ReadBlock(Span<char> buffer) => BaseReader.ReadBlock(buffer);


    public void Dispose() => BaseReader.Dispose();
}



sealed class TomlStringSource : ITomlReaderSource
{
    internal string Source { get; init; }

    private int _pos;

    public TomlStringSource([DisallowNull] string source)
    {
        Source = source;
        _pos = 0;
    }


    public int Read() => _pos >= Source.Length ? EOF : Source[_pos++];

    public int Peek() => _pos >= Source.Length ? EOF : Source[_pos];


    public int ReadBlock(Span<char> buffer)
    {
        if (_pos >= Source.Length) //read already finished
            return 0;


        var substring = buffer.Length <= Source.Length - _pos ? //If there are more remaining characters than the buffer can hold,
                       Source.AsSpan(_pos, buffer.Length) :     //only read as much as the buffer can hold;
                       Source.AsSpan(_pos);                     //otherwise, read to end.

        substring.CopyTo(buffer);

        _pos += substring.Length; //Set seeker forward by the amount of characters read

        return substring.Length;
    }

}
