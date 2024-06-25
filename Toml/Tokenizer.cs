#define TOML_VER_1_1_0

global using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Toml.Runtime;
using static Toml.Extensions.TomlExtensions;
using static Toml.Tokenization.TOMLTokenMetadata;
using static System.Char;
using static Toml.Tokenization.Constants;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using System.Globalization;

#pragma warning disable IDE0290 //Primary ctors - just confusing looking, can be nice for some (barebones) exception classes.


namespace Toml.Tokenization;


public sealed class TOMLTokenizer
{
    internal TOMLStreamReader Reader { get; init; }

    public Queue<TOMLValue> TokenStream { get; init; }

    public Lazy<List<string>> ErrorLog { get; init; }

    internal List<TObject> Values { get; init; }

    private int ValueIndex = -1; //Tracks the last index of the list (like a stack).

    internal const int FracSec_MaxPrecisionDigits = 7;

    public TOMLTokenizer(Stream source, int capacity = 64) //most document are at or larger, this amount of overalloc is probably fine.
    {
        Reader = new(source);
        TokenStream = new(capacity);
        ErrorLog = new();
        Values = new(capacity);
    }


    public (Queue<TOMLValue>, List<TObject>) TokenizeFile()
    {
        try
        {
            while (Reader.Peek() is not EOF)
            {
                TokenizeTopLevelElement();
            }
        }

        catch (DecoderFallbackException)
        {
            throw new TomlReaderException($"{Reader.Position}: An invalid UTF-8 byte sequence was encountered.");
        }


        TokenStream.Enqueue(new(TomlTokenType.Eof, -1));


        Reader.Dispose(); //The parser does not need the original file. Without this, the stream would be open during parsing, which was annoying when trying to save a modification after it was read.
        
        return (TokenStream, Values);
    }


    private void AddObject(TObject obj)
    {

        Values.Add(obj);
        ValueIndex++;

        Debug.Assert(ValueIndex == Values.Count - 1, "ValueIndex is out of sync with list's count.");
    }


    private void SkipUntil(char syncChar)
    {
        while (Reader.Peek() != syncChar)
        {
            if (Reader.Read() is EOF)
                break;
        }
    }


    private void TokenizeTopLevelElement()
    {
        //Top level elements are either keys, table declarations, or comments.

        Reader.SkipWhiteSpace(skipLineEnding: true);


        if (IsKey(Reader.Peek()))
        {

            TokenizeKeyValuePair();


            AssertEOL();
            return;
        }

        int readResult = Reader.Read();


        switch (readResult)
        {
            case EOF: // Empty source file.
                return;

            case Comment:
                ConsumeComment();
                return;

            case SquareOpen:
                TokenizeTable();
                break;
        }

        AssertEOL();


        //  Debug.Assert(readResult != '\r' && readResult != '\n', $"Skipwhitespace failed to consume newline character {GetFriendlyNameFor(readResult)}");

        // ErrorLog.Value.Add($"Unexpected top level character: {GetFriendlyNameFor(readResult)}");
        //  SkipUntil(LF);
        return;


        void AssertEOL()
        {
            if (Reader.PeekSkip() is Comment)
            {
                ConsumeComment();
                return;
            }

            Reader.SkipWhiteSpace();

            if (!Reader.MatchLineEnding() && Reader.Peek() is not EOF)//we'll see about that "&&"
                ErrorLog.Value.Add($"{Reader.Position}: Found trailing characters from '{Reader.BaseReader.ReadLine()}'");
        }
    }


    private void ConsumeComment()
    {
        // Control is passed to this method when Tokenize() encounters a '#' character.
        // The '#' character is already consumed!

        int readResult;

        while (!Reader.MatchLineEnding())
        {
            if ((readResult = Reader.UncheckedRead()) == EOF)
                return;


            if (IsControl((char)readResult))
            {
                if (readResult is Tab or Space) //Only allowed control character.
                    continue;

                ErrorLog.Value.Add($"Found control character {readResult:X4} in comment. Tab is the only allowed control character inside comments.");
                SkipUntil(LF);
                return;
            }

            //Its valid to add at this point, but currently comments are not added to the DOM.
        }
    }


    private void TokenizeValue()
    {
        //Control is passed to this method from TokenizeArray() or TokenizeKeyValuePair()
        //No characters are consumed, including opening delimiters for strings.

        ValueStringBuilder buffer = new(stackalloc char[256]);

        int peekResult = Reader.PeekSkip();


        try
        {
            switch (peekResult)
            {
                case DoubleQuote:
                    ResolveBasicString(ref buffer);
                    return;

                case SingleQuote:
                    ResolveLiteralString(ref buffer);
                    return;

                case SquareOpen:
                    TokenizeArray();
                    return;

                case CurlyOpen:
                    TokenizeInlineTable();
                    return;

                case 't' or 'f':
                    TokenizeBool();
                    return;

                case >= '0' and <= '9':
                case 'i' or 'n':
                case '+' or '-':
                    TokenizeNumber(ref buffer);
                    return;

                case EOF:
                    ErrorLog.Value.Add($"{Reader.Position}: Expected a value to follow, but the end of the file was reached.");
                    break;

                default:
                    ErrorLog.Value.Add($"{Reader.Position}: Expected a TOML value, but no value can start with the character '{GetFriendlyNameFor(peekResult)}'");
                    break;
            }

            //Shared code for in case of error (EOF or default)
            SkipUntil(LF);
            return;
        }

        finally { buffer.Dispose(); }
    }



    private void TokenizeBool()
    {
        if (Reader.Peek() is 't')
        {
            Span<char> bufferT = stackalloc char[4];

            if (Reader.ReadBlock(bufferT) != 4 || bufferT is not "true")
            {
                if (string.Equals(bufferT.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                    ErrorLog.Value.Add($"{Reader.Position}: Boolean literal 'true' has invalid casing: '{bufferT.ToString()}'");

                ErrorLog.Value.Add($"{Reader.Position}: Expected Boolean value 'true' but got '{bufferT.ToString()}'");
                SkipUntil(LF);
                return;
            }

            AddObject(new TBool(true));
            TokenStream.Enqueue(new(TomlTokenType.Bool, ValueIndex));
            return;
        }

        Span<char> bufferF = stackalloc char[5];
        if (Reader.ReadBlock(bufferF) != 5 || bufferF is not "false")
        {
            if (string.Equals(bufferF.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                ErrorLog.Value.Add($"{Reader.Position}: Boolean literal 'false' has invalid casing: '{bufferF.ToString()}'");

            else
                ErrorLog.Value.Add($"{Reader.Position}: Expected Boolean value 'false' but got '{bufferF.ToString()}'");

            SkipUntil(LF);
            return;
        }

        AddObject(new TBool(false));
        TokenStream.Enqueue(new(TomlTokenType.Bool, ValueIndex));
        return;
    }


    #region Keys

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TokenizeKeyValuePair()
    {
        TokenizeKey(TomlTokenType.Key);


        if (!Reader.MatchNextSkip(KeyValueSeparator))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected separator '=' after key, but found '{GetFriendlyNameFor(Reader.Peek())}' instead.");
            SkipUntil(LF);
        }


        TokenizeValue();
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    /// <summary>
    /// <paramref name="keyType"/>: marks whether the key is part of a table or arraytable declaration, or a simple key.
    /// </summary>
    private void TokenizeKey(TomlTokenType keyType)
    {
        //Control is passed to this method from:
        //1. When a top-level key is found, passed as: Tokenize()->TokenizeKeyValuePair()->TokenizeKey().
        //2. TokenizeArrayTable(), after verifying both '[' characters.
        //3. TokenizeTable(), after verifying the '[' character.


        Reader.SkipWhiteSpace();


        if (Reader.Peek() is EOF)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected key but the end of file was reached");
            return;
        }


        /* 
           This part was overhauled to make parsing easier. In a dotted key, every fragment refers to a table,
           apart from the last, which can be a table, arraytable or key.
        
           So to make parsing (and code reuse) easier, every fragment in a dotted key is parsed as a table,
           apart from the last, which will be a normal key.
        
           This makes enforcing the no-redeclaration rule for tables and arraytables easier in the parser.
        */
        while (true)
        {
            //This call adds the key object to the value list. The structural token is added here.
            var resolvedMetadata = TokenizeKeyFragment();


            if (!Reader.MatchNextSkip(Dot)) //last fragment, always use target type.
            {
                TokenStream.Enqueue(new(keyType, ValueIndex, resolvedMetadata));
                break;
            }


            //these implicit tables cannot ever be redeclared (to avoid injection).
            if(keyType is TomlTokenType.Key)
                TokenStream.Enqueue(new(TomlTokenType.ImplicitKeyValueTable, ValueIndex, resolvedMetadata));
            

            //tables and arraytable implicit tables. Asked and confirmed over in toml-lang, these implicit tables can be redeclared (once). 
            else
                TokenStream.Enqueue(new(TomlTokenType.ImplicitHeaderTable, ValueIndex, resolvedMetadata));
        }
    }


    private TOMLTokenMetadata TokenizeKeyFragment()
    {
        //Control is passed to this method from TokenizeKey() for each key fragment.
        //EOF and whitespace is already checked and handled before calling this method.

        int peekResult = Reader.PeekSkip();


        if (peekResult is Dot)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Empty key fragment in dotted key");
            return None;
        }


        TOMLTokenMetadata fragmentType = peekResult switch
        {
            DoubleQuote => QuotedKey,
            SingleQuote => QuotedLiteralKey,
            _ => None,
        };

        ValueStringBuilder vsb = new(stackalloc char[256]);

        try
        {
            switch (peekResult)
            {
                case DoubleQuote:
                    _ = Reader.Read();
                    TokenizeBasicString(ref vsb);
                    break;

                case SingleQuote:
                    _ = Reader.Read();
                    TokenizeLiteralString(ref vsb);
                    break;

                default:
                    TokenizeBareKey(ref vsb);
                    break;
            }

            AddObject(new TFragment(vsb.ToString()));
            return fragmentType;
        }

        finally { vsb.Dispose(); }
    }


    private void TokenizeBareKey(ref ValueStringBuilder vsb)
    {
        int peekResult;

        while ((peekResult = Reader.Peek()) != EOF)
        {
            if (!IsBareKey((char)peekResult))
                break;

            vsb.Append((char)Reader.Read());
        }
    }


    private static bool IsBareKey(char c) => IsAsciiLetter(c) || IsAsciiDigit(c) || c is Dash or Underscore;


    private static bool IsKey(int c) => c == -1 ? false : IsBareKey((char)c) || c is DoubleQuote or SingleQuote;

    #endregion




    #region Collection Types

    /*Tables are the base of all TOML files, this method WILL get called. With only 1 callsite in a 
      relatively small method, it should definitely be inlined, is possible. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TokenizeTable()
    {
        //Control is passed to this method when Tokenize() encounters a '[' character
        //The '[' character is consumed!


        //Double '[' means an arraytable
        if (Reader.MatchNext(SquareOpen))
        {
            TokenizeArrayTable();
            return;
        }

        if (Reader.Peek() is SquareClose)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Empty table declaration.");
            SkipUntil(LF);
            return;
        }



        TokenStream.Enqueue(new(TomlTokenType.TableStart, -1));

        //Tokenize the table's key, and mark that it's part of a table declaration
        TokenizeKey(TomlTokenType.TableDecl);


        //Assert that the table declaration is terminated, log error then sync if not.
        if (!Reader.MatchNextSkip(SquareClose))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unterminated table declaration.");
            SkipUntil(LF);
            return;
        }
    }



    /*This code may be worth inlining aggressively, if possible, since arraytables
      seem to be relatively common. However, while the code here is smaller and simpler, 
      it uses methods that are currently set to inline aggresively, so the size in the IDE 
      is not really reprsentative.*/
    private void TokenizeArrayTable()
    {
        //Control is passed here when TokenizeTable() encounters a second '[' character.
        //Because TokenizeTable calls MatchNextSkip, the second '[' is already consumed.

        if (Reader.PeekSkip() is SquareClose)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Empty arraytable declaration.");
            SkipUntil(LF);
            return;
        }



        TokenStream.Enqueue(new(TomlTokenType.ArrayTableStart, -1));


        //Tokenize the arraytable's key, and mark that it's part of an arraytable declaration.
        TokenizeKey(TomlTokenType.ArrayTableDecl);


        if (!(Reader.MatchNextSkip(SquareClose) && Reader.MatchNext(SquareClose)))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unterminated arraytable declaration.");
            SkipUntil(LF);
            return;
        }
    }



    /*Too large to be worth inlining, even with aggresive inlining (IL limit removed). 
      Since tables and arrays are inlined into the same location, TokenizeValue(), it would 
      become huge, causing unnecessary JIT work, since there may not even be inline tables
      in the document itself (meaning unused code was compiled for no reason). */
    private void TokenizeArray()
    {
        //Consume opening square bracket.
        _ = Reader.Read();

        TokenStream.Enqueue(new(TomlTokenType.ArrayStart, -1));

        do
        {
            SkipWhiteSpaceAndComments();

            if (Reader.Peek() is SquareClose)
                break;

            TokenizeValue();

            SkipWhiteSpaceAndComments();

            if (!Reader.MatchNext(Comma) || Reader.Peek() == EOF) //Failsafe against infinite loop in the case of bad array syntax.
                break;

        } while (true);


        if (!Reader.MatchNext(SquareClose))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unterminated array");
            SkipUntil(LF);
        }

        else
            TokenStream.Enqueue(new(TomlTokenType.ArrayEnd, -1));


        void SkipWhiteSpaceAndComments()
        {
            do
            {
                Reader.SkipWhiteSpace(true);

                if (Reader.Peek() is Comment)
                    ConsumeComment();

            } while (Reader.Peek() is Space or Tab or Comment);
        }
    }



    private void TokenizeInlineTable()
    {
        //Consume opening curly bracket
        Debug.Assert(Reader.Peek() is CurlyOpen, "Incorrect usage, { was already consumed.");
        _ = Reader.Read();

        //Console.WriteLine("previous token: " + Values[^1]);


        TokenStream.Enqueue(new(TomlTokenType.InlineTableStart, -1));

        Reader.SkipWhiteSpace();


        //Short circuit on empty inline tables
        if (Reader.MatchNext(CurlyClose))
        {
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));
            return;
        }


        if (Reader.Peek() is Comma)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Empty inline tables cannot contain separators.");
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));
            SkipUntil(LF);
            return;
        }


        do
        {
            if (Reader.PeekSkip() is CurlyClose)
            {
                ErrorLog.Value.Add($"{Reader.Position}: Inline tables cannot contain trailing commas.");
                TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));
                SkipUntil(LF);
                return;
            }

            TokenizeKeyValuePair();

        } while (Reader.MatchNextSkip(Comma));


        if (Reader.MatchNext(CurlyClose))
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd, -1));


        else if (Reader.MatchLineEnding())
            ErrorLog.Value.Add($"{Reader.Position}: Inline tables must appear on a single line.");

        else
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected '}}' to terminate inline table, but found {GetFriendlyNameFor(Reader.Peek())}.");
            SkipUntil(LF);
        }
    }

    #endregion



    #region Strings

    private void ResolveBasicString(ref ValueStringBuilder buffer)
    {
        switch (Reader.MatchedCountOf(DoubleQuote))
        {
            case 1: //Single-line
                TokenizeBasicString(ref buffer);
                AddObject(new TString(buffer.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex));
                return;

            case 2: //Empty single-line
                AddObject(new TString(Span<char>.Empty));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex));
                return;

            case 6: //Empty multiline
                AddObject(new TString(Span<char>.Empty));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, Multiline));
                return;

            case >= 3: //Multiline
                TokenizeMultiLineBasicString(ref buffer);
                AddObject(new TString(buffer.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, Multiline));
                return;

            default: //Not possible, at least 1 quote exists if this method was called.
                Debug.Fail("Usage error; unexpected state, no case matched the count of quotes.");
                ErrorLog.Value.Add($"{Reader.Position}: Invalid number of opening doublequotes in basic string.");
                SkipUntil(LF);
                return;
        }
    }


    private void ResolveLiteralString(ref ValueStringBuilder buffer)
    {
        switch (Reader.MatchedCountOf(SingleQuote))
        {
            case 1: //Single-line
                TokenizeLiteralString(ref buffer);
                AddObject(new TString(buffer.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex));
                return;

            case 2: //Empty single-line
                AddObject(new TString(Span<char>.Empty));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex));
                return;

            case 6: //Empty multiline
                AddObject(new TString(Span<char>.Empty));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, Multiline));
                return;

            case >= 3: //Multiline
                TokenizeMultiLineLiteralString(ref buffer);
                AddObject(new TString(buffer.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.String, ValueIndex, Multiline));
                return;

            default: //Not possible, at least 1 quote exists if this method was called.
                Debug.Fail("Usage error; unexpected state, no case matched the count of quotes.");
                ErrorLog.Value.Add($"{Reader.Position}: Invalid number of opening singlequotes in literal string.");
                SkipUntil(LF);
                return;
        }
    }


    private void TokenizeBasicString(ref ValueStringBuilder vsb)
    {
        int c;
        while ((c = Reader.UncheckedRead()) is not (EOF or DoubleQuote))
        {
            switch (c)
            {
                case LF:
                    ErrorLog.Value.Add($"{Reader.Position}: Basic strings cannot span multiple lines.");
                    SkipUntil(LF);
                    return;

                case Backslash:
                    if (Reader.Peek() is Space)
                    {
                        ErrorLog.Value.Add($"{Reader.Position}: Only multiline strings can contain line ending backslashes.");
                        SkipUntil(LF);
                        return;
                    }

                    EscapeSequence(ref vsb);
                    break;

                case not Tab when IsControl((char)c):
                    ErrorLog.Value.Add($"{Reader.Position}: Found unescaped control character {GetFriendlyNameFor(c)} in basic string. Control characters other than Tab (U+0009) must be escaped.");
                    break;

                default:
                    vsb.Append((char)c);
                    break;
            }
        }

        if (c is not DoubleQuote)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unterminated string");
            SkipUntil(LF);
            return;
        }


        //The string itself is added in TokenizeValue() or as a key, DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated, and to avoid stack allocations in loops.
    }


    private void TokenizeMultiLineBasicString(ref ValueStringBuilder vsb)
    {
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        int c;

        while ((c = Reader.UncheckedRead()) != EOF)
        {
            switch (c)
            {
                case DoubleQuote:
                    if (TerminateMultiLineString(DoubleQuote, ref vsb))
                        return;
                    continue;

                case CR when Reader.MatchNext(LF): //Currently normalizes to LF line endings.
                    vsb.Append(LF);
                    break;

                case Backslash:
                    if (Reader.MatchLineEnding())
                    {
                        if (Reader.MatchLineEnding()) //line ending backslash found.
                        {
                            Reader.SkipWhiteSpace(skipLineEnding: true);
                            continue;
                        }
                    }

                    else if (IsWhiteSpace((char)Reader.Peek())) //could be line ending backslash
                    {
                        Reader.SkipWhiteSpace(); //consume all whitespace. line ending backslash IF \ is the last non-wp character.

                        if (Reader.MatchLineEnding()) //line ending backslash found.
                        {
                            Reader.SkipWhiteSpace(skipLineEnding: true);
                            continue;
                        }

                        else //unescaped backslash not last non-wp char, syntax error.
                        {
                            ErrorLog.Value.Add($"{Reader.Position}: Found unescaped '\\' in multiline basic string. Only tab, line feed and carriage return are allowed unescaped. " +
                                               $"If this is a line-ending backslash, make sure it is the last non-whitespace character on the line.");
                            SkipUntil(LF);
                            break;
                        }
                    }

                    else //Escape sequence (or a syntax error...)
                        EscapeSequence(ref vsb);
                    break;

                case not (Tab or LF) when IsControl((char)c):
                    ErrorLog.Value.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in multiline basic string. Only tab, line feed and carriage return are allowed unescaped.");
                    SkipUntil(LF);
                    break;

                default:
                    vsb.Append((char)c);
                    break;
            }
        }

        ErrorLog.Value.Add($"{Reader.Position}: Unterminated string");
        SkipUntil(LF);
        return;
    }


    private void TokenizeLiteralString(ref ValueStringBuilder vsb)
    {
        int c;
        while ((c = Reader.UncheckedRead()) is not (EOF or SingleQuote))
        {
            switch (c)
            {
                case LF:
                case CR when Reader.Peek() is LF:
                    ErrorLog.Value.Add($"{Reader.Position}: Literal strings cannot span multiple lines.");
                    SkipUntil(LF);
                    break;

                case not Tab when IsControl((char)c):
                    ErrorLog.Value.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in literal string. Only tab is allowed unescaped.");
                    SkipUntil(LF);
                    break;

                default:
                    vsb.Append((char)c);
                    break;
            }
        }


        if (c is not SingleQuote)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unterminated literal string");
            SkipUntil(LF);
            return;
        }

        //The string itself is added in TokenizeValue(), DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated.
    }


    private void TokenizeMultiLineLiteralString(ref ValueStringBuilder vsb)
    {
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        int c;

        while ((c = Reader.UncheckedRead()) != EOF)
        {
            switch (c)
            {
                case SingleQuote:
                    if (TerminateMultiLineString(SingleQuote, ref vsb))
                        return;
                    continue;

                case CR when Reader.MatchNext(LF): //Currently normalizes to LF line endings.
                    vsb.Append(LF);
                    break;

                case not (Tab or LF) when IsControl((char)c):
                    ErrorLog.Value.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in multiline literal string. Only tab is allowed unescaped.");
                    break;

                default:
                    vsb.Append((char)c);
                    break;
            }
        }

        ErrorLog.Value.Add($"{Reader.Position}: Unterminated string");
        SkipUntil(LF);
        return;
    }


    private bool TerminateMultiLineString(char separator, ref ValueStringBuilder vsb)
    {
        //The number of quotes plus the first that was already consumed.
        int matchResult = Reader.MatchedCountOf(separator) + 1;

        switch (matchResult)
        {
            case 1 or 4:
                vsb.Append(separator);
                break;

            case 2 or 5:
                vsb.Append(separator);
                vsb.Append(separator);
                break;

            case 3:
                break;

            default:
                ErrorLog.Value.Add($"{Reader.Position}: Too many separators ({separator}) in multiline string.");
                SkipUntil(LF);
                break;
        }

        return matchResult > 2;
    }

    #endregion





    #region Numerics

    private void TokenizeNumber(ref ValueStringBuilder vsb)
    {
        int c = Reader.Read();


        //Short circuit on bad input. This can happen when reading buffered input after a datetime resolve.
        if (c == EOF)
            return;

        bool? hasSign = c == '-' ? true : c == '+' ? false : null; //True -> -, False -> +, null -> no sign


        if (hasSign != null) //Consume sign
            c = Reader.Read();


        if (c is '0')
        {
            c = Reader.Peek();


            if ((uint)(c - '0') <= ('9' - '0'))
            {
                //small optimization, we can easily decide the type right here with a single branch,
                //then defer the error handling and reporting to the respective method.

                vsb.Append('0'); //previous char was 0
                vsb.Append((char)Reader.Read()); //current char is a digit too

                if (Reader.Peek() is Semicolon)
                {

                    var timeonly = TokenizeTimeOnly(vsb.RawChars[..Time_HourSeparator]); //also pass already buffered hours
                    AddObject(new TTimeOnly(timeonly));
                    TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, TOMLTokenMetadata.TimeOnly));
                }


                else
                {
                    vsb.Append((char)Reader.Read());
                    vsb.Append((char)Reader.Read());
                    TokenizeDateOrDateTime(ref vsb);
                    //this will return 0xFFFF if eof, but tryparseexact will fail on that so its handled.
                    //its a datetime, and the year is already in buffered.
                }
                return;
            }


            switch (c)
            {
                case 'x' or 'b' or 'o':
                    if (hasSign is not null)
                    {
                        ErrorLog.Value.Add($"Prefixed numbers cannot have signs. (Line {Reader.Line} Column {Reader.Column - 2})"); //subtract for 0 and prefix char to get sign position.
                        SkipUntil(LF);
                        return;
                    }

                    var formatType = GetFormatFor((char)c);

                    _ = Reader.Read(); //Consume prefix char.

                    if (Reader.Peek() is Underscore)
                    {
                        ErrorLog.Value.Add($"Syntax error: Found unit separator '_'  between a {formatType} number's first digit and its prefix. {Reader.Position}");
                        SkipUntil(LF);
                        return;
                    }


                    ResolveInteger(ref vsb, formatType);
                    AddObject(new TInteger(vsb.AsSpan(), formatType));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, formatType));
                    return;

                case '.':
                    if (hasSign != null)
                        vsb.Append(hasSign is true ? '-' : '+');
                    vsb.Append('0');
                    break;

                case 'e' or 'E':
                    vsb.Append('0');
                    _ = Reader.Read();
                    TokenizeFloatExponent(ref vsb);
                    AddObject(new TFloat(vsb.AsSpan()));
                    TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, FloatHasExponent));
                    return;

                default:
                    AddObject(new TInteger(0));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex));
                    return;
            }
        }


        else if (IsAsciiDigit((char)c)) //Decimal integer or intergral part
        {
            if (hasSign == true)
                vsb.Append('-'); //leading + is meaningless, so its discarded.


            vsb.Append((char)c); //Append the originally read character


            if (TokenizeDecimalInteger(ref vsb, hasSign is null)) //if it was a datetime, method is finished.
                return;


            if (Reader.Peek() is not ('.' or 'e' or 'E')) //Decimal integer
            {
                AddObject(new TInteger(vsb.AsSpan(), None, isNegative: hasSign is true));
                TokenStream.Enqueue(new(TomlTokenType.Integer, ValueIndex, null));
                return;
            }
        }


        else if (c is 'i') //Infinity
        {   
            if (!Reader.MatchNext('n') || !Reader.MatchNext('f'))
            {
                ErrorLog.Value.Add($"{Reader.Position}: Expected literal 'inf'.");
                SkipUntil(LF);
            }

            else
            {
                if (hasSign is true)
                    AddObject(new TFloat(double.NegativeInfinity));

                else
                    AddObject(new TFloat(double.PositiveInfinity));

                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, FloatInf));
            }

            return;
        }


        else if (c is 'n') //NaN
        {
            if (!Reader.MatchNext('a') || !Reader.MatchNext('n'))
            {
                ErrorLog.Value.Add($"{Reader.Position}: Expected the literal 'nan', but some characters are missing or invalid.");
                SkipUntil(LF);
            }

            //could add a warning or info that the sign of a nan value will not be preserved.

            else
            {
                AddObject(new TFloat(double.NaN));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, FloatNan));
            }

            return;
        }


        switch (Reader.Read()) //Float
        {
            case '.':
                TokenizeFloatFractional(ref vsb);

                if (Reader.Peek() is 'e' or 'E') //An exponent part may follow a fractional part
                {
                    Reader.Read();
                    goto case 'e';
                }

                AddObject(new TFloat(vsb.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex));
                return;

            case 'E':
            case 'e':
                TokenizeFloatExponent(ref vsb);
                AddObject(new TFloat(vsb.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.Float, ValueIndex, FloatHasExponent));
                return;
        }


        //This will never be inlined regardless of IL size limit (afaik),
        //because inlining is not supported for method bodies with 'complicated control flows', like in this case, switches.
        static TOMLTokenMetadata GetFormatFor(char c)
        {
            Debug.Assert(c is 'x' or 'b' or 'o', "Usage error; method called with an invalid format specifier");

            //See debug assertion. User input cannot cause this issue, only a tokenizer bug. Also, IDE0055 to disable VS auto indent removal. I get that it's scopeless, but it's also ugly.
            #pragma warning disable IDE0055, CS8509 
            return c switch
            {
                'x' => Hex,
                'b' => Binary,
                'o' => Octal,
            };
            #pragma warning restore CS8509
        }
    }


    private void ResolveInteger(ref ValueStringBuilder vsb, TOMLTokenMetadata format)
    {
        int c;
        bool previousWasDigit = true;


        TokenizePrefixedNumber(format, ref vsb);


        if (vsb.AsSpan().Length == 0)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Invalid integer.");
            SkipUntil(LF);
            return;
        }

        void TokenizePrefixedNumber(TOMLTokenMetadata format, ref ValueStringBuilder vsb)
        {
            Func<int,bool> isDigit = format switch
            {
                Hex    => IsHexadecimalDigit,
                Binary => IsBinaryDigit,
                Octal  => IsOctalDigit,
                _      => throw new TomlInternalException($"Invalid format specifier '{format}' provided to [{nameof(TokenizePrefixedNumber)}]")
            };


            while(isDigit(c = Reader.Peek()) || c is Underscore)
            {
                if (Reader.MatchNext(Underscore))
                {
                    if (!previousWasDigit)
                    {
                        ErrorLog.Value.Add($"{Reader.Position}: Underscores in numbers must have digits on both sides.");
                        SkipUntil(LF);
                        return;
                    }

                    previousWasDigit = false;
                    continue;
                }

                vsb.Append((char)c);
                _ = Reader.Read();
                previousWasDigit = true;
            }

           
            if (!previousWasDigit)
            {
                ErrorLog.Value.Add($"{Reader.Position} Numbers cannot end on an underscore.");
                SkipUntil(LF);
                return;
            }
        }
        

        static bool IsOctalDigit(int c) => c is not EOF && (uint)(c - '0') <= ('7' - '0');
        static bool IsBinaryDigit(int c) => c is '1' or '0';
        static bool IsHexadecimalDigit(int c) => c is not EOF && IsAsciiHexDigit((char)c);
    }


    //bool return: if true, there was a succesful promotion; otherwise it's an integer.
    private bool TokenizeDecimalInteger(ref ValueStringBuilder vsb, bool canPromote)
    {
        int c;
        bool previousWasDigit = true;

        while ((c = Reader.Peek()) != EOF)
        {
            if ((uint)(c - '0') <= ('9' - '0'))
            {
                vsb.Append((char)Reader.Read());
                previousWasDigit = true;
            }

            else if (Reader.MatchNext(Underscore))
            {
                canPromote = false;

                if (!previousWasDigit)
                {
                    ErrorLog.Value.Add("Underscores in numbers must have digits on both sides.");
                    SkipUntil(LF);
                    return true;
                }

                previousWasDigit = false;
            }


            else if (c is Dash)
            {
                PromoteToDate(ref vsb);
                return true;
            }


            else if (c is Semicolon)
            {
                PromoteToTime(ref vsb);
                return true;
            }

            else
                break;
        }

        if (!previousWasDigit)
        {
            ErrorLog.Value.Add("Numbers cannot end on an underscore.");
            SkipUntil(LF);
        }


        return false;

        void PromoteToDate(ref ValueStringBuilder vsb)
        {
            if (canPromote)
            {
                if (vsb.Length != 4)
                {
                    ErrorLog.Value.Add("RFC3339 timestamps can only represent years between 0 and 9999, and must be exactly 4 digits long.");
                    SkipUntil(LF);
                    return;
                }

                TokenizeDateOrDateTime(ref vsb);
            }

            else
            {
                ErrorLog.Value.Add("Invalid position of '-' in number.");
                SkipUntil(LF);
                return;
            }
        }

        void PromoteToTime(ref ValueStringBuilder vsb)
        {
            if (canPromote)
            {
                if (vsb.Length != 2)
                {
                    ErrorLog.Value.Add("RFC3339 timestamps can only represent hours between 0 and 23, and must be exactly 2 digits long.");
                    SkipUntil(LF);
                    return;
                }

                var timeonly = TokenizeTimeOnly(vsb.RawChars[..Time_HourSeparator]);
                AddObject(new TTimeOnly(timeonly));
                TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, TOMLTokenMetadata.TimeOnly));
            }

            else
            {
                ErrorLog.Value.Add("Invalid position of ':' in number. Make sure the hours in your timestamps are exactly 2 digits long.");
                SkipUntil(LF);
                return;
            }
        }
    }


   
    private void TokenizeFloatExponent(ref ValueStringBuilder vsb)
    {
        //Exponent character should already be consumed when control is passed to this method!
        vsb.Append('e');

        int peekResult = Reader.Peek();

        if (peekResult == EOF)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected an exponent, but the end of the file was reached.");
            SkipUntil(LF);
            return;
        }

        if(peekResult is Underscore)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Unit separators are not allowed between an exponent character and its first digit.");
            SkipUntil(LF);
            return;
        }

        if (peekResult is '-' or '+')
            vsb.Append((char)Reader.Read());


        //The exponent part "follows the same rules as decimal integer values but may include leading zeroes."
        TokenizeDecimalInteger(ref vsb, false);
        return;
    }


    private void TokenizeFloatFractional(ref ValueStringBuilder vsb)
    {
        //Dot should already be consumed when control is passed to this method!
        int peekResult = Reader.Peek();

        if (peekResult is EOF)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected the fractional part of a number, but the end of the file was reached.");
            SkipUntil(LF);
            return;
        }


        if (!IsAsciiDigit((char)peekResult))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Decimal points must have digits on both sides.");
            SkipUntil(LF);
            return;
        }

        vsb.Append('.');

        TokenizeDecimalInteger(ref vsb, false);  //The fractional part is "a decimal point followed by one or more digits."
    }


    #endregion


    #region Date and Time

    private void TokenizeDateOrDateTime(ref ValueStringBuilder vsb)
    {
        Reader.Read();  //Consume '-' (The char is already matched before calling this method, but not consumed for consistency with other code on callsite.)
        TOMLTokenMetadata metadata = None;
        Span<char> buffer = stackalloc char[5];


        if (Reader.ReadBlock(buffer) != 5)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Invalid date format.");
            SkipUntil(LF);
            return;
        }

        if (!System.DateOnly.TryParseExact(buffer, "MM-dd", out var date))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Invalid month and/or day.");
            SkipUntil(LF);
            return;
        }

        try
        {
            date = new(int.Parse(vsb.RawChars[..4], CultureInfo.InvariantCulture), date.Month, date.Day);
        }

        catch (ArgumentOutOfRangeException)
        {
            ErrorLog.Value.Add($"{Reader.Position}: File contains an invalid date. Year: {vsb.RawChars[..4]} Month: {date.Month} Day: {date.Day}");
            SkipUntil(LF);
            return;
        }

        int peekResult = Reader.Peek();

        if (MatchDateTimeSeparator(peekResult))
        {
            var time = TokenizeTimeOnly(Span<char>.Empty);

            /*If no offset is provided, it will be TimeSpan.Zero. Since UTC is also zero, ambiguity could arise between
              a datetime with an offset of UTC, and a datetime with no offset, which is why the metadata is used to decide that.*/
            var offset = TokenizeTimeOffset(ref metadata);


            if (metadata is TOMLTokenMetadata.Local)
                AddObject(new TDateTime(new(date, time, DateTimeKind.Local)));

            else
                AddObject(new TDateTimeOffset(new(date, time, offset)));

            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, metadata));
        }

        else
        {
            metadata |= TOMLTokenMetadata.DateOnly;

            AddObject(new TDateOnly(date));
            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, ValueIndex, metadata));

            return;
        }


        bool MatchDateTimeSeparator(int c)
        {
            if (c is EOF)
                return false;

            if (c is 't' or 'T')
            {
                _ = Reader.Read();
                return true;
            }

            return Reader.MatchNext(Space) && IsAsciiDigit((char)Reader.Peek());
        }
    }



    private TimeOnly TokenizeTimeOnly(Span<char> hours)
    {
        Span<char> buffer = stackalloc char[Time_Length];


        if (hours.IsEmpty) //Hours not yet read.
        {
            if (!Reader.TryRead(out char h1) || !Reader.TryRead(out char h2))
            {
                ErrorLog.Value.Add($"{Reader.Position}: Invalid hour component");
                goto ReturnError;
            }

            buffer[Time_H1] = h1;
            buffer[Time_H2] = h2;
        }

        else //Hours already buffered.
        {
            buffer[Time_H1] = hours[0];
            buffer[Time_H2] = hours[1];
        }


        if (!Reader.MatchNext(Semicolon))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Expected separator ':' between hours and minutes in timestamp.");
            goto ReturnError;
        }


        buffer[Time_HourSeparator] = ':'; //Consume and add the ':'

        int readResult;

        for (int i = Time_HourSeparator + 1; i < Time_Length; ++i) //mm:ss
        {
            readResult = Reader.Read();

            if (readResult == Semicolon)
            {
                if (i != Time_MinSeparator)
                {
                    ErrorLog.Value.Add($"{Reader.Position}: Expected separator ':' between minutes and seconds in timestamp.");
                    goto ReturnError;
                }
                else
                    buffer[i] = (char)readResult;
            }

            else if ((uint)(readResult - '0') > ('9' - '0'))
            {
                ErrorLog.Value.Add($"{Reader.Position}: Unexpected character '{GetFriendlyNameFor(readResult)}'. Local times may only consist of digits between 0 and 9.");
                goto ReturnError;
            }

            else
                buffer[i] = (char)readResult;
        }


        /* Regarding tick calculation
          We cannot calculate the result in ticks directly, because that would bypass validation
          of the individual components in a timestamp (see checks below).
          
          The reason we still calculate the result in ticks afterwards, instead of calling eg. the 
          ctor TimeSpan(int hour,int min, int sec), is fractional seconds. 
          
          Right now, they are supported up to 7 digits, at a 100-nanosecond resolution, which is 
          the .NET DateTime max. However, since no ctor actually has nanoseconds as a parameter, apart 
          from directly constructing from ticks, we stay with ticks apart from validation.
           
          The calculations for hour,minute and second do make the tick calculation a bit cleaner though.
          That, and the ctor using ticks is short, with only one comparison. There's also a ctor without
          the check using a direct assignment, but it's internal, so no dice.
        */

        int hour =   (buffer[Time_H1] - AsciiNumOffset) * 10 + (buffer[Time_H2] - AsciiNumOffset);
        int minute = (buffer[Time_M1] - AsciiNumOffset) * 10 + (buffer[Time_M2] - AsciiNumOffset);
        int second = (buffer[Time_S1] - AsciiNumOffset) * 10 + (buffer[Time_S2] - AsciiNumOffset);


        //There's a problem with this implementation. Only an UTC end of month may have a leap second.
        //This is not checked, because leap seconds are truncated, which in turn is because of DateTime not supporting it.
        //A parser should optimize for the common case, and since leap seconds are on the verge of being
        //obsoleted by lobbying from big tech, I don't feel like wasting more branches on this would be very beneficial.
        if(second == 60)
        {
            ErrorLog.Value.Add($"{Reader.Position}: [WARNING] .NET does not support leap seconds. End of month was not validated. Seconds set back to 59.");
            --second;
        }


        if ((uint)hour > 23 || (uint)minute > 59 || (uint)second > 59)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Invalid timestamp '{hour}:{minute}:{second}'.");
            goto ReturnError;
        }


        long resultInTicks = hour * TimeSpan.TicksPerHour +
                             minute * TimeSpan.TicksPerMinute +
                             second * TimeSpan.TicksPerSecond;


        if (Reader.MatchNext(Dot))
            resultInTicks += TokenizeFractionalSeconds();


        if (resultInTicks > TimeSpan.TicksPerDay - 1) //Avoid throw from .NET ctor and report as tokenizer error instead.
        {
            ErrorLog.Value.Add($"{Reader.Position}: Invalid local time; represented value is greater than 23:59:59.");
            goto ReturnError;
        }


        TimeOnly result = new(ticks: resultInTicks);

        return result;

    ReturnError:
        SkipUntil(LF);
        return System.TimeOnly.MinValue;
    }

   
    private TimeSpan TokenizeTimeOffset(ref TOMLTokenMetadata metadata)
    {
        TimeSpan result = TimeSpan.Zero;

        switch (Reader.Peek())
        {
            case 'Z' or 'z': //Timespan.Zero is already UTC
                Reader.Read();
                return result;

            case '-':
                goto case '+';

            case '+':
                Span<char> buffer = stackalloc char[TimeOffset_Length]; //+XX:XX
                if (Reader.ReadBlock(buffer) != TimeOffset_Length)
                {
                    ErrorLog.Value.Add($"{Reader.Position}: Invalid time offset format");
                    SkipUntil(LF);
                }

                //TryParse does not accept '+' for positive offsets. Negatives are handled fine.
                if (!TimeSpan.TryParse(buffer[0] is '+' ? buffer[1..] : buffer, out result))
                {
                    ErrorLog.Value.Add($"{Reader.Position}: Invalid time offset");
                    SkipUntil(LF);
                }
                break;

            default: //No offset; local datetime.
                metadata |= Local;
                return result;
        }

        if (result == TimeSpan.Zero) //The offset -00:00 is convention for unknown offsets.
            metadata |= UnkownLocal;

        return result;
    }

    //returns the number of digits parsed. assigns the value in ticks in 'resultInTicks'
    private int TokenizeFractionalSeconds()
    {
        /*For reference, 1 .NET tick is 100ns. We read digits left to right, multiply the current
          digit with the coefficient, then decrease the coefficient for the next digit.*/
        int i = 0, ticks = 0;
        int coeff = 1_000_000;

        /*Milliseconds: digits 0,1,2
          Microseconds: digits 3,4,5
          Nanoseconds:  digits 6,7,8*/

        int peekResult;

        for (; i < FracSec_MaxPrecisionDigits + 1 && (peekResult = Reader.Peek()) != EOF && IsAsciiDigit((char)peekResult); i++)
        {
            if (i == FracSec_MaxPrecisionDigits)
            {
                // ErrorLog.Value.Add($"Fractional seconds are only supported up to {FracSec_MaxPrecisionDigits} digit precision; value was truncated.");

                //Truncate any additional digits, as per spec
                do
                {
                    Reader.Read();
                } while ((peekResult = Reader.Peek()) != EOF && IsAsciiDigit((char)peekResult));

                return ticks;
            }

            ticks += (Reader.Read() - AsciiNumOffset) * coeff;
            coeff /= 10;
        }


        if (i == 0)
        {
            ErrorLog.Value.Add($"{Reader.Position}: Fractional second specifier '.' must be followed by at least one digit.");
            return 0;
        }

        return ticks;
    }

    #endregion




    #region Escape Sequence

    private void EscapeSequence(ref ValueStringBuilder vsb)
    {
        int initial = Reader.Read();

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

        
        int result = initial switch
        {
            'b' => '\u0008',
#if TOML_VER_1_1_0
            'e' => '\u001B',
#endif
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
            ErrorLog.Value.Add($"{Reader.Position}: No escape sequence for character '{GetFriendlyNameFor(initial)}' (U+{initial:X8}).");
            SkipUntil(LF);
            return;
        }

        vsb.Append((char)result);
    }


    private void UnicodeLongForm(ref ValueStringBuilder vsb)
    {
        //When control is passed to this method, only the numeric sequence remains (without '\' and 'U')
        int codePoint = ToUnicodeCodepoint(8);


        if (!IsUnicodeScalar(codePoint))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Found non-scalar or out of range Unicode codepoint U+{codePoint:X8} in escape sequence. Only scalar values may be escaped.");
            return;
        }


        //UTF-32 escape sequence, encode to surrogate pair.
        if (codePoint > MaxValue)
        {
            codePoint -= Plane1Start;
            vsb.Append((char)((codePoint >> 10) + HighSurrogateStart));
            vsb.Append((char)((codePoint & HighSurrogateRange) + LowSurrogateStart));

            Debug.Assert(IsSurrogatePair(vsb.AsSpan()[^2], vsb.AsSpan()[^1]));

            return;
        }

        vsb.Append((char)codePoint);
        return;
    }


    private void UnicodeShortForm(ref ValueStringBuilder vsb)
    {
        int codePoint = ToUnicodeCodepoint(4);

        if (!IsUnicodeScalar(codePoint))
        {
            ErrorLog.Value.Add($"{Reader.Position}: Found non-scalar or out of range Unicode codepoint U+{codePoint:X4} in escape sequence. Only scalar values may be escaped.");
            return;
        }

        vsb.Append((char)codePoint);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUnicodeScalar(int codePoint) => (uint)codePoint < ValidCodepointEnd &&
    /*Eg. valid codepoints that are not surrogates*/    !((uint)(codePoint - HighSurrogateStart) <= LowSurrogateEnd - HighSurrogateStart);


    private int ToUnicodeCodepoint(int digits)
    {
        Span<char> buffer = stackalloc char[digits];
        int charsRead = Reader.ReadBlock(buffer);

        if (charsRead < digits) //It's definitely not good...
        {
            if (buffer[charsRead - 1] is DoubleQuote or Null) //More likely error
                ErrorLog.Value.Add($"{Reader.Position}: Escape sequence '{buffer.ToString()}' is missing {5 - charsRead} digit(s).");

            else
                ErrorLog.Value.Add($"{Reader.Position}: Escape sequence '{buffer.ToString()}' must consist of {digits} hexadecimal characters.");

            SkipUntil(LF);
            return -1;
        }

        int codePoint = 0;

        for (int i = 0; i < digits; i++)
        {
            //Convert char to hexadecimal digit
            int digit = buffer[i] < AsciiDigitEnd ? buffer[i] - AsciiNumOffset : (buffer[i] & AsciiUpperNormalizeMask) - AsciiHexNumOffset;

            if ((uint)digit > 15)
            {
                ErrorLog.Value.Add($"{Reader.Position}: {i + 1}. character '{buffer[i]}' in escape sequence is not a hexadecimal digit.");
                SkipUntil(Space);
            }

            //Build up codepoint from digits
            codePoint = (codePoint << 4) + digit;
        }

        return codePoint;
    }

    /* Added 0x80 check to conform to inconsistent TOML spec excluding
       non-ASCII control characters from the definition of 'control characters',
       in a document format specifically designed with UTF-8 in mind. */
    private static bool IsControl(char c) => c < AsciiControlEnd && char.IsControl(c);

#endregion
}
