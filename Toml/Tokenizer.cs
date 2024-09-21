//#define DISALLOW_LEAP_SEC
global using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Toml.Runtime;
using static Toml.Extensions.TomlExtensions;
using static Toml.Tokenization.TomlTokenMetadata;
using static System.Char;
using static Toml.Tokenization.Constants;
using Toml.Reader;
using System.Globalization;
using System.Reflection.PortableExecutable;


namespace Toml.Tokenization;


public ref struct TOMLTokenizer
{
    internal readonly TomlReader Reader { get; }

    private ValueStringBuilder _builder;

    public readonly Queue<TOMLValue> TokenStream { get; }

    public readonly List<TObject> Values { get; }

    internal readonly List<TComment>? Comments { get; }

    
    private int _valueIndex = -1; //Tracks the last index of the list (like a stack).

    private int _commentIndex = -1; //Same deal with comments

    public readonly List<string> ErrorLog { get; }

    private TomlCommentMode _commentMode;


    public TOMLTokenizer(ITomlReaderSource source, int capacity = 32, TomlCommentMode comments = TomlCommentMode.Validate) //most document are at or larger, this amount of overalloc is probably fine.
    {
        Reader = new(source);
        TokenStream = new(capacity);

        ErrorLog = new();

        Values = new(capacity);

        _builder = new(new char[256]);
        
        Comments = comments is TomlCommentMode.Store ? new() : null;
        _commentMode = comments;
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
            throw new TomlReaderException("An invalid UTF-8 byte sequence was encountered.", Reader.Line, Reader.Column);
        }


        TokenStream.Enqueue(new(TomlTokenType.Eof));


        return (TokenStream, Values);
    }


    private void AddObject(TObject obj)
    {
        Values.Add(obj);
        _valueIndex++;

        Debug.Assert(_valueIndex == Values.Count - 1, "ValueIndex is out of sync with list's count.");
    }

    
    private void SkipUntil(char syncChar)
    {
        while (Reader.Peek() is not LF)
            if (Reader.Read() is EOF)
                break;
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

            case CommentStart:
                ProcessComment(callsiteId: 1);//A standalone comment on a line of its own.
                return;

            case SquareOpen:
                TokenizeTable();
                break;
        }

        AssertEOL();

        return;
    }


    private void AssertEOL() //was previously local but not possible for ref structs
    {
        if (Reader.PeekSkip() is CommentStart)
        {
            ProcessComment(callsiteId: 2); //Any comment after a line of TOML
            return;
        }

        Reader.SkipWhiteSpace();

        if (!Reader.MatchLineEnding() && Reader.Peek() is not EOF)
            ErrorLog.Add($"{Reader.Position}: Found trailing characters starting from {Reader.Position}");
    }



    private void ProcessComment(int callsiteId)
    {
        // Control is passed to this method when Tokenize() encounters a '#' character.
        // The '#' character is already consumed.

        _builder.ResetPointer();

        switch (_commentMode)
        {
            case TomlCommentMode.Validate:
                ValidateComment();
                return;

            case TomlCommentMode.Store:
                StoreComment(callsiteId);
                return;

            case TomlCommentMode.Skip:
                while (Reader.UncheckedRead() is not LF or EOF) ;
                return;
        }
    }


    private void ValidateComment()
    {
        int readResult;

        while (!Reader.MatchLineEnding())
        {
            if ((readResult = Reader.UncheckedRead()) is EOF)
                return;


            if (IsControl((char)readResult))
            {
                if (readResult is Tab or Space)
                    continue;

                ErrorLog.Add($"Found control character {GetFriendlyNameFor(readResult)} in comment. Tab is the only allowed control character inside comments.");
                SkipUntil(LF);
                return;
            }
        }
    }


    private void StoreComment(int callsiteId)
    {
        int readResult;

        while (!Reader.MatchLineEnding())
        {
            if ((readResult = Reader.UncheckedRead()) is EOF)
                break;

            if (IsControl((char)readResult) && readResult is not Tab or Space)
            {
                ErrorLog.Add($"Found control character {GetFriendlyNameFor(readResult)} in comment. Tab is the only allowed control character inside comments.");
                SkipUntil(LF);
                return;
            }

            _builder.Append((char)readResult);
        }


        //Comment on top of document, nothing to attach to.
        if (TokenStream.Count is 0)
        {   
            Comments!.Add(new TComment(_builder.AsSpan(), -2, true)!); //-2 marks a noattach comment.
            _commentIndex++; //Counting must still go up else it becomes out of sync.
        }


        else
        {
            Comments!.Add(new TComment(_builder.AsSpan(), _commentIndex++)!);
            Debug.Assert(_commentIndex == Comments.Count - 1, "CommentIndex is out of sync with list's count");


            if (callsiteId is 1) //attach as below
                Values[Values.Count - 1].CommentBelow = _commentIndex;
            
            else //callsites 2 and 3; attach same line
                Values[Values.Count - 1].CommentSameLine = _commentIndex;
        }


        _builder.ResetPointer(); //comment added, buffer can be marked for overwrite
    }


    private void TokenizeValue()
    {
        //Control is passed to this method from TokenizeArray() or TokenizeKeyValuePair()
        //No characters are consumed, including opening delimiters for strings.

        
        _builder.ResetPointer();


        int peekResult = Reader.PeekSkip();


        switch (peekResult)
        {
            case DoubleQuote:
                ResolveBasicString();
                return;

            case SingleQuote:
                ResolveLiteralString();
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

            case EOF:
                ErrorLog.Add($"{Reader.Position}: Expected a value to follow, but the end of the file was reached.");
                break;

            default:
                if(IsAsciiDigit((char)peekResult) || peekResult is 'i' or 'n' or '+' or '-')
                {
                    TokenizeNumber();
                    return;
                }

                ErrorLog.Add($"{Reader.Position}: Expected a TOML value, but no value can start with the character '{GetFriendlyNameFor(peekResult)}'");
                break;
        }

        //Shared code for in case of error (EOF or default)
        SkipUntil(LF);
        return;
    }


    private void TokenizeBool()
    {
        if (Reader.Peek() is 't')
        {
            if (TryTokenizeTrue())
            {
                AddObject(new TBool(true));
                TokenStream.Enqueue(new(TomlTokenType.Bool, _valueIndex));
            }
        }

        else
        {
            if (TryTokenizeFalse())
            {
                AddObject(new TBool(false));
                TokenStream.Enqueue(new(TomlTokenType.Bool, _valueIndex));
            }
        }
    }


    private bool TryTokenizeTrue()
    {
        Span<char> bufferT = stackalloc char[4];

        if (Reader.ReadBlock(bufferT) is not 4 || bufferT is not "true")
        {   
            string invalidResult = bufferT.ToString();

            if (string.Equals(invalidResult, "false", StringComparison.OrdinalIgnoreCase))
                ErrorLog.Add($"{Reader.Position}: Found Boolean 'true' with invalid casing: '{invalidResult}'");

            ErrorLog.Add($"{Reader.Position}: Expected Boolean literal 'true' but got '{invalidResult}'");
            SkipUntil(LF);
            return false;
        }

        return true;
    }


    private bool TryTokenizeFalse()
    {
        Span<char> bufferF = stackalloc char[5];
        if (Reader.ReadBlock(bufferF) is not 5 || bufferF is not "false")
        {   
            string invalidResult = bufferF.ToString();
            if (string.Equals(invalidResult, "false", StringComparison.OrdinalIgnoreCase))
                ErrorLog.Add($"{Reader.Position}: Found Boolean 'false' with invalid casing: '{invalidResult}'");

            else
                ErrorLog.Add($"{Reader.Position}: Expected Boolean literal 'false' but got '{invalidResult}'");

            SkipUntil(LF);
            return false;
        }

        return true;
    }



    #region Keys

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TokenizeKeyValuePair()
    {
        TokenizeKey(TomlTokenType.Key);

        if (!Reader.MatchNextSkip(KeyValueSeparator))
        {
            ErrorLog.Add($"{Reader.Position}: Expected separator '=' after key, but found '{GetFriendlyNameFor(Reader.Peek())}' instead.");
            SkipUntil(LF);
        }


        TokenizeValue();
    }


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
            ErrorLog.Add($"{Reader.Position}: Expected key but the end of file was reached");
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
            TokenizeKeyFragment();


            if (!Reader.MatchNextSkip(Dot)) //last fragment, always use target type.
            {
                ((TKey)Values[Values.Count - 1]).IsDotted = false;
                TokenStream.Enqueue(new(keyType, _valueIndex));
                break;
            }

            TokenStream.Enqueue(new(keyType is TomlTokenType.Key ? 
                                               TomlTokenType.ImplicitKeyValueTable :             //these implicit tables cannot ever be redeclared (to avoid injection).
                                               TomlTokenType.ImplicitHeaderTable, _valueIndex)); //tables and arraytable implicit tables. These implicit tables can be redeclared (once).
        }
    }


    private void TokenizeKeyFragment()
    {
        //Control is passed to this method from TokenizeKey() for each key fragment.
        //EOF and whitespace is already checked and handled before calling this method.

        Reader.SkipWhiteSpace();
        int peekResult = Reader.Peek();


        if (peekResult is Dot)
        {
            ErrorLog.Add($"{Reader.Position}: Empty key fragment in dotted key");
            return;
        }


        TomlTokenMetadata fragmentType = peekResult switch
        {
            DoubleQuote => QuotedKey,
            SingleQuote => QuotedLiteralKey,
            _ => None,
        };


        _builder.ResetPointer();

        switch (peekResult)
        {
            case DoubleQuote:
                _ = Reader.Read();
                TokenizeBasicString();
                break;

            case SingleQuote:
                _ = Reader.Read();
                TokenizeLiteralString();
                break;

            default:
                TokenizeBareKey();
                break;
        }

        AddObject(new TKey(_builder.AsSpan(), true, fragmentType));
    }


    private void TokenizeBareKey()
    {
        int peekResult;

        while ((peekResult = Reader.Peek()) is not EOF && IsBareKey((char)peekResult))
        {
            //if (!IsBareKey((char)peekResult))
              //  break;

            _builder.Append((char)Reader.Read());
        }
    }


    private static bool IsBareKey(char c) => IsAsciiLetter(c) || IsAsciiDigit(c) || c is Dash or Underscore;


    private static bool IsKey(int c) => c is not EOF && (IsBareKey((char)c) || c is DoubleQuote or SingleQuote);

    #endregion



    #region Collection Types

    /*Tables are the base of all TOML files so this is almost always hot. Only 1 callsite in a 
      relatively small method, definitely inline if is possible. */
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
            ErrorLog.Add($"{Reader.Position}: Empty table declaration.");
            SkipUntil(LF);
            return;
        }


        TokenStream.Enqueue(new(TomlTokenType.TableStart));

        //Tokenize the table's key, and mark that it's part of a table declaration
        TokenizeKey(TomlTokenType.TableDecl);


        //Assert that the table declaration is terminated, log error then sync if not.
        if (!Reader.MatchNextSkip(SquareClose))
        {
            ErrorLog.Add($"{Reader.Position}: Unterminated table declaration.");
            SkipUntil(LF);
            return;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TokenizeArrayTable()
    {
        //Control is passed here when TokenizeTable() encounters a second '[' character.
        //Because TokenizeTable calls MatchNextSkip, the second '[' is already consumed.

        if (Reader.PeekSkip() is SquareClose)
        {
            ErrorLog.Add($"{Reader.Position}: Empty arraytable declaration.");
            SkipUntil(LF);
            return;
        }


        TokenStream.Enqueue(new(TomlTokenType.ArrayTableStart));


        //Tokenize the arraytable's key, and mark that it's part of an arraytable declaration.
        TokenizeKey(TomlTokenType.ArrayTableDecl);


        if (!(Reader.MatchNextSkip(SquareClose) && Reader.MatchNext(SquareClose)))
        {
            ErrorLog.Add($"{Reader.Position}: Unterminated arraytable declaration.");
            SkipUntil(LF);
            return;
        }
    }



    /*Too large to be worth inlining, even with aggresive inlining (IL limit removed). 
      Since tables and arrays are inlined into the same location, TokenizeValue(), it would 
      become huge, causing unnecessary JIT work, since there may not even be inline tables
      in the document, thus jitting unused code for no reason). */
    private void TokenizeArray()
    {
        //Consume opening square bracket.
        _ = Reader.Read();

        TokenStream.Enqueue(new(TomlTokenType.ArrayStart));

        do
        {
            SkipWhiteSpaceAndComments();

            if (Reader.Peek() is SquareClose)
                break;

            TokenizeValue();


            SkipWhiteSpaceAndComments();

            if (!Reader.MatchNext(Comma) || Reader.Peek() is EOF) //Failsafe against infinite loop in the case of bad array syntax.
                break;

        } while (true);


        if (!Reader.MatchNext(SquareClose))
        {
            ErrorLog.Add($"{Reader.Position}: Unterminated array");
            SkipUntil(LF);
        }

        else
            TokenStream.Enqueue(new(TomlTokenType.ArrayEnd));
    }

    private void SkipWhiteSpaceAndComments()
    {
        do
        {
            Reader.SkipWhiteSpace(true);

            if (Reader.Peek() is CommentStart)
                ProcessComment(callsiteId: 3); //A comment after line in an array spanning multiple lines.

        } while (Reader.Peek() is Space or Tab or CommentStart);
    }


    private void TokenizeInlineTable()
    {
        //Consume opening curly bracket
        Debug.Assert(Reader.Peek() is CurlyOpen, "Incorrect usage, { was already consumed.");
        _ = Reader.Read();

        //Console.WriteLine("previous token: " + Values[^1]);


        TokenStream.Enqueue(new(TomlTokenType.InlineTableStart));

        Reader.SkipWhiteSpace();


        //Short circuit on empty inline tables
        if (Reader.MatchNext(CurlyClose))
        {
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd));
            return;
        }


        if (Reader.Peek() is Comma)
        {
            ErrorLog.Add($"{Reader.Position}: Empty inline tables cannot contain separators.");
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd));
            SkipUntil(LF);
            return;
        }


        do
        {
            if (Reader.PeekSkip() is CurlyClose)
            {
                ErrorLog.Add($"{Reader.Position}: Inline tables cannot contain trailing commas.");
                TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd));
                SkipUntil(LF);
                return;
            }

            TokenizeKeyValuePair();

        } while (Reader.MatchNextSkip(Comma));


        if (Reader.MatchNext(CurlyClose))
            TokenStream.Enqueue(new(TomlTokenType.InlineTableEnd));


        else if (Reader.MatchLineEnding())
            ErrorLog.Add($"{Reader.Position}: Inline tables must appear on a single line.");

        else
        {
            ErrorLog.Add($"{Reader.Position}: Expected '}}' to terminate inline table, but found {GetFriendlyNameFor(Reader.Peek())}.");
            SkipUntil(LF);
        }
    }

    #endregion



    #region Strings

    private void ResolveBasicString()
    {
        switch (Reader.MatchedCountOf(DoubleQuote))
        {
            case 1: //Single-line
                TokenizeBasicString();
                AddObject(new TString(_builder.AsSpan(), Basic));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case 2: //Empty single-line
                AddObject(new TString(Span<char>.Empty, Basic));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case 6: //Empty multiline
                AddObject(new TString(Span<char>.Empty, Multiline));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case >= 3: //Multiline
                TokenizeMultiLineBasicString();
                AddObject(new TString(_builder.AsSpan(), Multiline));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            default: //Not possible, at least 1 quote exists if this method was called.
                Debug.Fail("Usage error; unexpected state, no case matched the count of quotes.");
                ErrorLog.Add($"{Reader.Position}: Invalid number of opening doublequotes in basic string.");
                SkipUntil(LF);
                return;
        }
    }


    private void ResolveLiteralString()
    {
        switch (Reader.MatchedCountOf(SingleQuote))
        {
            case 1: //Single-line
                TokenizeLiteralString();
                AddObject(new TString(_builder.AsSpan(), Literal));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case 2: //Empty single-line
                AddObject(new TString(Span<char>.Empty, Literal));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case 6: //Empty multiline
                AddObject(new TString(Span<char>.Empty, MultilineLiteral));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            case >= 3: //Multiline
                TokenizeMultiLineLiteralString();
                AddObject(new TString(_builder.AsSpan(), MultilineLiteral));
                TokenStream.Enqueue(new(TomlTokenType.String, _valueIndex));
                return;

            default: //Not possible, at least 1 quote exists if this method was called.
                Debug.Fail("Usage error; unexpected state, no case matched the count of quotes.");
                ErrorLog.Add($"{Reader.Position}: Invalid number of opening singlequotes in literal string.");
                SkipUntil(LF);
                return;
        }
    }


    private void TokenizeBasicString()
    {
        int c;
        while ((c = Reader.UncheckedRead()) is not (EOF or DoubleQuote))
        {
            switch (c)
            {
                case LF:
                    ErrorLog.Add($"{Reader.Position}: Basic strings cannot span multiple lines.");
                    SkipUntil(LF);
                    return;

                case Backslash:
                    if (Reader.Peek() is Space)
                    {
                        ErrorLog.Add($"{Reader.Position}: Only multiline strings can contain line ending backslashes.");
                        SkipUntil(LF);
                        return;
                    }

                    EscapeSequence();
                    break;

                case not Tab when IsControl((char)c):
                    ErrorLog.Add($"{Reader.Position}: Found unescaped control character {GetFriendlyNameFor(c)} in basic string. Control characters other than Tab (U+0009) must be escaped.");
                    break;

                default:
                    _builder.Append((char)c);
                    break;
            }
        }

        if (c is not DoubleQuote)
        {
            ErrorLog.Add($"{Reader.Position}: Unterminated string");
            SkipUntil(LF);
            return;
        }


        //The string itself is added in TokenizeValue() or as a key, DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated, and to avoid stack allocations in loops.
    }


    private void TokenizeMultiLineBasicString()
    {
        //if(Reader.Peek() is CR or LF)
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        int c;

        while ((c = Reader.UncheckedRead()) is not EOF)
        {
            switch (c)
            {
                case DoubleQuote:
                    if (TerminateMultiLineString(DoubleQuote))
                        return;
                    continue;

                case CR when Reader.MatchNext(LF): //Currently normalizes to LF line endings.
                    _builder.Append(LF);
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
                            ErrorLog.Add($"{Reader.Position}: Found unescaped '\\' in multiline basic string. Only tab, line feed and carriage return are allowed unescaped. " +
                                               $"If this is a line-ending backslash, make sure it is the last non-whitespace character on the line.");
                            SkipUntil(LF);
                            break;
                        }
                    }

                    else //Escape sequence (or a syntax error...)
                        EscapeSequence();
                    break;

                case not (Tab or LF) when IsControl((char)c):
                    ErrorLog.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in multiline basic string. Only tab, line feed and carriage return are allowed unescaped.");
                    SkipUntil(LF);
                    break;

                default:
                    _builder.Append((char)c);
                    break;
            }
        }

        ErrorLog.Add($"{Reader.Position}: Unterminated string");
        SkipUntil(LF);
        return;
    }


    private void TokenizeLiteralString()
    {
        int c;
        while ((c = Reader.UncheckedRead()) is not (EOF or SingleQuote))
        {
            switch (c)
            {
                case LF:
                case CR when Reader.Peek() is LF:
                    ErrorLog.Add($"{Reader.Position}: Literal strings cannot span multiple lines.");
                    SkipUntil(LF);
                    break;

                case not Tab when IsControl((char)c):
                    ErrorLog.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in literal string. Only tab is allowed unescaped.");
                    SkipUntil(LF);
                    break;

                default:
                    _builder.Append((char)c);
                    break;
            }
        }


        if (c is not SingleQuote)
        {
            ErrorLog.Add($"{Reader.Position}: Unterminated literal string");
            SkipUntil(LF);
            return;
        }

        //The string itself is added in TokenizeValue(), DO NOT TRY TO ADD OR RETURN ANYTHING HERE.
        //This is so that only one ValueStringBuilder is allocated.
    }


    private void TokenizeMultiLineLiteralString()
    {
        Reader.MatchLineEnding(); //Skip newline if immediately after opening delimiter (as defined by spec)

        int c;

        while ((c = Reader.UncheckedRead()) is not EOF)
        {
            switch (c)
            {
                case SingleQuote:
                    if (TerminateMultiLineString(SingleQuote))
                        return;
                    continue;

                case CR when Reader.MatchNext(LF): //Currently normalizes to LF line endings.
                    _builder.Append(LF);
                    break;

                case not (Tab or LF) when IsControl((char)c):
                    ErrorLog.Add($"{Reader.Position}: Found control character {GetFriendlyNameFor(c)} in multiline literal string. Only tab is allowed unescaped.");
                    break;

                default:
                    _builder.Append((char)c);
                    break;
            }
        }

        ErrorLog.Add($"{Reader.Position}: Unterminated string");
        SkipUntil(LF);
        return;
    }


    private bool TerminateMultiLineString(char separator)
    {
        //The number of quotes plus the first that was already consumed.
        int matchResult = Reader.MatchedCountOf(separator) + 1;

        switch (matchResult)
        {
            case 1 or 4:
                _builder.Append(separator);
                break;

            case 2 or 5:
                _builder.Append(separator);
                _builder.Append(separator);
                break;

            case 3:
                break;

            default:
                ErrorLog.Add($"{Reader.Position}: Too many separators ({separator}) in multiline string.");
                SkipUntil(LF);
                break;
        }

        return matchResult > 2;
    }

    #endregion



    #region Numerics

    private void TokenizeNumber()
    {
        int c = Reader.Read();


        //Short circuit on bad input. This can happen when reading buffered input after a datetime resolve.
        if (c is EOF)
            return;

        bool? hasSign = c is '-' ? true : 
                        c is '+' ? false : 
                        null; //no sign


        if (hasSign is not null) //Consume sign
            c = Reader.Read();


        if (c is '0')
        {
            c = Reader.Peek();


            if ((uint)(c - '0') <= ('9' - '0'))
            {
                //small optimization, we can easily decide the type right here with a single branch,
                //then defer the error handling and reporting to the respective method.

                _builder.Append('0'); //previous char was 0
                _builder.Append((char)Reader.Read()); //current char is a digit too

                if (Reader.Peek() is Semicolon)
                {
                  //MODIFIED  var timeonly = TokenizeTimeOnly(_builder.RawChars[..Time_HourSeparator]); //also pass already buffered hours
                    var timeonly = TokenizeTimeOnly(_builder.RawChars.Slice(0, Time_HourSeparator));   
                    AddObject(new TTimeOnly(timeonly));
                    TokenStream.Enqueue(new(TomlTokenType.TimeStamp, _valueIndex));
                }


                else
                {
                    _builder.Append((char)Reader.Read());
                    _builder.Append((char)Reader.Read());
                    TokenizeDateOrDateTime();
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
                        ErrorLog.Add($"Prefixed numbers cannot have signs. (Line {Reader.Line} Column {Reader.Column - 2})"); //subtract for 0 and prefix char to get sign position.
                        SkipUntil(LF);
                        return;
                    }

#pragma warning disable CS8509 //Impossible, the switch case already matched against x,b,o
                    var formatType = c switch
                    {
                        'x' => Hex,
                        'b' => Binary,
                        'o' => Octal,
                    };
#pragma warning restore CS8509
                    _ = Reader.Read(); //Consume prefix char.

                    if (Reader.Peek() is Underscore)
                    {
                        ErrorLog.Add($"Syntax error: Found unit separator '_'  between a {formatType} number's first digit and its prefix. {Reader.Position}");
                        SkipUntil(LF);
                        return;
                    }


                    ResolvePrefixedInteger(formatType);
                    AddObject(new TInteger(_builder.AsSpan(), formatType));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, _valueIndex));
                    return;

                case '.':
                    if (hasSign is not null)
                        _builder.Append(hasSign is true ? '-' : '+');
                    _builder.Append('0');
                    break;

                case 'e' or 'E':
                    _builder.Append('0');
                    _ = Reader.Read();
                    TokenizeFloatExponent();
                    AddObject(new TFloat(_builder.AsSpan()));
                    TokenStream.Enqueue(new(TomlTokenType.Float, _valueIndex));
                    return;

                default:
                    AddObject(new TInteger(0, TomlTokenMetadata.Decimal));
                    TokenStream.Enqueue(new(TomlTokenType.Integer, _valueIndex));
                    return;
            }
        }


        else if (IsAsciiDigit((char)c)) //Decimal integer or intergral part
        {
            if (hasSign is true)
                _builder.Append('-'); //leading + is meaningless, so its discarded.


            _builder.Append((char)c); //Append the originally read character


            if (TokenizeDecimalInteger(hasSign is null)) //if it was a datetime, method is finished.
                return;


            if (Reader.Peek() is not ('.' or 'e' or 'E')) //Decimal integer
            {
                AddObject(new TInteger(_builder.AsSpan(), TomlTokenMetadata.Decimal, hasSign is true));
                TokenStream.Enqueue(new(TomlTokenType.Integer, _valueIndex));
                return;
            }
        }


        else if (c is 'i') //Infinity
        {
            if (!Reader.MatchNext('n') || !Reader.MatchNext('f'))
            {
                ErrorLog.Add($"{Reader.Position}: Expected literal 'inf'.");
                SkipUntil(LF);
            }

            else
            {
                if (hasSign is true)
                    AddObject(new TFloat(double.NegativeInfinity));

                else
                    AddObject(new TFloat(double.PositiveInfinity));

                TokenStream.Enqueue(new(TomlTokenType.Float, _valueIndex));
            }

            return;
        }


        else if (c is 'n') //NaN
        {
            if (!Reader.MatchNext('a') || !Reader.MatchNext('n'))
            {
                ErrorLog.Add($"{Reader.Position}: Expected the literal 'nan', but some characters are missing or invalid.");
                SkipUntil(LF);
            }

            //could add a warning or info that the sign of a nan value will not be preserved.

            else
            {
                var metadata = hasSign switch
                {
                    null => Nan,
                    true => NegativeNan,
                    false => PositiveNan,
                };

                AddObject(new TFloat(double.NaN, metadata));
                TokenStream.Enqueue(new(TomlTokenType.Float, _valueIndex));
            }

            return;
        }


        switch (Reader.Read()) //Float
        {
            case '.':
                TokenizeFloatFractional();

                if (Reader.Peek() is 'e' or 'E') //An exponent part may follow a fractional part
                {
                    Reader.Read();
                    goto case 'e';
                }

                AddObject(new TFloat(_builder.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.Float, _valueIndex));
                return;

            case 'E':
            case 'e':
                TokenizeFloatExponent();
                AddObject(new TFloat(_builder.AsSpan()));
                TokenStream.Enqueue(new(TomlTokenType.Float, _valueIndex));
                return;
        }


        //This will never be inlined regardless of IL size limit (afaik),
        //because inlining is not supported for anything other than basic if/then/else.
        static TomlTokenMetadata GetFormatFor(char c) => c switch
        {
            'x' => Hex,
            'b' => Binary,
            'o' => Octal,
            _ => throw new TomlInternalException($"Invalid format character {c} provided to {nameof(GetFormatFor)}"),
        };
    }


    //Wrapping with an unmanaged func pointer was benched to be somewhat faster
    //than wrapping in a normal Func<int,bool>.
    //Since this is all internal code it should be just as safe (or caught before release).
    //The best would be to inline it, but not worth the binary-size increase the refactor would likely cause.
    private unsafe void ResolvePrefixedInteger(TomlTokenMetadata format)
    {
        int c;
        bool previousWasDigit = true;


        delegate*<int, bool> isDigit = format switch
        {
            Hex    => &IsHexadecimalDigit,
            Binary => &IsBinaryDigit,
            Octal  => &IsOctalDigit,
            _ => throw new TomlInternalException($"Invalid format specifier '{format}' provided to [{nameof(ResolvePrefixedInteger)}]")
        };


        while (isDigit(c = Reader.Peek()) || c is Underscore)
        {
            if (Reader.MatchNext(Underscore))
            {
                if (!previousWasDigit)
                {
                    ErrorLog.Add($"{Reader.Position}: Underscores in numbers must have digits on both sides.");
                    SkipUntil(LF);
                    return;
                }

                previousWasDigit = false;
                continue;
            }

            _builder.Append((char)c);
            _ = Reader.Read();
            previousWasDigit = true;
        }


        if (!previousWasDigit)
        {
            ErrorLog.Add($"{Reader.Position} Numbers cannot end on an underscore.");
            SkipUntil(LF);
            return;
        }


        if (_builder.AsSpan().Length is 0)
        {
            ErrorLog.Add($"{Reader.Position}: Invalid integer.");
            SkipUntil(LF);
            return;
        }



        static bool IsOctalDigit(int c) => c is not EOF && (uint)(c - '0') <= ('7' - '0');
        static bool IsBinaryDigit(int c) => c is '1' or '0';
        static bool IsHexadecimalDigit(int c) => c is not EOF && IsAsciiHexDigit((char)c);
    }

    //bool return: if true, there was a succesful promotion; otherwise it's an integer.
    private bool TokenizeDecimalInteger(bool canPromote)
    {
        int c;
        bool previousWasDigit = true;


        while ((c = Reader.Peek()) is not EOF)
        {
            if (c is >= '0' and <= '9')
            {
                _builder.Append((char)Reader.Read());
                previousWasDigit = true;
            }


            else if (Reader.MatchNext(Underscore))
            {
                canPromote = false;

                if (!previousWasDigit)
                {
                    ErrorLog.Add("Underscores in numbers must have digits on both sides.");
                    SkipUntil(LF);
                    return true;
                }

                previousWasDigit = false;
            }


            else if (c is Dash)
            {
                TryPromoteToDate(canPromote);
                return true;
            }


            else if (c is Semicolon)
            {
                TryPromoteToTime(canPromote);
                return true;
            }

            else
                break;
        }

        if (!previousWasDigit)
        {
            ErrorLog.Add("Numbers cannot end on an underscore.");
            SkipUntil(LF);
        }


        return false;
    }

    private void TryPromoteToDate(bool canPromote)
    {
        if (canPromote)
        {
            if (_builder.Length is not 4)
            {
                ErrorLog.Add("RFC3339 timestamps can only represent years between 0 and 9999, and must be exactly 4 digits long.");
                SkipUntil(LF);
                return;
            }

            TokenizeDateOrDateTime();
        }

        else
        {
            ErrorLog.Add("Invalid position of '-' in number.");
            SkipUntil(LF);
            return;
        }
    }

    private void TryPromoteToTime(bool canPromote)
    {
        if (canPromote)
        {
            if (_builder.Length is not 2)
            {
                ErrorLog.Add("RFC3339 timestamps can only represent hours between 0 and 23, and must be exactly 2 digits long.");
                SkipUntil(LF);
                return;
            }

            var timeonly = TokenizeTimeOnly(_builder.RawChars.Slice(0, Time_HourSeparator));
            //var timeonly = TokenizeTimeOnly(_builder.RawChars[..Time_HourSeparator]);
            AddObject(new TTimeOnly(timeonly));
            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, _valueIndex));
        }

        else
        {
            ErrorLog.Add("Invalid position of ':' in number. Make sure the hours in your timestamps are exactly 2 digits long.");
            SkipUntil(LF);
            return;
        }
    }


    private void TokenizeFloatExponent()
    {
        //Exponent character should already be consumed when control is passed to this method!
        _builder.Append('e');

        int peekResult = Reader.Peek();

        if (peekResult is EOF)
        {
            ErrorLog.Add($"{Reader.Position}: Expected an exponent, but the end of the file was reached.");
            SkipUntil(LF);
            return;
        }

        if (peekResult is Underscore)
        {
            ErrorLog.Add($"{Reader.Position}: Unit separators are not allowed between an exponent character and its first digit.");
            SkipUntil(LF);
            return;
        }

        if (peekResult is '-' or '+')
            _builder.Append((char)Reader.Read());


        //The exponent part "follows the same rules as decimal integer values but may include leading zeroes."
        TokenizeDecimalInteger(false);
        return;
    }


    private void TokenizeFloatFractional()
    {
        //Dot should already be consumed when control is passed to this method!
        int peekResult = Reader.Peek();

        if (peekResult is EOF)
        {
            ErrorLog.Add($"{Reader.Position}: Expected the fractional part of a number, but the end of the file was reached.");
            SkipUntil(LF);
            return;
        }


        if (!IsAsciiDigit((char)peekResult))
        {
            ErrorLog.Add($"{Reader.Position}: Decimal points must have digits on both sides.");
            SkipUntil(LF);
            return;
        }

        _builder.Append('.');

        TokenizeDecimalInteger(false);  //The fractional part is "a decimal point followed by one or more digits."
    }


    #endregion



    #region Date and Time

    private void TokenizeDateOrDateTime()
    {
        _ = Reader.Read();  //Consume '-' (The char is already matched before calling this method, but not consumed for consistency with other code on callsite.)
        TomlTokenMetadata metadata = None;
        Span<char> buffer = stackalloc char[5];

        int tmp = 0;

        if ((tmp = Reader.ReadBlock(buffer)) is not 5)
        {
            ErrorLog.Add($"{Reader.Position}: Invalid date format.");
            SkipUntil(LF);
            return;
        }

        if (!System.DateOnly.TryParseExact(buffer, "MM-dd", out var date))
        {
            ErrorLog.Add($"{Reader.Position}: Invalid month and/or day.");
            SkipUntil(LF);
            return;
        }

        try
        {           //RawChars[..4]
            date = new(int.Parse(_builder.RawChars.Slice(0,4), CultureInfo.InvariantCulture), date.Month, date.Day);
        }

        catch (ArgumentOutOfRangeException)
        {
            ErrorLog.Add($"{Reader.Position}: File contains an invalid date. Year: {_builder.RawChars.Slice(0,4)} Month: {date.Month} Day: {date.Day}");
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


            if (metadata is TomlTokenMetadata.Local)
                AddObject(new TDateTime(new(date, time, DateTimeKind.Local)));

            else
                AddObject(new TDateTimeOffset(new(date, time, offset), metadata));


            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, _valueIndex));
        }

        else
        {
            AddObject(new TDateOnly(date));
            TokenStream.Enqueue(new(TomlTokenType.TimeStamp, _valueIndex));

            return;
        }
    }

    readonly bool MatchDateTimeSeparator(int c)
    {
        if (c is EOF)
            return false;

        if ((c & AsciiUpperNormalizeMask) is 'T')
        {
            _ = Reader.Read();
            return true;
        }

        return Reader.MatchNext(Space) && IsAsciiDigit((char)Reader.Peek());
    }



    private TimeOnly TokenizeTimeOnly(Span<char> hours)
    {
        Span<char> buffer = stackalloc char[Time_Length];


        if (hours.IsEmpty) //Hours not yet read.
        {
            if (!Reader.TryRead(out char h1) || !Reader.TryRead(out char h2))
            {
                ErrorLog.Add($"{Reader.Position}: Invalid hour component");
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
            ErrorLog.Add($"{Reader.Position}: Expected separator ':' between hours and minutes in timestamp.");
            goto ReturnError;
        }


        buffer[Time_HourSeparator] = ':'; //Consume and add the ':'

        int readResult;

        for (int i = Time_HourSeparator + 1; i < Time_Length; ++i) //mm:ss
        {
            readResult = Reader.Read();

            if (readResult is Semicolon)
            {
                if (i is not Time_MinSeparator)
                {
                    ErrorLog.Add($"{Reader.Position}: Expected separator ':' between minutes and seconds in timestamp.");
                    goto ReturnError;
                }
                else
                    buffer[i] = (char)readResult;
            }

            else if ((uint)(readResult - '0') > ('9' - '0'))
            {
                ErrorLog.Add($"{Reader.Position}: Unexpected character '{GetFriendlyNameFor(readResult)}'. Local times may only consist of digits between 0 and 9.");
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

        int hour   = (buffer[Time_H1] - AsciiNumOffset) * 10 + (buffer[Time_H2] - AsciiNumOffset);
        int minute = (buffer[Time_M1] - AsciiNumOffset) * 10 + (buffer[Time_M2] - AsciiNumOffset);
        int second = (buffer[Time_S1] - AsciiNumOffset) * 10 + (buffer[Time_S2] - AsciiNumOffset);


        //There's a problem with this implementation. Only an UTC end of month may have a leap second.
        //This is not checked, because leap seconds are truncated, which in turn is because of DateTime not supporting it.
        //A parser should optimize for the common case, and since leap seconds are on the verge of being
        //obsoleted by lobbying from big tech, I don't feel like wasting more branches on this would be very beneficial.
        if (second is 60)
        {
#if DISALLOW_LEAP_SEC
            ErrorLog.Value.Add($"{Reader.Position}: Leap seconds are disallowed in the parser's current configuration.");
#endif
            --second;
        }

        if ((uint)hour > 23 || (uint)minute > 59 || (uint)second > 59)
        {
            ErrorLog.Add($"{Reader.Position}: Invalid timestamp '{hour}:{minute}:{second}'.");
            goto ReturnError;
        }


        long resultInTicks = hour * TimeSpan.TicksPerHour +
                             minute * TimeSpan.TicksPerMinute +
                             second * TimeSpan.TicksPerSecond;


        if (Reader.MatchNext(Dot))
            resultInTicks += TokenizeFractionalSeconds();


        if (resultInTicks > TimeSpan.TicksPerDay - 1) //Avoid throw from .NET ctor and report as tokenizer error instead.
        {
            ErrorLog.Add($"{Reader.Position}: Invalid local time; represented value is greater than 23:59:59.");
            goto ReturnError;
        }


        TimeOnly result = new(ticks: resultInTicks);

        return result;

    ReturnError:
        SkipUntil(LF);
        return System.TimeOnly.MinValue;
    }


    private TimeSpan TokenizeTimeOffset(ref TomlTokenMetadata metadata)
    {
        TimeSpan result = TimeSpan.Zero;

        switch (Reader.Peek())
        {
            case 'Z' or 'z': //Timespan.Zero is already UTC
                Reader.Read();
                return result;

            case '-':
            case '+':
                Span<char> buffer = stackalloc char[TimeOffset_Length]; //+XX:XX
                if (Reader.ReadBlock(buffer) is not TimeOffset_Length)
                {
                    ErrorLog.Add($"{Reader.Position}: Invalid time offset format");
                    SkipUntil(LF);
                }

                //TryParse does not accept '+' for positive offsets. Negatives are handled fine.
                if (!TimeSpan.TryParse(buffer[0] is '+' ? buffer.Slice(1) : buffer, out result))
                {
                    ErrorLog.Add($"{Reader.Position}: Invalid time offset");
                    SkipUntil(LF);
                }
                break;

            default: //No offset; local datetime.
                metadata |= Local;
                return result;
        }

        if (result == TimeSpan.Zero) //The offset -00:00 is convention for unknown offsets.
            metadata |= UnknownLocal;

        return result;
    }


    private readonly int TokenizeFractionalSeconds()
    {
        /* For reference, 1 .NET tick is 100ns. We read digits left to right, multiply the current
           digit with the coefficient, then decrease the coefficient for the next digit. */
        int i = 0, ticks = 0;
        int coeff = 1_000_000;

        /* Milliseconds: digits 0,1,2
           Microseconds: digits 3,4,5
           Nanoseconds:  digits 6,7,8 */

        int peekResult;

        for (; i < FracSec_MaxPrecisionDigits + 1 
                   && (peekResult = Reader.Peek()) is not EOF 
                   && IsAsciiDigit((char)peekResult); i++)
        {
            if (i is FracSec_MaxPrecisionDigits)
            {
                ErrorLog.Add($"Fractional seconds are only supported up to {FracSec_MaxPrecisionDigits} digit precision; value was truncated.");

                //Truncate any additional digits, as per spec
                do
                {
                    Reader.Read();
                } while (IsAsciiDigit((char)Reader.Peek())); //-1 (EOF) will underflow to maxval (65535), which is out of range anyways

                return ticks;
            }

            ticks += (Reader.Read() - AsciiNumOffset) * coeff;
            coeff /= 10;
        }


        if (i is 0)
        {
            ErrorLog.Add($"{Reader.Position}: Fractional second specifier '.' must be followed by at least one digit.");
            return 0;
        }

        return ticks;
    }

    #endregion



    #region Escape Sequence

    private void EscapeSequence()
    {
        int initial = Reader.Read();

        if (initial is 'U')
        {
            UnicodeLongForm();
            return;
        }

        if (initial is 'u')
        {
            UnicodeShortForm();
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
            ErrorLog.Add($"{Reader.Position}: No escape sequence for character '{GetFriendlyNameFor(initial)}' (U+{initial:X8}).");
            SkipUntil(LF);
            return;
        }

        _builder.Append((char)result);
    }


    private void UnicodeLongForm()
    {
        //When control is passed to this method, only the numeric sequence remains (without '\' and 'U')
        int codePoint = ToUnicodeCodepoint(8);


        if (!IsUnicodeScalar(codePoint))
        {
            ErrorLog.Add($"{Reader.Position}: Found non-scalar or out of range Unicode codepoint U+{codePoint:X8} in escape sequence. Only scalar values may be escaped.");
            return;
        }


        //UTF-32 escape sequence, encode to surrogate pair.
        if (codePoint > MaxValue)
        {
            codePoint -= Plane1Start;
            _builder.Append((char)((codePoint >> 10) + HighSurrogateStart));
            _builder.Append((char)((codePoint & HighSurrogateRange) + LowSurrogateStart));

            Debug.Assert(IsSurrogatePair(_builder.AsSpan()[^2], _builder.AsSpan()[^1]));

            return;
        }

        _builder.Append((char)codePoint);
        return;
    }


    private void UnicodeShortForm()
    {
        int codePoint = ToUnicodeCodepoint(4);

        if (!IsUnicodeScalar(codePoint))
        {
            ErrorLog.Add($"{Reader.Position}: Found non-scalar or out of range Unicode codepoint U+{codePoint:X4} in escape sequence. Only scalar values may be escaped.");
            return;
        }

        _builder.Append((char)codePoint);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUnicodeScalar(int codePoint) => (uint)codePoint < ValidCodepointEnd &&
    /*Eg. valid codepoints that are not surrogates*/    !((uint)(codePoint - HighSurrogateStart) <= LowSurrogateEnd - HighSurrogateStart);


    private int ToUnicodeCodepoint(int digits)
    {
        Span<char> buffer = stackalloc char[digits];
        int charsRead = Reader.ReadBlock(buffer);

        if (charsRead < digits) //Definitely not good...
        {
            if (buffer[charsRead - 1] is DoubleQuote or Null)
                ErrorLog.Add($"{Reader.Position}: Escape sequence '{buffer.ToString()}' is missing {5 - charsRead} digit(s).");

            else
                ErrorLog.Add($"{Reader.Position}: Escape sequence '{buffer.ToString()}' must consist of {digits} hexadecimal characters.");

            SkipUntil(LF);
            return -1;
        }

        int codePoint = 0;

        for (int i = 0; i < digits; i++)
        {
            //Convert char to hexadecimal digit
            int digit = buffer[i] < AsciiDigitEnd ? 
                        buffer[i] - AsciiNumOffset : 
                        (buffer[i] & AsciiUpperNormalizeMask) - AsciiHexNumOffset;

            if ((uint)digit > 15)
            {
                ErrorLog.Add($"{Reader.Position}: {i + 1}. character '{buffer[i]}' in escape sequence is not a hexadecimal digit.");
                codePoint = -1;
                break;
            }

            //Build up codepoint from digits
            codePoint = (codePoint << 4) + digit;
        }

        return codePoint;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsControl(char c) => c < AsciiControlEnd && char.IsControl(c);

    #endregion
}
