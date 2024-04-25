global using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Diagnostics.Runtime.DacInterface;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Timers;
using Toml.Runtime;
using static System.Char;
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
        Stopwatch sw = Stopwatch.StartNew();
        _ = t.TokenizeFile();
        sw.Stop();
        Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds + "ms");

        Console.ForegroundColor = ConsoleColor.Red;
        foreach (var msg in t.TempErrorLogger.Value)
            Console.WriteLine(msg);
        Console.ResetColor();

        List<string> output = new(3);
        //Console.OutputEncoding = Encoding.UTF8;
        foreach (var token in t.TokenStream)
        {
            if(token.Metadata.HasFlag(TOMLTokenMetadata.TimeOnly))
                Console.Write("[TIME_ONLY] ");
            if (token.Metadata.HasFlag(TOMLTokenMetadata.DateOnly))
                Console.Write("[DATE_ONLY] ");
            if (token.Metadata.HasFlag(TOMLTokenMetadata.Local))
                Console.Write("[LOCAL] ");
            if (token.Metadata.HasFlag(TOMLTokenMetadata.UnkownLocal))
                Console.Write("[UNKOWN_LOCAL] ");

            Console.WriteLine(token.Payload);
        }
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

        TokenStream.Enqueue(new(TOMLTokenType.Eof, (-1, -1), (-1, -1), []));
        return TokenStream;
    }

    public (int, int) CurrentPosition => (Reader.Line, Reader.Column);

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

            if (Reader.PeekSkip() is not (EOF or Comment) && !Reader.MatchLineEnding())
            {
                Reader.SkipWhiteSpace();
                //ReadLine() here also functions as a syncpoint to the end of the line.
                TempErrorLogger.Value.Add($"Found trailing characters: '{Reader.BaseReader.ReadLine()}'");
            }

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
        //The '#' character is already consumed!
        var start = CurrentPosition;
        char c = Reader.Peek();
        ValueStringBuilder vsb = new(stackalloc char[128]);
        while (!Reader.MatchLineEnding())
        {
            c = Reader.Read();

            if (c is EOF)
                break;

            if (c is not Tab && IsControl(c))
            {
                TempErrorLogger.Value.Add("Tab is the only control character valid inside comments.");
                Synchronize(LF);
            }

            else
                vsb.Append(c);
        }

        TokenStream.Enqueue(new(TOMLTokenType.Comment, start, CurrentPosition, vsb.RawChars));
    }

    private void TokenizeTable()
    {
        //Control is passed to this method when Tokenize() encounters a '[' character
        //The '[' character is consumed!

        //Console.WriteLine("table found, next will be: " + Reader.PeekSkip());
        var start = CurrentPosition;

        if (Reader.MatchNextSkip(SquareOpen)) //Double '[' means an arraytable
        {
            TokenizeArrayTable();
            return;
        }

        //Decl tokens will be used for scope switches in the parser.
        TokenStream.Enqueue(new(TOMLTokenType.TableDecl, start, CurrentPosition, []));

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
        var start = CurrentPosition;

        //Decl tokens will be used for scope switches in the parser.
        TokenStream.Enqueue(new(TOMLTokenType.ArrayTableDecl, start, CurrentPosition, []));


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
    }


    /// <summary>
    /// <paramref name="keyType"/>: marks whether the key is part of a table or arraytable declaration, or a simple key.
    /// </summary>
    private void TokenizeKey(TOMLTokenType keyType)
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

        do { TokenizeKeyFragment(keyType); } while (Reader.MatchNextSkip(Dot));
    }

    private void TokenizeKeyFragment(TOMLTokenType keyTypeModifier)
    {
        //Control is passed to this method from TokenizeKey() for each key fragment.
        var start = CurrentPosition;
        char c = Reader.PeekSkip();
        //Console.WriteLine("Fragment started, read: " + c);
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
                    Reader.ReadSkip();
                    TokenizeBasicString(ref vsb);
                    break;
                case SingleQuote:
                    Reader.ReadSkip();
                    TokenizeLiteralString(ref vsb);
                    keyMetadata = TOMLTokenMetadata.QuotedLiteralKey;
                    break;
                default:
                    TokenizeBareKey(ref vsb);
                    keyMetadata = TOMLTokenMetadata.None;
                    break;
            }
            TokenStream.Enqueue(new(keyTypeModifier, start, CurrentPosition, vsb.RawChars, keyMetadata));
        }
        finally { vsb.Dispose(); }

        //Console.WriteLine("Fragment finished, key: " + TokenStream.Last().Payload);
    }

    private void TokenizeValue()
    {
        //Control is passed to this method from TokenizeArray() or TokenizeKeyValuePair()
        //No characters are consumed, including leading double quotes for strings.
        var start = CurrentPosition;
        ValueStringBuilder buffer = new(stackalloc char[128]);
        try
        {
            Reader.SkipWhiteSpace();
            switch (Reader.Peek())
            {
                case DoubleQuote:
                    switch (Reader.MatchCount(DoubleQuote))
                    {
                        case 1: //One doublequote; basic string
                            TokenizeBasicString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, start, CurrentPosition, buffer.RawChars));
                            break;
                        case 2: //Two doublequotes cannot start any string
                            TempErrorLogger.Value.Add("Strings cannot start with '\"\"'.");
                            goto ERR_SYNC;
                        case >= 3: //Three doublequotes; multiline string
                            TokenizeMultiLineBasicString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, start, CurrentPosition, buffer.RawChars, TOMLTokenMetadata.Multiline));
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
                            TokenStream.Enqueue(new(TOMLTokenType.String, start, CurrentPosition, buffer.RawChars, TOMLTokenMetadata.Literal));
                            break;
                        case 2: //Two singlequotes cannot start any string
                            TempErrorLogger.Value.Add("Literal strings cannot start with \"''\".");
                            goto ERR_SYNC;
                        case >= 3: //Three singlequotes; multiline literal string
                            TokenizeMultiLineLiteralString(ref buffer);
                            TokenStream.Enqueue(new(TOMLTokenType.String, start, CurrentPosition, buffer.RawChars, TOMLTokenMetadata.MultilineLiteral));
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
                    return;

                case CurlyOpen:
                    TokenizeInlineTable();
                    return;

                case 't' or 'f': //Bool is added on call-site because the allocated stack is very small (at most 5*2 bytes) and short-lived.
                    TokenizeBool();
                    return;

                case >= '0' and <= '9':
                    ResolveNumberOrDateTime(ref buffer);
                    return;
                case 'i' or 'n':
                case '+' or '-':        //Optional sign
                    TokenizeNumber(ref buffer);
                    return;

                default:
                    TempErrorLogger.Value.Add($"Could find a parse method for value token {Reader.PeekSkip()}");
                    Synchronize(LF);
                    return;
            }
        }
        finally { buffer.Dispose(); }
    }


    private void TokenizeInlineTable()
    {
        Reader.Read(); //Consume opening curly bracket
        var start = CurrentPosition;
        TokenStream.Enqueue(new(TOMLTokenType.InlineTableStart, start, CurrentPosition, []));

        if (Reader.MatchNextSkip(CurlyClose))
        {
            TokenStream.Enqueue(new(TOMLTokenType.InlineTableEnd, start, CurrentPosition, []));
            return;
        }

        do { TokenizeKeyValuePair(); } while (Reader.MatchNextSkip(Comma));

        Reader.SkipWhiteSpace();

        if (Reader.MatchNext(CurlyClose))
            TokenStream.Enqueue(new(TOMLTokenType.InlineTableEnd, start, CurrentPosition, []));

        else if (Reader.MatchLineEnding())
            TempErrorLogger.Value.Add("Inline tables should appear on a single line.");

        else
        {
            TempErrorLogger.Value.Add("Unterminated or invalid inline table.");
            Synchronize(LF);
        }
    }

    private void TokenizeArray()
    {
        Reader.Read(); //Consume opening [
        var start = CurrentPosition;
        TokenStream.Enqueue(new(TOMLTokenType.ArrayStart, start, CurrentPosition, []));

        do
        {
            if (Reader.PeekSkip() is SquareClose)
                break;

            TokenizeValue();
        } while (Reader.MatchLineEnding() || Reader.MatchNextSkip(Comma));

        if (!Reader.MatchNextSkip(SquareClose))
        {
            TempErrorLogger.Value.Add("Unterminated array");
            Synchronize(LF);
        }

        else
            TokenStream.Enqueue(new(TOMLTokenType.ArrayEnd, start, CurrentPosition, []));
    }


    private void TokenizeBareKey(ref ValueStringBuilder vsb)
    {
        while (IsBareKey(Reader.PeekSkip()))
            vsb.Append(Reader.ReadSkip());
    }


    private void TokenizeBasicString(ref ValueStringBuilder vsb)
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


    private void TokenizeMultiLineBasicString(ref ValueStringBuilder vsb)
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


    private void TokenizeInteger(ref ValueStringBuilder vsb, TOMLTokenMetadata format, bool useBuffer)
    {
        char c;
        bool underScore = false;
        switch (format)
        {
            case TOMLTokenMetadata.Hex: TokenizeHexadecimal(ref vsb); break;
            case TOMLTokenMetadata.Binary: TokenizeBinary(ref vsb); break;
            case TOMLTokenMetadata.Octal: TokenizeOctal(ref vsb); break;
            case TOMLTokenMetadata.None: TokenizeDecimal(ref vsb, useBuffer); break;
            default: throw new ArgumentException("If this got thrown, I'm a fucking idiot."); //Internal usage rules *should* prevent this from triggering. Times this got thrown counter: [0]
        }

        if (vsb.RawChars[^1] is '_')
        {
            TempErrorLogger.Value.Add("Numbers cannot end on an underscore.");
            Synchronize(LF);
            return;
        }

        void TokenizeOctal(ref ValueStringBuilder vsb)
        {
            Reader.MatchNext('o', useBuffer); //Skip prefix char. If called from TokenizeNumber it's consumed, but if it's synced then it's not.
            while ((c = Reader.Peek(useBuffer)) is >= '0' and <= '7' || c is '_')
            {
                if (underScore && c is '_')
                {
                    TempErrorLogger.Value.Add("Underscores in numbers must have digits on both sides.");
                    Synchronize(LF);
                    return;
                }

                underScore = c is '_';
                vsb.Append(c);
                _ = Reader.Read(useBuffer);
            }
        }

        void TokenizeBinary(ref ValueStringBuilder vsb)
        {
            Reader.MatchNext('b', useBuffer); //Skip prefix char. If called from TokenizeNumber it's consumed, but if it's synced then it's not.
            while ((c = Reader.Peek(useBuffer)) is '0' or '1' || c is '_')
            {
                if (underScore && c is '_')
                {
                    TempErrorLogger.Value.Add("Underscores in numbers must have digits on both sides.");
                    Synchronize(LF);
                    return;
                }

                underScore = c is '_';
                vsb.Append(c);
                _ = Reader.Read(useBuffer);
            }
        }

        void TokenizeHexadecimal(ref ValueStringBuilder vsb)
        {
            Reader.MatchNext('x', useBuffer); //Skip prefix char. If called from TokenizeNumber it's consumed, but if it's synced then it's not.
            while (IsAsciiHexDigit(c = Reader.Peek(useBuffer)) || c is '_')
            {
                if (underScore && c is '_')
                {
                    TempErrorLogger.Value.Add("Underscores in numbers must have digits on both sides.");
                    Synchronize(LF);
                    return;
                }

                underScore = c is '_';
                vsb.Append(c);
                _ = Reader.Read(useBuffer);
            }
        }
    }


    private void TokenizeDecimal(ref ValueStringBuilder vsb, bool useBuffer)
    {
        char c;
        bool underScore = false;

        while (IsAsciiDigit(c = Reader.Peek(useBuffer)) || c is '_')
        {
            if (underScore && c is '_')
            {
                TempErrorLogger.Value.Add("Underscores in numbers must have digits on both sides.");
                Synchronize(LF);
                return;
            }

            underScore = c is '_';
            vsb.Append(Reader.Read(useBuffer));
        }
    }


    private void ResolveNumberOrDateTime(ref ValueStringBuilder vsb)
    {

        Span<char> buffer = stackalloc char[4];
        int i = 0;
        for (; i < 4; i++)
        {
            if (!IsAsciiDigit(Reader.Peek()))//if its not a digit theres no point buffering, since datetime are only asci, so the ambiguity resolves
                break;

            buffer[i] = Reader.Read();
        }

        switch (Reader.Peek())
        {
            case '-':
                Reader.BufferFill(buffer, i);
                TokenizeDateOrDateTime(ref vsb);
                return;
            case ':':
                Reader.BufferFill(buffer, i);
                BufferedTokenizeTimeOnly();
                return;
        }


        buffer[i++] = Reader.Read();
        Reader.BufferFill(buffer, i);
        TokenizeNumber(ref vsb, true);
    }


    private void TokenizeNumber(ref ValueStringBuilder vsb, bool useBuffer = false)
    {
        char c = Reader.Read(useBuffer);
        bool? hasSign = c == '-' ? true : c == '+' ? false : null; //T is -, F is +, null is none

        var start = CurrentPosition;

        if (hasSign != null)//consume sign
            c = Reader.Read(useBuffer);


        if (c is '0') //Prefixed nums, leading zero check
        {
            switch (Reader.Peek(useBuffer))
            {
                case >= '0' and <= '9':
                    TempErrorLogger.Value.Add("Only prefixed numbers and exponents can contain leading zeros.");
                    Synchronize(LF);
                    return;

                case 'x' or 'b' or 'o' when hasSign is not null:
                    TempErrorLogger.Value.Add($"Prefixed numbers cannot have signs.");
                    Synchronize(LF);
                    return;

                case 'x':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Hex, useBuffer);
                    TokenStream.Enqueue(new(TOMLTokenType.Integer, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.Hex));
                    return;

                case 'b':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Binary, useBuffer);
                    TokenStream.Enqueue(new(TOMLTokenType.Integer, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.Binary));
                    return;

                case 'o':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Octal, useBuffer);
                    TokenStream.Enqueue(new(TOMLTokenType.Integer, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.Octal));
                    return;

                case '.':
                    if (hasSign != null)
                        vsb.Append(hasSign is true ? '-' : '+');
                    vsb.Append('0');
                    break; //will go to the switch

                default:
                    TokenStream.Enqueue(new(TOMLTokenType.Integer, start, CurrentPosition, ['0']));
                    return;
            }
        }

        else if (IsAsciiDigit(c)) //Decimal number
        {
            if (hasSign != null)
                vsb.Append(hasSign is true ? '-' : '+');

            vsb.Append(c); //Append the originally read character

            TokenizeInteger(ref vsb, TOMLTokenMetadata.None, useBuffer);

            if (Reader.Peek(useBuffer) is not ('.' or 'e' or 'E')) //Decimal integer
            {
                TokenStream.Enqueue(new(TOMLTokenType.Integer, start, CurrentPosition, vsb.RawChars));
                return;
            }
        }


        else if (c is 'i') //Infinity
        {
            if (!Reader.MatchNext('n', useBuffer) || !Reader.MatchNext('f', useBuffer))
            {
                TempErrorLogger.Value.Add("Expected literal 'inf'.");
                Synchronize(LF);
            }

            else
            {
                if (hasSign != null)
                    vsb.Append(hasSign is true ? '-' : '+');
                vsb.Append("inf".AsSpan());
                TokenStream.Enqueue(new(TOMLTokenType.Float, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.FloatInf));
            }

            return;
        }


        else if (c is 'n') //NaN
        {
            if (!Reader.MatchNext('a', useBuffer) || !Reader.MatchNext('n', useBuffer))
            {
                TempErrorLogger.Value.Add("Expected literal 'nan'.");
                Synchronize(LF);
            }

            else
            {
                if (hasSign != null)
                    vsb.Append(hasSign is true ? '-' : '+');
                vsb.Append("nan".AsSpan());
                TokenStream.Enqueue(new(TOMLTokenType.Float, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.FloatNan));
            }

            return;
        }

        switch (Reader.Read(useBuffer)) //Float
        {
            case '.': //Fractional
                TokenizeFractional(ref vsb, useBuffer);

                if (Reader.Peek(useBuffer) is 'e' or 'E') //An exponent part may follow a fractional part
                {
                    Reader.Read(useBuffer);
                    goto case 'e';
                }

                TokenStream.Enqueue(new(TOMLTokenType.Float, start, CurrentPosition, vsb.RawChars));
                return;

            case 'E': //Exponent
            case 'e':
                TokenizeExponent(ref vsb, useBuffer);
                TokenStream.Enqueue(new(TOMLTokenType.Float, start, CurrentPosition, vsb.RawChars, TOMLTokenMetadata.FloatHasExponent));
                return;
        }
    }

    private void TokenizeExponent(ref ValueStringBuilder vsb, bool useBuffer)
    {
        //Exponent character should already be consumed when control is passed to this method!
        vsb.Append('e');

        char maybeSign = Reader.Peek(useBuffer);

        if (maybeSign is '-' or '+') //basically a MatchNext() but shorter (and 1 less branch)
            vsb.Append(Reader.Read(useBuffer));


        //The exponent part "follows the same rules as decimal integer values but may include leading zeroes."
        TokenizeDecimal(ref vsb, useBuffer);
        return;
    }

    private void TokenizeFractional(ref ValueStringBuilder vsb, bool useBuffer)
    {
        //Dot should already be consumed when control is passed to this method!
        if (!IsAsciiDigit(Reader.Peek(useBuffer)))
        {
            TempErrorLogger.Value.Add("Decimal points must have digits on both sides.");
            Synchronize(LF);
            return;
        }

        vsb.Append('.');

        TokenizeDecimal(ref vsb, useBuffer);  //The fractional part is "a decimal point followed by one or more digits."
    }


    private void TokenizeBool()
    {
        var start = CurrentPosition;
        if (Reader.Peek() is 't')
        {
            Span<char> bufferT = stackalloc char[4];

            if (Reader.BaseReader.ReadBlock(bufferT) != 4 || bufferT is not "true")
            {
                TempErrorLogger.Value.Add($"Expected Boolean value 'true' but found {bufferT.ToString()}");
                Synchronize(LF);
                return;
            }
            TokenStream.Enqueue(new(TOMLTokenType.Bool, start, CurrentPosition, bufferT));
            return;
        }

        Span<char> bufferF = stackalloc char[5];
        if (Reader.BaseReader.ReadBlock(bufferF) != 5 || bufferF is not "false")
        {
            TempErrorLogger.Value.Add($"Expected Boolean value 'false' but found {bufferF.ToString()}");
            Synchronize(LF);
            return;
        }

        TokenStream.Enqueue(new(TOMLTokenType.Bool, start, CurrentPosition, bufferF));
        return;
    }


    private void TokenizeDateOrDateTime(ref ValueStringBuilder vsb)
    {
        Reader.Read();  //Consume '-' (The char is already checked before calling this method)
        TOMLTokenMetadata metadata = TOMLTokenMetadata.None;
        Span<char> buffer = stackalloc char[5];

        if (Reader.BaseReader.ReadBlock(buffer) != 5)
        {
            TempErrorLogger.Value.Add("Invalid date format.");
            Synchronize(LF);
            return;
        }

        if (!DateOnly.TryParseExact(buffer, "MM-dd", out var date))
        {
            TempErrorLogger.Value.Add("Invalid month and/or day.");
            Synchronize(LF);
            return;
        }

        date = new(int.Parse(Reader.GetBuffer()), date.Month, date.Day); //retrieve year from buffer


        if (!IsAsciiDigit(Reader.PeekSkip()) && !Reader.MatchNext('T'))
        {
            Console.WriteLine("Dateonly. Result: " + date);
            metadata |= TOMLTokenMetadata.DateOnly;

            TokenStream.Enqueue(new(TOMLTokenType.TimeStamp, CurrentPosition, CurrentPosition, date.ToString(), metadata));

            return;
        }

        TimeOnly time = TokenizeTimeOnly();

        //If no offset is provided, it will be set to +00:00 (AKA UTC).
        //However, the tokenizer will flag it as 'local' if no offset was provided, to
        //avoid potential ambiguity.
        TimeSpan offset = TokenizeTimeOffset(ref metadata);

        System.DateTimeOffset result = new(date, time, offset); //hook in later

        TokenStream.Enqueue(new(TOMLTokenType.TimeStamp, CurrentPosition, CurrentPosition, result.ToString(), metadata));
        //Console.WriteLine(result);
    }



    private TimeOnly BufferedTokenizeTimeOnly() //For timeonly, when input is partially buffered
    {
        //When control is passed to this method (and the input is valid),
        //the input contains the hour component, and the peek is ':'.
        
        Span<char> buffer = stackalloc char[5]; //mm:ss
        TOMLTokenMetadata metadata = TOMLTokenMetadata.TimeOnly;

        Reader.Read(); //Skip ':'

        if (Reader.BaseReader.ReadBlock(buffer) != 5)
        {
            TempErrorLogger.Value.Add("Invalid time format.");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }


        if (!TimeOnly.TryParseExact(buffer, "mm:ss", out var time))
        {
            TempErrorLogger.Value.Add("Invalid time value");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }

        time = time.AddHours(int.Parse(Reader.GetBuffer()));

        if (Reader.MatchNext(Dot))
            time = time.Add(TimeSpan.FromTicks(TokenizeFracSeconds()));


        //TMP
        System.DateTimeOffset result =  new(DateOnly.MinValue, time, TokenizeTimeOffset(ref metadata));
        TokenStream.Enqueue(new(TOMLTokenType.TimeStamp, CurrentPosition, CurrentPosition, result.ToString(), metadata));



        return time;


        //the actual result will be a datetimeoffset with a zeroed date when the toml date is done
    }


    private TimeOnly TokenizeTimeOnly()
    {
        //When control is passed to this method the delimiter (if any) should already be consumed
        Span<char> buffer = stackalloc char[8];

        if (Reader.BaseReader.ReadBlock(buffer) != 8)
        {
            TempErrorLogger.Value.Add("Invalid time format.");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }


        if (!TimeOnly.TryParseExact(buffer, "hh:mm:ss", out var time))
        {
            TempErrorLogger.Value.Add("Invalid time value");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }


        if (Reader.MatchNext(Dot))
            time = time.Add(TimeSpan.FromTicks(TokenizeFracSeconds()));//returns the fractionals seconds in ticks

        return time;
    }


    private TimeSpan TokenizeTimeOffset(ref TOMLTokenMetadata metadata)
    {
        TimeSpan result = TimeSpan.Zero;
        bool maybeUnkown = false;

        switch (Reader.Peek())
        {
            case 'Z':
                Reader.Read();
                break;

            case '-':
                maybeUnkown = true;
                goto case '+';
            case '+':
                Span<char> buffer = stackalloc char[6];
                if (Reader.BaseReader.ReadBlock(buffer) != 6)
                {
                    TempErrorLogger.Value.Add("Invalid time offset format");
                    Synchronize(LF);
                }

                if (!TimeSpan.TryParse(buffer[0] is '+' ? buffer[1..] : buffer, out result)) //Because TryParse fails on '+'. However '-' is fine.
                {
                    TempErrorLogger.Value.Add("Invalid time offset");
                    Synchronize(LF);
                }
                break;

            default:
                metadata |= TOMLTokenMetadata.Local;
                break;
        }

        if(result == TimeSpan.Zero && maybeUnkown) //The offset -00:00 is a convention for unkown offsets
            metadata |= TOMLTokenMetadata.UnkownLocal;
        
        return result;
    }


    private int TokenizeFracSeconds()
    {
        int ticks = 0, i = 0;

        for (; i < 5 && IsAsciiDigit(Reader.Peek()); i++) //frac sec max prec is 6 digits
        {
            ticks *= 10;
            ticks += (Reader.Read() - 0x30);
        }

        ticks *= (int)Math.Pow(10, 5 - i);

        //Console.WriteLine(ticks + "ns (" + (ticks / 100) + " ticks)");


        if (IsAsciiDigit(Reader.Peek()))
        {
            TempErrorLogger.Value.Add("Fractional seconds are only supported up to 5 digits, since 1 tick in .NET equals 100 nanoseconds. Value has been truncated.");
            while (IsAsciiDigit(Reader.Peek())) //Truncate any additional parts, as per spec
                Reader.Read();
        }

        return ticks;
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
    Table,
    TableDecl,
    ArrayTable,
    ArrayTableDecl,
    InlineTableStart,
    InlineTableEnd,
    ArrayStart,
    ArrayEnd,
    Key,
    String,
    Integer,
    Float,
    Bool,
    TimeStamp,
    Comment,
    Eof,
    Undefined,
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


readonly record struct TOMLToken : IEquatable<TOMLToken>
{
    internal TOMLTokenType TokenType { get; init; }

    internal string? Payload { get; init; }

    internal (int Line, int Column) Start { get; init; }

    internal (int Line, int Column) End { get; init; }

    internal TOMLTokenMetadata Metadata { get; init; }

    public TOMLToken(TOMLTokenType type, (int l, int c) start, (int l, int c) end, in ReadOnlySpan<char> payload, TOMLTokenMetadata? metadata = null)
    {
        TokenType = type;
        Start = (start.l, start.c);
        End = (end.l, end.c);
        Payload = payload == ReadOnlySpan<char>.Empty ? null : payload.ToString();
        Metadata = metadata ?? TOMLTokenMetadata.None;
    }

    public override string ToString() => $"Type: {TokenType,-14} | Value: '{Payload ?? "[N/A]"}'";
}
