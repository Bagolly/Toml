global using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using Toml.Runtime;
using static System.Char;
#pragma warning disable IDE0290


namespace Toml.Tokenization;


sealed class TOMLTokenizer
{
    #region Alphabet 
    //Used for consistent pattern matching
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

    public Queue<TOMLValue> TokenStream { get; init; }

    public Lazy<List<string>> ErrorLog { get; init; }

    internal List<TObject> Values { get; init; }

    private int ValueIndex = -1; //Tracks the last index of the list (like a stack).

    public TOMLTokenizer(Stream source, int capacity = 32)
    {
        Reader = new(source);
        TokenStream = new(capacity);
        ErrorLog = new();
        Values = new(capacity);
    }

    public (Queue<TOMLValue>, List<TObject>) TokenizeFile()
    {
        while (Reader.PeekSkip() is not EOF)
        {
            Tokenize();
        }


        TokenStream.Enqueue(new(TomlTokenType.Eof, -1)); //acts as sentinel in parser. Dont forget to remove if it ends up unused!
        return (TokenStream, Values);
    }


    private void Add(TObject obj)
    {
        Values.Add(obj);
        ValueIndex++;

        Debug.Assert(ValueIndex == Values.Count - 1, "ValueIndex is out of sync with list's count.");
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

            if (Reader.PeekSkip() is not (EOF or Comment) && !Reader.MatchLineEnding())
            {
                Reader.SkipWhiteSpace();
                //ReadLine() here also functions as a syncpoint to the end of the line.
                ErrorLog.Value.Add($"Found trailing characters: '{Reader.BaseReader.ReadLine()}'");
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
                ErrorLog.Value.Add("Unexpected top level character: " + readResult);
                Synchronize(LF);
                return;
        }

    }


    private void TokenizeComment()
    {
        //Control is passed to this method when Tokenize() encounters a '#' character.
        //The '#' character is already consumed!
        char c;


        //Regarding the commented section: comments are currently not part of the parser's feed.

        ValueStringBuilder vsb = new(stackalloc char[128]);
        while (!Reader.MatchLineEnding())
        {
            c = Reader.Read();

            if (c is EOF)
                break;

            if (c is not Tab && IsControl(c))
            {
                ErrorLog.Value.Add("Tab is the only control character valid inside comments.");
                Synchronize(LF);
            }

            else
                vsb.Append(c);
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

        //Decl tokens will be used for scope switches in the parser.  <- Now that there is a parser in place, this may end up being unnecessary.
        TokenStream.Enqueue(new(TomlTokenType.TableDeclStart, -1));

        TokenizeKey(TomlTokenType.TableDecl);                   //Tokenize the table's key, and mark that it's part of a table declaration


        if (!Reader.MatchNextSkip(SquareClose)) //Assert that the table declaration is terminated, log error then sync if not.
        {
            ErrorLog.Value.Add("Unterminated table declaration.");
            Synchronize(LF);
            return;
        }

        //Console.WriteLine("No errors with table decl");
    }


    private void TokenizeArrayTable()
    {
        //Control is passed here when TokenizeTable() encounters a second '[' character.
        //Because TokenizeTable calls MatchNextSkip, the second '[' is already consumed.

        //Decl tokens will be used for scope switches in the parser.  <- Now that there is a parser in place, this may end up being unnecessary.
        TokenStream.Enqueue(new(TomlTokenType.ArrayTableDeclStart, -1));


        TokenizeKey(TomlTokenType.ArrayTableDecl);    //Tokenize the arraytable's key, and mark that it's part of an arraytable declaration.

        if (!Reader.MatchNextSkip(SquareClose) || !Reader.MatchNextSkip(SquareClose))
        {
            ErrorLog.Value.Add("Unterminated arraytable declaration.");
            Synchronize(2);
            return;
        }
    }


    private void TokenizeKeyValuePair()
    {
        TokenizeKey(TomlTokenType.Key);          //Tokenize the keyvaluepair's key

        if (!Reader.MatchNextSkip(EqualsSign))
        {
            ErrorLog.Value.Add("Key was not followed by '=' character.");
            Synchronize(LF);
        }

        TokenizeValue();
    }


    /// <summary>
    /// <paramref name="keyType"/>: marks whether the key is part of a table or arraytable declaration, or a simple key.
    /// </summary>
    private void TokenizeKey(TomlTokenType keyType) //TODO: might be good candidate for inlining (relatively short, no trycatch or state etc., only 3 callsites), needs testing!
    {
        //Control is passed to this method from:
        //1. When a top-level key is found, passed as: Tokenize()->TokenizeKeyValuePair()->TokenizeKey().
        //2. TokenizeArrayTable(), after verifying both '[' characters.
        //3. TokenizeTable(), after verifying the '[' character.

        char c = Reader.PeekSkip();
        if (c is EOF)
        {
            ErrorLog.Value.Add("Expected key but the end of file was reached");
            return;
        }


        //This part was overhauled to make parsing easier. In a dotted key, every fragment refers to a table,
        //apart from the last, which can be a table, arraytable or key.
        //
        //So to make parsing (and code reuse) easier, every fragment in a dotted key is parsed as a table,
        //apart from the last, which will be a normal key.
        //
        //Furthermore, ArrayTableDecl and TableDecl was repurposed; now every key fragment inside '[ ]' or '[[ ]]'
        //will be tokenized as the respective 'Decl' version of its type.
        //This makes enforcing the no-redeclaration rule for tables and arraytables easier in the parser.
        while (true)
        {
            var resolvedMetadata = TokenizeKeyFragment(); //Note: this also adds the key itself to the value list. The structural token is added here afterwards.

            if (!Reader.MatchNextSkip(Dot))
            {
                //Console.WriteLine("Passed type: " + keyType);
                TokenStream.Enqueue(new(keyType, ValueIndex, resolvedMetadata));
                break;
            }

            else
                TokenStream.Enqueue(new(TomlTokenType.Table, ValueIndex, resolvedMetadata));
        }
    }


    private TOMLTokenMetadata TokenizeKeyFragment()
    {
        //Control is passed to this method from TokenizeKey() for each key fragment.
        char c = Reader.PeekSkip();


        if (c is EOF)
        {
            ErrorLog.Value.Add("Expected fragment but the end of file was reached");
            return TOMLTokenMetadata.None;
        }

        if (c is Dot)
        {
            ErrorLog.Value.Add("Empty key fragment in dotted key");
            return TOMLTokenMetadata.None;
        }


        ValueStringBuilder vsb = new(stackalloc char[64]);
        TOMLTokenMetadata keyMetadata = TOMLTokenMetadata.None;

        try
        {
            switch (c)
            {
                case DoubleQuote:
                    Reader.ReadSkip();
                    TokenizeBasicString(ref vsb);
                    keyMetadata = TOMLTokenMetadata.QuotedKey;
                    break;
                case SingleQuote:
                    Reader.ReadSkip();
                    TokenizeLiteralString(ref vsb);
                    keyMetadata = TOMLTokenMetadata.QuotedLiteralKey;
                    break;
                default:
                    TokenizeBareKey(ref vsb);
                    break;
            }


            //avoid using rawchars, because of lack of null termination. (RawChars length will equal the builderss stackallocated length, currently 64)
            Add(new TFragment(vsb.ToString(), vsb.Length));
            return keyMetadata;
        }

        finally { vsb.Dispose(); }
    }

    private void TokenizeValue()
    {
        //Control is passed to this method from TokenizeArray() or TokenizeKeyValuePair()
        //No characters are consumed, including leading double quotes for strings.
        ValueStringBuilder buffer = new(stackalloc char[128]);
        try
        {
            Reader.SkipWhiteSpace();
            switch (Reader.Peek())
            {
                case DoubleQuote:
                    switch (Reader.MatchCount(DoubleQuote))
                    {
                        //One doublequote; basic string
                        case 1:
                            TokenizeBasicString(ref buffer);
                            Add(new TString(buffer.RawChars));
                            TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex));
                            break;


                        //Two doublequotes cannot start any string
                        case 2:
                            ErrorLog.Value.Add("Strings cannot start with '\"\"'.");
                            goto ERR_SYNC;


                        //Three doublequotes; multiline string
                        case >= 3:
                            TokenizeMultiLineBasicString(ref buffer);
                            Add(new TString(buffer.RawChars));
                            TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, TOMLTokenMetadata.Multiline));
                            break;


                        default:
                            ErrorLog.Value.Add("MatchCount returned an unusual value."); // Currently impossible, MatchCount returns [0; MaxValue[
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
                            Add(new TString(buffer.RawChars));
                            TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, TOMLTokenMetadata.Literal));
                            break;
                        case 2: //Two singlequotes cannot start any string
                            ErrorLog.Value.Add("Literal strings cannot start with \"''\".");
                            goto ERR_SYNC;
                        case >= 3: //Three singlequotes; multiline literal string
                            TokenizeMultiLineLiteralString(ref buffer);
                            Add(new TString(buffer.RawChars));
                            TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, TOMLTokenMetadata.MultilineLiteral));
                            break;
                        default:
                            ErrorLog.Value.Add("MatchCount returned an unusual value."); // Currently impossible, MatchCount returns between 0 and int.MaxValue
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
                    ErrorLog.Value.Add($"Could find a parse method for value token {Reader.PeekSkip()}");
                    Synchronize(LF);
                    return;
            }
        }
        finally { buffer.Dispose(); }
    }


    private void TokenizeInlineTable()
    {
        Reader.Read(); //Consume opening curly bracket

        TokenStream.Enqueue(new(TomlTokenType.InlineTableStart, -1));


        if (Reader.MatchNextSkip(CurlyClose))
        {
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));
            return;
        }

        do { TokenizeKeyValuePair(); } while (Reader.MatchNextSkip(Comma));

        Reader.SkipWhiteSpace();

        if (Reader.MatchNext(CurlyClose))
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));

        else if (Reader.MatchLineEnding())
            ErrorLog.Value.Add("Inline tables should appear on a single line.");

        else
        {
            ErrorLog.Value.Add("Unterminated or invalid inline table.");
            Synchronize(LF);
        }
    }

    private void TokenizeArray()
    {
        Reader.Read(); //Consume opening [
        TokenStream.Enqueue(new(TomlTokenType.ArrayStart, -1));
        int elementCount = 0;

        do
        {
            Reader.SkipWhiteSpace(true);

            if (Reader.Peek() is SquareClose)
                break;

            if (Reader.Peek() is Comment)
            {
                TokenizeComment();
                continue;
            }

            TokenizeValue();
            ++elementCount;
        } while (Reader.MatchLineEnding() || Reader.MatchNextSkip(Comma));

        if (!Reader.MatchNextSkip(SquareClose))
        {

            ErrorLog.Value.Add("Unterminated array");
            Synchronize(LF);
        }

        else
        {
            TokenStream.Enqueue(new(TomlTokenType.ArrayEnd, -1));
        }
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
                        ErrorLog.Value.Add("Basic strings cannot span multiple lines.");
                        Synchronize(LF);
                        return;
                    }
                    break;

                case BackSlash:
                    if (Reader.Peek() is Space)
                    {
                        ErrorLog.Value.Add("Only multiline strings can contain line ending backslashes.");
                        Synchronize(LF);
                        return;
                    }
                    EscapeSequence(ref vsb);
                    break;

                case not Tab when IsControl(c):
                    ErrorLog.Value.Add("Control characters other than Tab (U+0009) must be escaped.");
                    break;

                default:
                    vsb.Append(c);
                    break;
            }
        }

        if (c is not DoubleQuote)
        {
            ErrorLog.Value.Add("Unterminated string");
            Synchronize(LF);
            return;
        }

        //The string itself is added in TokenizeValue() or as a key, DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
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
                            ErrorLog.Value.Add($"Stray doublequotes.");
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

        ErrorLog.Value.Add("Unterminated string");
        Synchronize(LF);
        return;
    }


    private void TokenizeLiteralString(ref ValueStringBuilder vsb)
    {
        char c;
        while ((c = Reader.Read()) is not (EOF or SingleQuote))
        {
            if (Reader.MatchLineEnding())
                ErrorLog.Value.Add("Literal strings cannot span multiple lines. Newline was removed.");

            else if (c is not Tab && IsControl(c))
                ErrorLog.Value.Add("Literal strings cannot contain control characters other than Tab (U+0009)");

            else
                vsb.Append(c);
        }


        if (c is not SingleQuote)
        {
            ErrorLog.Value.Add("Unterminated literal string");
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
                            ErrorLog.Value.Add($"Stray singlequotes.");
                            Synchronize(LF);
                            return;
                    }

                case CR when Reader.Peek() is LF: //Normalize CRLF to LF
                    vsb.Append(LF);
                    Reader.Read(); //Consume LF
                    break;

                case not Tab when IsControl(c):
                    ErrorLog.Value.Add("Literal strings cannot contain control characters other than Tab (U+0009)");
                    break;

                default:
                    vsb.Append(c);
                    break;
            }
        }

        ErrorLog.Value.Add("Unterminated string");
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
            ErrorLog.Value.Add("Numbers cannot end on an underscore.");
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
                    ErrorLog.Value.Add("Underscores in numbers must have digits on both sides.");
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
                    ErrorLog.Value.Add("Underscores in numbers must have digits on both sides.");
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
                    ErrorLog.Value.Add("Underscores in numbers must have digits on both sides.");
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
                ErrorLog.Value.Add("Underscores in numbers must have digits on both sides.");
                Synchronize(LF);
                return;
            }

            underScore = c is '_';
            if (!underScore)
                vsb.Append(Reader.Read(useBuffer));
            else
                Reader.Read(useBuffer);
        }
    }


    private void ResolveNumberOrDateTime(ref ValueStringBuilder vsb)
    {

        Span<char> buffer = stackalloc char[4];
        int i = 0;

        for (; i < 4; i++)
        {
            if (i == 2 && Reader.Peek() == ':')
            {
                Reader.BufferFill(buffer, i + 1);
                BufferedTokenizeTimeOnly();
                return;
            }

            else if (!IsAsciiDigit(Reader.Peek()))
                break;

            else
                buffer[i] = Reader.Read();
        }

        if (Reader.Peek() == '-')
        {
            Reader.BufferFill(buffer, i + 1);
            TokenizeDateOrDateTime(ref vsb);
            return;
        }

        Reader.BufferFill(buffer, i);
        TokenizeNumber(ref vsb, true);
    }


    private void TokenizeNumber(ref ValueStringBuilder vsb, bool useBuffer = false)
    {
        char c = Reader.Read(useBuffer);

        bool? hasSign = c == '-' ? true : c == '+' ? false : null; //True -> -, False -> +, null -> no sign


        if (hasSign != null)//consume sign
            c = Reader.Read(useBuffer);


        if (c is '0') //Prefixed number leading zero check
        {
            switch (Reader.Peek(useBuffer))
            {
                case >= '0' and <= '9':
                    ErrorLog.Value.Add("Only prefixed numbers and exponents can contain leading zeros.");
                    Synchronize(LF);
                    return;

                case 'x' or 'b' or 'o' when hasSign is not null:
                    ErrorLog.Value.Add($"Prefixed numbers cannot have signs.");
                    Synchronize(LF);
                    return;

                case 'x':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Hex, useBuffer);
                    Add(new TInteger(vsb.RawChars[..vsb.Length]));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, TOMLTokenMetadata.Hex));
                    return;

                case 'b':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Binary, useBuffer);

                    Add(new TInteger(vsb.RawChars[..vsb.Length]));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, TOMLTokenMetadata.Binary));
                    return;

                case 'o':
                    TokenizeInteger(ref vsb, TOMLTokenMetadata.Octal, useBuffer);

                    Add(new TInteger(vsb.RawChars[..vsb.Length]));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, TOMLTokenMetadata.Octal));
                    return;

                case '.':
                    if (hasSign != null)
                        vsb.Append(hasSign is true ? '-' : '+');
                    vsb.Append('0');
                    break; //will go to the switch

                default:
                    //TokenStream.Enqueue(new(TomlTokenType.Integer,  ['0']));
                    Add(new TInteger(0));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex));
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
                //TokenStream.Enqueue(new(TomlTokenType.Integer,  vsb.RawChars));
                Add(new TInteger(vsb.RawChars[..vsb.Length]));
                TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, null));
                return;
            }
        }


        else if (c is 'i') //Infinity
        {
            if (!Reader.MatchNext('n', useBuffer) || !Reader.MatchNext('f', useBuffer))
            {
                ErrorLog.Value.Add("Expected literal 'inf'.");
                Synchronize(LF);
            }

            else
            {
                if (hasSign == true)
                    Add(new TFloat(double.NegativeInfinity));

                else
                    Add(new TFloat(double.PositiveInfinity));

                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, TOMLTokenMetadata.FloatInf));
            }

            return;
        }


        else if (c is 'n') //NaN
        {
            if (!Reader.MatchNext('a', useBuffer) || !Reader.MatchNext('n', useBuffer))
            {
                ErrorLog.Value.Add("Expected literal 'nan'.");
                Synchronize(LF);
            }

            else
            {

                Add(new TFloat(double.NaN));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, TOMLTokenMetadata.FloatNan));
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

                Add(new TFloat(vsb.RawChars[..vsb.Length]));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex));
                return;

            case 'E': //Exponent
            case 'e':
                TokenizeExponent(ref vsb, useBuffer);
                Add(new TFloat(vsb.RawChars[..vsb.Length]));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, TOMLTokenMetadata.FloatHasExponent));
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
            ErrorLog.Value.Add("Decimal points must have digits on both sides.");
            Synchronize(LF);
            return;
        }

        vsb.Append('.');

        TokenizeDecimal(ref vsb, useBuffer);  //The fractional part is "a decimal point followed by one or more digits."
    }


    private void TokenizeBool()
    {
        if (Reader.Peek() is 't')
        {
            Span<char> bufferT = stackalloc char[4];

            if (Reader.ReadBlock(bufferT) != 4 || bufferT is not "true")
            {
                ErrorLog.Value.Add($"Expected Boolean value 'true' but found {bufferT.ToString()}");
                Synchronize(LF);
                return;
            }
            Add(new TBool(true));
            TokenStream.Enqueue(new(TomlTokenType.Bool, ValueIndex));
            return;
        }

        Span<char> bufferF = stackalloc char[5];
        if (Reader.ReadBlock(bufferF) != 5 || bufferF is not "false")
        {
            ErrorLog.Value.Add($"Expected Boolean value 'false' but found {bufferF.ToString()}");
            Synchronize(LF);
            return;
        }

        Add(new TBool(false));
        TokenStream.Enqueue(new(TomlTokenType.Bool, ValueIndex));
        return;
    }


    private void TokenizeDateOrDateTime(ref ValueStringBuilder vsb)
    {
        Reader.Read();  //Consume '-' (The char is already checked before calling this method)
        TOMLTokenMetadata metadata = TOMLTokenMetadata.None;
        Span<char> buffer = stackalloc char[5];

        if (Reader.ReadBlock(buffer) != 5)
        {
            ErrorLog.Value.Add("Invalid date format.");
            Synchronize(LF);
            return;
        }

        if (!DateOnly.TryParseExact(buffer, "MM-dd", out var date))
        {
            ErrorLog.Value.Add("Invalid month and/or day.");
            Synchronize(LF);
            return;
        }

        date = new(int.Parse(Reader.GetBuffer()), date.Month, date.Day); //retrieve year from buffer


        if (!IsAsciiDigit(Reader.PeekSkip()) && !Reader.MatchNext('T'))
        {

            metadata |= TOMLTokenMetadata.DateOnly;


            Add(new TDateOnly(date));
            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, metadata));

            return;
        }

        TimeOnly time = TokenizeTimeOnly();

        //If no offset is provided, it will be set to +00:00 (AKA UTC).
        TimeSpan offset = TokenizeTimeOffset(ref metadata);


        if (metadata is TOMLTokenMetadata.Local)
            Add(new TDateTime(new(date, time)));
        else
            Add(new TDateTimeOffset(new(date, time, offset)));
        
        TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, metadata));
    }


    private TimeOnly BufferedTokenizeTimeOnly() //For timeonly, when input is partially buffered
    {
        //When control is passed to this method (and the input is valid),
        //the input contains the hour component, and the peek is ':'.

        Span<char> buffer = stackalloc char[5]; //mm:ss
        TOMLTokenMetadata metadata = TOMLTokenMetadata.TimeOnly;

        Reader.Read(); //Skip ':'

        if (Reader.ReadBlock(buffer) != 5)
        {
            ErrorLog.Value.Add("Invalid time format.");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }


        if (!TimeOnly.TryParseExact(buffer, "mm:ss", out var time))
        {
            ErrorLog.Value.Add("Invalid time value");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }

        time = time.AddHours(int.Parse(Reader.GetBuffer()));

        if (Reader.MatchNext(Dot))
            time = time.Add(TimeSpan.FromTicks(TokenizeFracSeconds()));



        Add(new TTimeOnly(time));
        TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, metadata));


        return time;

        //the actual result will be a datetimeoffset with a zeroed date when the toml date is done
    }


    private TimeOnly TokenizeTimeOnly()
    {
        //When control is passed to this method the delimiter (if any) should already be consumed
        Span<char> buffer = stackalloc char[8];

        if (Reader.ReadBlock(buffer) != 8)
        {
            ErrorLog.Value.Add("Invalid time format.");
            Synchronize(LF);
            return TimeOnly.MinValue;
        }


        if (!TimeOnly.TryParseExact(buffer, "hh:mm:ss", out var time))
        {
            ErrorLog.Value.Add("Invalid time value");
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
        bool isUnkownOffset = false;

        switch (Reader.Peek())
        {
            case 'Z':
                Reader.Read();
                break;

            case '-':
                isUnkownOffset = true;
                goto case '+';
            case '+':
                Span<char> buffer = stackalloc char[6];
                if (Reader.ReadBlock(buffer) != 6)
                {
                    ErrorLog.Value.Add("Invalid time offset format");
                    Synchronize(LF);
                }

                if (!TimeSpan.TryParse(buffer[0] is '+' ? buffer[1..] : buffer, out result)) //Because TryParse fails on '+'. However '-' is fine. (Don't ask why)
                {
                    ErrorLog.Value.Add("Invalid time offset");
                    Synchronize(LF);
                }
                break;

            default:
                metadata |= TOMLTokenMetadata.Local;
                break;
        }

        if (result == TimeSpan.Zero && isUnkownOffset) //The offset -00:00 is convention for unknown offsets
            metadata |= TOMLTokenMetadata.UnkownLocal;

        return result;
    }


    private int TokenizeFracSeconds()
    {
        int ticks = 0, i = 0;

        for (; i < 5 && IsAsciiDigit(Reader.Peek()); i++) //frac sec max prec is 6 digits
        {
            ticks *= 10;
            ticks += Reader.Read() - 0x30;
        }

        ticks *= (int)Math.Pow(10, 5 - i);

        //Console.WriteLine(ticks + "ns (" + (ticks / 100) + " ticks)");


        if (IsAsciiDigit(Reader.Peek()))
        {
            ErrorLog.Value.Add("Fractional seconds are only supported up to 5 digits, since 1 tick in .NET equals 100 nanoseconds. Value has been truncated.");
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
            ErrorLog.Value.Add($"No escape sequence for character '{initial}' (U+{(int)initial:X8}).");
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
        int charsRead = Reader.ReadBlock(buffer);

        if (charsRead < length) //It's definitely not good...
        {
            if (buffer[charsRead - 1] is DoubleQuote or EOF) //More likely error
            {
                --Reader.BaseReader.BaseStream.Position;
                ErrorLog.Value.Add($"Escape sequence '{buffer.ToString()}' is missing {5 - charsRead} digit(s).");
            }

            else//Generic error message
                ErrorLog.Value.Add($"Escape sequence '{buffer.ToString()}' must consist of {length} hexadecimal characters.");

            Synchronize(DoubleQuote);
            return -1;
        }

        int codePoint = 0;

        for (int i = 0; i < length; i++)
        {
            int digit = buffer[i] < 0x3A ? buffer[i] - 0x30 : (buffer[i] & 0x5F) - 0x37; //Convert char to hexadecimal digit

            if (digit < 0 || digit > 15)
            {
                ErrorLog.Value.Add($"{i + 1}. character '{buffer[i]}' in escape sequence is not a hexadecimal digit.");
                Synchronize(Space);
            }

            codePoint = (codePoint << 4) + digit; //Build up codepoint from digits
        }

        return codePoint;
    }


    private static bool IsBareKey(char c) => IsAsciiLetter(c) || IsAsciiDigit(c) || c is '-' or '_';


    private static bool IsKey(char c) => IsBareKey(c) || c is '"' or '\'';

}



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
