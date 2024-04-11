﻿global using System;
using static System.Char;
using Toml.Runtime;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Collections.Frozen;
using System.Text.Json;
using System.Linq;
using System.IO.Pipes;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using BenchmarkDotNet.Columns;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Formats.Asn1;
using System.Numerics;
using System.ComponentModel;
using System.Collections;
using System.CodeDom;
#pragma warning disable IDE0290


namespace Toml;

internal class Program
{
    static void Main()
    {   /*
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

        using FileStream fs = new("C:/Users/BAGOLY/Desktop/ab.txt", FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        TOMLTokenizer t = new(fs);

        t.TokenizeFile();

        Console.ForegroundColor = ConsoleColor.Red;
        foreach (var msg in t.TempErrorLogger.Value)
            Console.WriteLine(msg);
        Console.ResetColor();

        List<string> output = new(3);
        Console.OutputEncoding = Encoding.UTF8;
        foreach (var token in t.TokenStream)
            if (token.TokenType is TOMLTokenType.String)
            {   
                if(token.Metadata.HasFlag(TOMLTokenMetadata.Literal))
                    Console.WriteLine("Type string, value: \"" + token.Payload + "\"");
                else
                    Console.WriteLine("Type string, value: '" + token.Payload + "'");
            }
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



    private static void SkipWhiteSpace(string s, ref int i)
    {
        if (s is "")
            return;

        while (s[i++] is '\t' or ' ') ;

        i -= 1;
    }
}

sealed class TOMLTokenizer
{
    #region Alphabet
    internal const char EOF = '\0';
    internal const char Tab = '\t';
    internal const char Space = ' ';
    internal const char CR = '\r';
    internal const char LF = '\n';
    internal const char BackSlash = '\\';
    internal const char Comment = '#';
    internal const char DoubleQuote = '"';
    internal const char SingleQuote = '\'';
    internal const char UnderScore = '_';
    internal const char Dash = '-';
    internal const char EqualsSign = '=';
    internal const char Dot = '.';
    internal const char Comma = ',';
    internal const char SquareOpen = '[';
    internal const char SquareClose = ']';
    internal const char CurlyOpen = '{';
    internal const char CurlyClose = '}';
    internal const string Empty = "";
    #endregion

    public TOMLStreamReader Reader { get; init; }

    public Queue<TOMLToken> TokenStream { get; init; }

    public Lazy<List<string>> TempErrorLogger { get; init; }

    public TOMLTokenizer(Stream source, int capacity = 32)
    {
        Reader = new(source);
        TokenStream = new(capacity);
        TempErrorLogger = new();
    }

    public Queue<TOMLToken> TokenizeFile()
    {
        while (Reader.PeekSkip() is not EOF)
        {
            Tokenize();
        }

        TokenStream.Enqueue(new(TOMLTokenType.Eof, -1, -1, []));
        return TokenStream;
    }

    private void Synchronize(int syncMinimum) //Skips syncMinimum amount of characters
    {
        while (syncMinimum != 0)
        {
            if (Reader.Read() is EOF)
                break;

            --syncMinimum;
        }
    }

    private void Synchronize(char syncChar) //Reads until the next available char is syncChar
    {
        while (Reader.Peek() != syncChar)
        {
            if (Reader.Read() is EOF)
                break;
        }
    }


    private void Tokenize()
    {
        if (IsKey(Reader.PeekSkip(true)))
        {
            TokenizeKeyValuePair();
            return;
        }

        char readResult = Reader.ReadSkip(true);
        //Console.WriteLine("Main: read result: " + readResult);

        switch (readResult)
        {
            case EOF: return; //Empty source file

            case Comment: TokenizeComment(); return;

            case SquareOpen: TokenizeTable(); return;

            default:
                TempErrorLogger.Value.Add("Unexpected top level character: " + readResult);
                //some possible scenarios: if its eq then probably a missing key decl, sort it out later
                //can switch on the violating char when the tokenizer and rules are well in place
                Synchronize(LF);
                return;
        }

    }

    private void TokenizeComment()
    {
        //Control is passed to this method when Tokenize() encounters a '#' character.
        //The '#' character is consumed!
        char c;

        while ((c = Reader.ReadSkip()) is not EOF)
        {
            if (IsControl(c) && c is not Tab)
            {
                TempErrorLogger.Value.Add("Tab is the only control character valid inside a comment.");
                Synchronize(LF);
            }

            if (c is LF)
                break;
        }
    }

    private void TokenizeTable()
    {
        //Control is passed to this method when Tokenize() encounters a '[' character
        //The '[' character is consumed!

        //Console.WriteLine("table found, next will be: " + Reader.PeekSkip());
        if (Reader.MatchNextSkip(SquareOpen)) //Double '[' means an arraytable
        {
            TokenizeArrayTable();
            return;
        }

        //Decl tokens will be used for scope switches in the parser.
        TokenStream.Enqueue(new(TOMLTokenType.TableDecl, Reader.Line, Reader.Column, []));

        TokenizeKey(TOMLTokenType.Table);                   //Tokenize the table's key, and mark that it's part of a table declaration


        if (!Reader.MatchNextSkip(SquareClose)) //Assert that the table declaration is terminated, log error then sync if not.
        {
            TempErrorLogger.Value.Add("Unterminated table declaration.");
            Synchronize(LF);
            return;
        }

        //Console.WriteLine("No errors with table decl");
    }

    private void TokenizeArrayTable()
    {
        //Control is passed here when TokenizeTable() encounters a second '[' character.
        //Because TokenizeTable calls MatchNextSkip, the second '[' is already consumed.


        //Decl tokens will be used for scope switches in the parser.
        TokenStream.Enqueue(new(TOMLTokenType.ArrayTableDecl, Reader.Line, Reader.Column, []));


        TokenizeKey(TOMLTokenType.ArrayTable);    //Tokenize the arraytable's key, and mark that it's part of an arraytable declaration.

        if (!Reader.MatchNextSkip(SquareClose) || !Reader.MatchNextSkip(SquareClose))
        {
            TempErrorLogger.Value.Add("Unterminated arraytable declaration.");
            Synchronize(2);
            return;
        }
    }


    private void TokenizeKeyValuePair()
    {
        TokenizeKey(TOMLTokenType.Key);          //Tokenize the keyvaluepair's key

        if (!Reader.MatchNextSkip(EqualsSign))
        {
            TempErrorLogger.Value.Add("Key was not followed by '=' character.");
            Synchronize(LF);
        }

        TokenizeValue();



        if (Reader.PeekSkip() is not (CR or LF or EOF or Comment))
        {
            TempErrorLogger.Value.Add("Found trailing characters after keyvaluepair.");

            Console.WriteLine("Rest: " + Reader.BaseReader.ReadToEnd());

            Synchronize(LF);
        }
    }


    private void TokenizeKey(TOMLTokenType keyTypeModifier)
    {
        //Control is passed to this method from:
        //1. When a top-level key is found, passed as: Tokenize()->TokenizeKeyValuePair()->TokenizeKey().
        //2. TokenizeArrayTable(), after verifying both '[' characters.
        //3. TokenizeTable(), after verifying the '[' character.


        char c = Reader.PeekSkip();
        if (c is EOF)
        {
            TempErrorLogger.Value.Add("Expected key but the end of file was reached");
            return;
        }

        do
        {
            TokenizeFragment(keyTypeModifier);
        } while (Reader.MatchNextSkip(Dot));
    }

    private void TokenizeFragment(TOMLTokenType keyTypeModifier)
    {
        //Control is passed to this method from TokenizeKey() for each key fragment.

        char c = Reader.PeekSkip();
        Console.WriteLine("Fragment started, read: " + c);
        if (c is EOF)
        {
            TempErrorLogger.Value.Add("Expected fragment but the end of file was reached");
            return;
        }

        ValueStringBuilder vsb = new(stackalloc char[64]);
        TOMLTokenMetadata keyMetadata = TOMLTokenMetadata.QuotedKey;
        try
        {
            switch (c)
            {
                case DoubleQuote:
                    TokenizeString(ref vsb);
                    break;
                case SingleQuote:
                    TokenizeLiteralString(ref vsb);
                    break;
                default:
                    TokenizeBareKey(ref vsb);
                    keyMetadata = TOMLTokenMetadata.None;
                    break;
            }
            TokenStream.Enqueue(new(keyTypeModifier, Reader.Line, Reader.Column, vsb.RawChars, keyMetadata));
        }
        finally { vsb.Dispose(); }

        Console.WriteLine("Fragment finished, key: " + TokenStream.Last().Payload);
    }


    private void TokenizeArray()
    {

        //When control is passed to this method, the first [ is already consumed.
        TokenStream.Enqueue(new(TOMLTokenType.ArrayStart, Reader.Line, Reader.Column, []));

        do
        {
            TokenizeValue();
        } while (Reader.MatchNextSkip(Comma));

        if (!Reader.MatchNextSkip(SquareClose))
        {
            TempErrorLogger.Value.Add("Unterminated array");
            Synchronize(1); //dont currently have a better idea for a syncpoint, so skip one then see where it goes..
        }

        else
            TokenStream.Enqueue(new(TOMLTokenType.ArrayEnd, Reader.Line, Reader.Column, []));
    }


    private void TokenizeValue()
    {
        //Control is passed to this method from TokenizeArray() or TokenizeKeyValuePair()
        //No characters are consumed, including leading double quotes for strings.

        ValueStringBuilder buffer = new(stackalloc char[64]);

        try
        {
            switch (Reader.PeekSkip())
            {
                case DoubleQuote:
                    switch (Reader.MatchCount(DoubleQuote))
                    {
                        case 1: //One doublequote; basic string
                            TokenizeString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, Reader.Line, Reader.Column, buffer.RawChars));
                            break;
                        case 2: //Two doublequotes cannot start any string
                            TempErrorLogger.Value.Add("Strings cannot start with '\"\"'.");
                            goto ERR_SYNC;
                        case >= 3: //Three doublequotes; multiline string
                            TokenizeMultiLineString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, Reader.Line, Reader.Column, buffer.RawChars, TOMLTokenMetadata.Multiline));
                            break;
                        default:
                            TempErrorLogger.Value.Add("MatchCount returned an unusual value."); // Currently impossible, MatchCount returns between 0 and int.MaxValue
                        ERR_SYNC:
                            Synchronize(LF);
                            return;
                    }
                    break;

                case SingleQuote:
                    switch (Reader.MatchCount(SingleQuote))
                    {
                        case 1: //One singlequote; basic literal string
                            TokenizeLiteralString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, Reader.Line, Reader.Column, buffer.RawChars, TOMLTokenMetadata.Literal));
                            break;
                        case 2: //Two singlequotes cannot start any string
                            TempErrorLogger.Value.Add("Literal strings cannot start with \"''\".");
                            goto ERR_SYNC;
                        case >= 3: //Three singlequotes; multiline literal string
                            TokenizeMultiLineLiteralString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, Reader.Line, Reader.Column, buffer.RawChars, TOMLTokenMetadata.MultilineLiteral));
                            break;
                        default:
                            TempErrorLogger.Value.Add("MatchCount returned an unusual value."); // Currently impossible, MatchCount returns between 0 and int.MaxValue
                        ERR_SYNC:
                            Synchronize(LF);
                            return;

                    }
                    break;

                case SquareOpen:
                    TokenizeArray();
                    break;

                default:
                    TempErrorLogger.Value.Add($"Could find a parse method for value token {Reader.PeekSkip()}");
                    Synchronize(LF);
                    return;
            }
        }
        finally { buffer.Dispose(); }
    }

    private void TokenizeBareKey(ref ValueStringBuilder vsb)
    {
        while (IsBareKey(Reader.PeekSkip()))
            vsb.Append(Reader.ReadSkip());
    }


    private void TokenizeString(ref ValueStringBuilder vsb)
    {
        char c;
        while ((c = Reader.Read()) is not (EOF or DoubleQuote))
        {
            switch (c)
            {
                case CR or LF:
                    if (Reader.MatchLineEnding())
                    {
                        TempErrorLogger.Value.Add("Basic strings cannot span multiple lines.");
                        Synchronize(LF);
                        return;
                    }
                    break;

                case BackSlash:
                    if (Reader.Peek() is Space)
                    {
                        TempErrorLogger.Value.Add("Only multiline strings can contain line ending backslashes.");
                        Synchronize(LF);
                        return;
                    }
                    EscapeSequence(ref vsb);
                    break;

                case not Tab when IsControl(c):
                    TempErrorLogger.Value.Add("Control characters other than Tab (U+0009) must be escaped.");
                    break;

                default:
                    vsb.Append(c);
                    break;
            }
        }

        if (c is not DoubleQuote)
        {
            TempErrorLogger.Value.Add("Unterminated string");
            Synchronize(LF);
            return;
        }

        //The string itself is added in TokenizeValue(), DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated.
    }

    private void TokenizeMultiLineString(ref ValueStringBuilder vsb)
    {
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        char c;

        while ((c = Reader.Read()) is not EOF)
        {
            switch (c)
            {
                case DoubleQuote:
                    switch (Reader.MatchCount(DoubleQuote) + 1) //The number of doublequotes plus the first
                    {
                        case 1: //Only one 1 doublequote, in 'c'
                            vsb.Append(DoubleQuote);
                            continue;

                        case 2: //Two doublequotes, not terminating.
                            vsb.Append(DoubleQuote);
                            vsb.Append(DoubleQuote);
                            continue;

                        //From here on out, it's definitely terminating, just need to decide how many to add, if any.

                        case 3: //Exactly 3 doublequotes, terminate.
                            return;
                        
                        case 4: //4 doublequotes, append 1 then Terminate.
                            vsb.Append(DoubleQuote);
                            return;
                        
                        case 5: //5 doublequotes, append 2 then Terminate.
                            vsb.Append(DoubleQuote);
                            vsb.Append(DoubleQuote);
                            return;
                        
                        default:
                            TempErrorLogger.Value.Add($"Stray doublequotes.");
                            Synchronize(LF);
                            return;
                    }

                case CR when Reader.Peek() is LF: //Normalize to LF
                    vsb.Append(LF);
                    Reader.Read(); //Consume LF
                    break;

                case BackSlash:
                    if (Reader.MatchLineEnding()) //If line ending backlash
                    {
                        Reader.SkipWhiteSpace(true);
                        continue;
                    }

                    else
                        EscapeSequence(ref vsb);
                    break;

                default:
                    vsb.Append(c);
                    break;
            }
        }

        TempErrorLogger.Value.Add("Unterminated string");
        Synchronize(LF);
        return;
    }

    private void TokenizeLiteralString(ref ValueStringBuilder vsb)
    {
        char c;
        while ((c = Reader.Read()) is not (EOF or SingleQuote))
        {
            if (Reader.MatchLineEnding())
                TempErrorLogger.Value.Add("Literal strings cannot span multiple lines. Newline was removed.");

            else if (c is not Tab && IsControl(c))
                TempErrorLogger.Value.Add("Literal strings cannot contain control characters other than Tab (U+0009)");

            else
                vsb.Append(c);
        }


        if (c is not SingleQuote)
        {
            TempErrorLogger.Value.Add("Unterminated literal string");
            Synchronize(LF);
            return;
        }

        //The string itself is added in TokenizeValue(), DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated.
    }

    private void TokenizeMultiLineLiteralString(ref ValueStringBuilder vsb)
    {
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        char c;

        while ((c = Reader.Read()) is not EOF)
        {
            switch (c)
            {
                case SingleQuote:
                    switch (Reader.MatchCount(SingleQuote) + 1) //The number of singlequotes plus the first
                    {
                        case 1: //Only one 1 singlequotes, in 'c'
                            vsb.Append(SingleQuote);
                            continue;
                        case 2: //Two singlequotes, not terminating.
                            vsb.Append(SingleQuote);
                            vsb.Append(SingleQuote);
                            continue;
                        //From here on out, it's definitely terminating, just need to decide how many to add, if any.
                        case 3: //Exactly 3 singlequotes, terminate.
                            return;
                        case 4: //4 singlequotes, append 1 then Terminate.
                            vsb.Append(SingleQuote);
                            return;
                        case 5: //5 singlequotes, append 2 then Terminate.
                            vsb.Append(SingleQuote);
                            vsb.Append(SingleQuote);
                            return;
                        default:
                            TempErrorLogger.Value.Add($"Stray singlequotes.");
                            Synchronize(LF);
                            return;
                    }

                case CR when Reader.Peek() is LF: //Normalize CRLF to LF
                    vsb.Append(LF);
                    Reader.Read(); //Consume LF
                    break;

                case not Tab when IsControl(c):
                    TempErrorLogger.Value.Add("Literal strings cannot contain control characters other than Tab (U+0009)");
                    break;

                default:
                    vsb.Append(c);
                    break;
            }
        }

        TempErrorLogger.Value.Add("Unterminated string");
        Synchronize(LF);
        return;

        //Like literal strings tHeRe Is No EsCaPe-ing; however, newlines are allowed.
        //If there is a LF or CRLF after the leading ''', it will be removed.
        //Everything else will be left as-is.

        //Furthermore, 1 or 2 singlequotes are allowed anywhere.
        //3 or more obviously terminates the string.

        //Control chars other than tab are not permitted inside literal strings.
    }

   
    private void TokenizeInteger()
    {

    }


    private void TokenizeFloat()
    {

    }


    private void TokenizeBool()
    {

    }

    private void TokenizeDateTime()
    {

    }


    private void EscapeSequence(ref ValueStringBuilder vsb)
    {
        char initial = Reader.Read();

        if (initial is 'U')
        {
            UnicodeLongForm(ref vsb);
            return;
        }

        if (initial is 'u')
        {
            UnicodeShortForm(ref vsb);
            return;
        }

        char result = initial switch
        {
            'b' => '\u0008',
            't' => '\u0009',
            'n' => '\u000A',
            'f' => '\u000C',
            'r' => '\u000D',
            '"' => '\u0022',
            '\\' => '\u005C',
            _ => EOF,
        };

        if (result is EOF)
        {
            TempErrorLogger.Value.Add($"No escape sequence for character '{initial}' (U+{(int)initial:X8}).");
            Synchronize(Space);
            return;
        }

        vsb.Append(result);
    }

    private void UnicodeLongForm(ref ValueStringBuilder vsb)
    {
        //When control is passed to this method, only the numeric sequence remains (without '\' and 'U')
        int codePoint = ToUnicodeCodepoint(8);

        if (codePoint > MaxValue) //UTF-32 escape sequence, encode to surrogate pair
        {
            codePoint -= 0x10_000;
            vsb.Append((char)((codePoint >> 10) + 0xD800));
            vsb.Append((char)((codePoint & 0x3FF) + 0xDC00));

            return;
        }


        vsb.Append((char)codePoint);
        return;
    }

    private void UnicodeShortForm(ref ValueStringBuilder vsb)
    {
        int codePoint = ToUnicodeCodepoint(4);
        vsb.Append((char)codePoint);
    }


    private int ToUnicodeCodepoint(int length)
    {
        Span<char> buffer = stackalloc char[length];
        int charsRead = Reader.BaseReader.ReadBlock(buffer);

        if (charsRead < length) //It's definitely not good...
        {
            if (buffer[charsRead - 1] is DoubleQuote or EOF) //More likely error
            {
                --Reader.BaseReader.BaseStream.Position;
                TempErrorLogger.Value.Add($"Escape sequence '{buffer.ToString()}' is missing {5 - charsRead} digit(s).");
            }

            else//Generic error message
                TempErrorLogger.Value.Add($"Escape sequence '{buffer.ToString()}' must consist of {length} hexadecimal characters.");

            Synchronize(DoubleQuote);
            return -1;
        }

        int codePoint = 0;

        for (int i = 0; i < length; i++)
        {
            int digit = buffer[i] < 0x3A ? buffer[i] - 0x30 : (buffer[i] & 0x5F) - 0x37; //Convert char to hexadecimal digit

            if (digit < 0 || digit > 15)
            {
                TempErrorLogger.Value.Add($"{i + 1}. character '{buffer[i]}' in escape sequence is not a hexadecimal digit.");
                Synchronize(Space);
            }

            codePoint = (codePoint << 4) + digit; //Build up codepoint from digits
        }

        return codePoint;
    }




    private static bool IsBareKey(char c) => IsAsciiLetter(c) || IsAsciiDigit(c) || c is '-' or '_';
    private static bool IsKey(char c) => IsBareKey(c) || c is '"' or '\'';
    /*private static TTable ResolveKey(TTable root, in ReadOnlySpan<char> str)
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
        }}*/
}

enum TOMLTokenType
{
    Comment,
    Key,
    TableDecl,
    Table,
    ArrayStart,
    ArrayEnd,
    ArrayTable,
    ArrayTableDecl,
    String,
    Integer,
    Float,
    Bool,
    DateTime,
    Undefined,
    Eof,
}

[Flags]
enum TOMLTokenMetadata
{
    None = 0,
    QuotedKey = 1,
    Multiline = 2,
    Literal = 4,
    MultilineLiteral = Multiline | Literal,
    DateOnly = 8,
    TimeOnly = 16,
    DateTimeLocal = 32,
    DateTimeOffset = 64,
    Hex = 128,
    Octal = 256,
    Binary = 512,
}


readonly record struct TOMLToken : IEquatable<TOMLToken>
{
    internal TOMLTokenType TokenType { get; init; }

    internal string? Payload { get; init; }

    internal (int Line, int Column) Position { get; init; }

    internal TOMLTokenMetadata Metadata { get; init; }

    public TOMLToken(TOMLTokenType type, int ln, int col, in ReadOnlySpan<char> payload, TOMLTokenMetadata? metadata = null)
    {
        TokenType = type;
        Position = (ln, col);
        Payload = payload == ReadOnlySpan<char>.Empty ? null : payload.ToString();
        Metadata = metadata ?? TOMLTokenMetadata.None;
    }

    public override string ToString()
    {
        return $"Type: {TokenType} | Value: '{Payload ?? "None"}'";
    }
}
