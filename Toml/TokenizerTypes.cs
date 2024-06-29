namespace Toml.Tokenization;



internal static class Constants
{
    #region Unicode Magic Numbers
    internal const int HighSurrogateStart = 0xD800;
    internal const int HighSurrogateRange = 0x3FF;
    internal const int LowSurrogateStart = 0xDC00;
    internal const int LowSurrogateEnd = 0xDFFF;
    internal const int Plane1Start = 0x10_000;
    internal const int ValidCodepointEnd = 0x110_000;
    #endregion

    #region ASCII Magic Numbers
    /// <summary>
    /// The exlusive upper bound of ASCII digits.
    /// </summary>
    internal const int AsciiDigitEnd = 0x3A;
    /// <summary>
    /// The exlusive upper bound of ASCII control characters.
    /// </summary>
    internal const int AsciiControlEnd = 0x80;
    /// <summary>
    /// The mask to normalize any ASCII letter to its upper case variant.
    /// </summary>
    internal const int AsciiUpperNormalizeMask = 0x5F;
    /// <summary>
    /// The offset to subtract from an ASCII hex letter to get its numeric value.
    /// </summary>
    internal const int AsciiHexNumOffset = 0x37;
    /// <summary>
    /// The offset to subtract from an ASCII digit character to get its numeric value.
    /// </summary>
    internal const int AsciiNumOffset = 0x30;
    #endregion


    //Used for consistent pattern matching, but maybe it's a bit overboard.
    #region Alphabet 
    internal const int EOF = -1;
    internal const char Null = '\0';
    internal const char Tab = '\t';
    internal const char Space = ' ';
    internal const char CR = '\r';
    internal const char LF = '\n';
    internal const char Backslash = '\\';
    internal const char Comment = '#';
    internal const char DoubleQuote = '"';
    internal const char SingleQuote = '\'';
    internal const char Underscore = '_';
    internal const char Dash = '-';
    internal const char KeyValueSeparator = '=';
    internal const char Dot = '.';
    internal const char Semicolon = ':';
    internal const char Comma = ',';
    internal const char SquareOpen = '[';
    internal const char SquareClose = ']';
    internal const char CurlyOpen = '{';
    internal const char CurlyClose = '}';
    internal const string Empty = "";
    #endregion


    #region TOMLStreamReader Constants
    internal const int TAB = '\t';
    internal const int SPACE = ' ';
    #endregion


    internal static readonly string[] ASCIIControlCharFriendlyName = ["NUL","SOH","STX","ETX","EOT","ENQ","ACK","BEL",
                                                                      "BS",  "HT", "LF", "VT", "FF", "CR", "SO", "SI",
                                                                      "DLE", "DC1","DC2","DC3","DC4","NAK","SYN","ETB",
                                                                      "CAN", "EM", "SUB","ESC","FS", "GS", "RS", "US",
                                                                      "SP"];


    #region Fixed Index Constants
    internal const int Time_H1 = 0;
    internal const int Time_H2 = 1;
    internal const int Time_HourSeparator = 2;
    internal const int Time_M1 = 3;
    internal const int Time_M2 = 4;
    internal const int Time_MinSeparator = 5;
    internal const int Time_S1 = 6;
    internal const int Time_S2 = 7;
    internal const int Time_Length = 8;
    internal const int Time_SecSeparator = 8;
    internal const int TimeOffset_Length = 6;
    #endregion
}


public enum TomlCommentMode
{
    Validate,
    Store,
    Skip,
}


public enum TomlTokenType
{
    Eof,
    ArrayStart,
    ArrayEnd,
    InlineTableStart,
    InlineTableEnd,
    Key,

    /// <summary>
    /// A table created through a dotted path in a key/value pair:  '<c>key<see langword="."/>subkey = 1.2</c>'.
    /// 
    /// </summary>
    ImplicitKeyValueTable,

    /// <summary>
    /// A table created through a dotted path in a table/arraytable header.:  '<c><see langword="["/>table<see langword="."/>subtable<see langword="]"/></c>'.
    /// </summary>
    ImplicitHeaderTable,

    /// <summary>
    /// A table declared with a table header: <c><see langword="["/>table<see langword="]"/></c>.
    /// </summary>
    TableDecl, 
    
    /// <summary>
    /// Marks the start of a table header.
    /// </summary>
    TableStart,

    /// <summary>
    /// An arraytable declared with an arraytable header: <c><see langword="[["/>array-table<see langword="]]"/></c>.
    /// </summary>
    ArrayTableDecl,
    
    /// <summary>
    /// Marks the start of an arraytable header.
    /// </summary>
    ArrayTableStart,

    TOKEN_SENTINEL, //Only add new 'primitives' (tokens with no grammatical significance, only representing values) AFTER this value.

    String,
    Integer,
    Float,
    Bool,
    TimeStamp,
    Comment,
}


public enum TOMLTokenMetadata
{   
    None,

    //Key (TFragment)
    Bare,
    QuotedKey,
    QuotedLiteralKey,
    
    //String (TString)
    Basic,
    Multiline,
    Literal,
    MultilineLiteral,
    
    //Timestamp (TDateTimeOffset, TDateTime, TDateOnly, TTimeOnly)
    DateOnly,
    TimeOnly,
    Local,
    UnknownLocal,
    
    //Integer (TInteger)
    Decimal,
    Hex,
    Octal,
    Binary,
    
    //Floating Point (TFloat)
    Nan,
    PositiveNan,
    NegativeNan,
}


//Tokens are essentially bloated pointers, with some extra stored type information, to values in the value-list.
//Structural tokens hold no reference (index is -1).
public readonly record struct TOMLValue : IEquatable<TOMLValue>
{
    internal TomlTokenType TokenType { get; init; }

    internal int ValueIndex { get; init; }


    public TOMLValue(TomlTokenType type, int vIndex = -1)
    {
        TokenType = type;
        ValueIndex = vIndex; //-1 means the token does not have a value associated with it. Used for structural tokens.
    }

    public override string ToString() => $"Type: {TokenType,-14} | ValueIndex: {ValueIndex} | Metadata (raw): ";
}

