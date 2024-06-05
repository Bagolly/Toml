namespace Toml.Tokenization;



//2 basic token types: structural and value tokens.
//Structural tokens hold no 'reference' (list index) to values in the value-list.
//Value tokens are just bloated pointers with some optional extra metadata.
enum TomlTokenType
{
    Eof,
    ArrayStart,
    ArrayEnd,
    InlineTableStart,
    InlineTableEnd,
    Key,
    Table,
    ArrayTable,
    TableDecl,
    TableDeclStart,
    ArrayTableDecl,
    ArrayTableDeclStart,

    TOKEN_SENTINEL, //Add new 'primitives' (tokens with no grammatical significance, only representing values) AFTER this value.

    String,
    Integer,
    Float,
    Bool,
    TimeStamp,
    Comment,
}


[Flags]
public enum TOMLTokenMetadata
{
    None = 0,
    QuotedKey = 1,
    QuotedLiteralKey = QuotedKey | Literal,
    Multiline = 2,
    Literal = 4,
    MultilineLiteral = Multiline | Literal,
    DateOnly = 8,
    TimeOnly = 16,
    Local = 32,
    UnkownLocal = 64,
    Hex = 128,
    Octal = 256,
    Binary = 512,
    FloatInf = 1024,
    FloatNan = 2048,
    FloatHasExponent = 4096,
}


//Tokens are essentially bloated pointers, with some extra stored type information, to values in the value-list.
//Structural tokens hold no reference (index is -1).
readonly record struct TOMLValue : IEquatable<TOMLValue>
{
    internal TomlTokenType TokenType { get; init; }

    internal int ValueIndex { get; init; }

    internal TOMLTokenMetadata Metadata { get; init; }

    public TOMLValue(TomlTokenType type, int vIndex, TOMLTokenMetadata? metadata = null)
    {
        TokenType = type;
        ValueIndex = vIndex;
        Metadata = metadata ?? TOMLTokenMetadata.None;
    }

    public override string ToString() => $"Type: {TokenType,-14} | ValueIndex: {ValueIndex}";
}