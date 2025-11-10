using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Toml.Parser;
using Toml.Reader;
using Toml.Runtime;
using Toml.Tokenization;
using Toml.Diagnostics;

namespace Toml;

public class Processing //the planned front end of the library.
{   
    private static readonly TomlConfig _default = new TomlConfig(TomlCommentMode.Validate,
                                                                 ErrorReportPolicy.Aggregate, 
                                                                 ErrorSeverity.Fatal);
    /// <summary>
    /// Reads TOML from a file.
    /// </summary>
    /// <remarks>Note: the source file can be any plain-text format, .toml is not required.</remarks>
    /// <param name="filePath">The path to the TOML file.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromFile(string filePath) => FromFile(filePath, _default);


    /// <summary>
    /// Reads TOML from a file, using the provided configuration.
    /// </summary>
    /// <remarks>Note: the source file can be any plain-text format, .toml is not required.</remarks>
    /// <param name="filePath">The path to the TOML file.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromFile(string filePath, TomlConfig config)
    {   
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, 
                                  FileShare.Read, 8192, FileOptions.SequentialScan);

        using TomlStreamSource source = new(fs);

        return new TOMLParser(new(source, config)).Parse();
    }


    /// <summary>
    /// Reads TOML from a stream.
    /// </summary>
    /// <remarks>Note: the stream must support reading and seeking.</remarks>
    /// <param name="stream">The source stream.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromStream(Stream stream) => FromStream(stream, _default);


    /// <summary>
    /// Reads TOML from a stream, using the provided configuration.
    /// </summary>
    /// <remarks>Note: the stream must support reading and seeking.</remarks>
    /// <param name="stream">The source stream.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromStream(Stream stream, TomlConfig config)
    {
        using TomlStreamSource source = new(stream);

        return new TOMLParser(new(source, config)).Parse();
    }


    /// <summary>
    /// Reads TOML from a string.
    /// </summary>
    /// <param name="tomlString">The string containing the TOML source.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromString(string tomlString) => FromString(tomlString, _default, out _);
    

    /// <summary>
    /// Reads TOML from a string, using the provided configuration.
    /// </summary>
    /// <param name="tomlString">The string containing the TOML source.</param>
    /// <returns>A table representing the document's root.</returns>
    public static TTable FromString(string tomlString, TomlConfig config, out TomlDiagnosticsManager logger)
    { 
        TomlStringSource source = new(tomlString);

        var parser = new TOMLParser(new(source, config));

        var root = parser.Parse();

        logger = parser.Logger;
        return root;
    }
}
