using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Toml.Diagnostics;
using Toml.Runtime;
using Toml.Tokenization;
using static Toml.Runtime.TObject;
using static Toml.Tokenization.TomlTokenType;

namespace Toml.Parser;


public sealed class TOMLParser
{
    private TTable DocumentRoot;

    public Queue<TOMLValue> TokenStream { get; init; }

    public List<TObject> Values { get; init; }

    public TomlDiagnosticsManager Logger { get; }


    public TOMLParser(TOMLTokenizer tokenizer)
    {   
        (TokenStream, Values) = tokenizer.TokenizeFile();
        DocumentRoot = new(TOMLType.HeaderTable);
        Logger = tokenizer.Logger;
    }


    public TTable Parse()
    {
        TTable localRoot = DocumentRoot; //see documentation on local root for more info about this variable.


        while (TokenStream.TryPeek(out var currentToken))
        {
            switch (currentToken.TokenType)
            {
                case Key: //simple keys restricted to current scope.
                    AddKeyValuePair(to: localRoot);
                    break;

                case TableStart: //table declarations that change the scope to the declared table.
                    localRoot.CloseTable();
                    localRoot = AddTable(DocumentRoot);
                    break;

                case ImplicitKeyValueTable: //dotted keyval pairs (and ONLY those, headers are parsed below)
                    AddKeyValuePair(ResolveKeyValuePath(localRoot));
                    break;

                case ArrayTableStart: //arraytable declarations that change the scope to their last defined table.
                    localRoot.CloseTable();
                    localRoot = AddArrayTable(DocumentRoot);
                    break;

                case Eof:
                    goto FINISH;
                   
                default:
                    Console.WriteLine("Unexpected token: " + currentToken);
                    goto FINISH;
            }
        }

    FINISH:
        return DocumentRoot;
    }

    private static TomlParserException CreateFatalError(int i, string msg)
        => new TomlParserException(new TomlParserError(i, msg, ErrorSeverity.Fatal, ErrorDomain.Parser));
    
    private void LogParseError(int i, string msg, ErrorSeverity severity)
        => Logger.Add(new TomlParserError(i,msg, severity, ErrorDomain.Parser));    


    private TTable ResolveKeyValuePath(TTable containing)
    {
        while (TokenStream.Peek().TokenType is ImplicitKeyValueTable)
        {
            TKey fragment = (TKey)NextValue();

            if (containing.Values.TryGetValue(fragment.Value, out var existingValue)) //It is not yet known if it's actually a table.
            {
                if (existingValue is not TTable existingTable)    
                    throw CreateFatalError(TokenStream.Count - 2, 
                          $"Invalid path; expected {fragment} to refer to a table, not a {existingValue.Type}.");

                if (existingTable.Type is TOMLType.HeaderTable)
                    throw CreateFatalError(TokenStream.Count - 2,
                          $"Cannot modify existing table '{fragment.Value}', because it was already declared explicitly with a table header.");
                
                if (existingTable.State is TomlTableState.Closed)
                    throw CreateFatalError(TokenStream.Count - 2,
                        "Cannot inject key/value pairs into another table's subtable after it has already been defined.");

                else
                    return ResolveKeyValuePath(existingTable);
            }


            else
            {
                TTable subtable = new(TOMLType.KeyValTable);
                containing.Add(fragment.Value, subtable);

                return ResolveKeyValuePath(subtable);
            }
        }


        return containing;
    }


    private TTable ResolveHeaderPath(TTable containing) //lookups always start from the document root for headers.
    {
        while (TokenStream.Peek().TokenType is ImplicitHeaderTable)
        {
            TKey fragment = (TKey)NextValue();

            //It is not yet known if it's actually a table.
            if (containing.Values.TryGetValue(fragment.Value, out var existingValue))
            {
                if (existingValue is TArray existingArrayTable)
                {
                    if (existingArrayTable.Type is not TOMLType.ArrayTable)
                        throw CreateFatalError(TokenStream.Count - 2, 
                            $"Cannot reference statically defined array {fragment.Value} in a header.");

                    else
                        return ResolveHeaderPath((TTable)existingArrayTable[^1]);
                }


                if (existingValue is not TTable existingTable)
                    throw CreateFatalError(TokenStream.Count - 2,
                        $"Invalid path, {existingValue} is not a table.");
                
                /* [rare] Attempted injection by abusing dotted key and supertable-declarations, see example below.
                   This was explicitly made illegal by the standard (apparently).
                        a.b = 2
                        [a]
                        k = 123 # would inject directly into the closed scope of 'a'.
                 */
                if (existingTable.Type is TOMLType.KeyValTable && existingTable.State is TomlTableState.Closed)
                    throw CreateFatalError(TokenStream.Count - 2,
                        $"Cannot redeclare table '{fragment.Value}' because it was defined via dotted keys in another table (or root).");

                else
                    return ResolveHeaderPath(existingTable);
            }

            else
            {
                TTable subtable = new(TOMLType.HeaderTable);
                containing.Add(fragment.Value, subtable);

                return ResolveHeaderPath(subtable);
            }
        }

        return containing;
    }


    private TTable AddTable(TTable containing) //Handles table declarations
    {
        //Dequeue declstart marker token.
        TokenStream.Dequeue();


        //If the path to the table is a dotted key, resolve the path.
        if (TokenStream.Peek().TokenType is ImplicitHeaderTable)
            containing = ResolveHeaderPath(containing);


        Debug.Assert(TokenStream.Peek().TokenType is TableDecl);


        if (containing.Type is TOMLType.InlineTable)
        {
            LogParseError(TokenStream.Count - 1, "Table declarations cannot extend inline tables.", ErrorSeverity.Error);

            return new TTable(TOMLType.HeaderTable); //Worth a try...
        }

        //The tokenizer should fail before this even executes.
        if (!TokenStream.TryDequeue(out var keyToken))
            throw new TomlInternalException(new(
                "Uncaught syntax error: Table header's dotted key is missing the table itself.",
                ErrorSeverity.Fatal,
                ErrorDomain.Parser));

        if (keyToken.TokenType is Eof || Values[keyToken.ValueIndex] is not TKey tableKey)
            throw CreateFatalError(TokenStream.Count - 1,
                $"Expected a table declaration, but found a token of type '{keyToken.TokenType}'");


        if (containing.Values.TryGetValue(tableKey.Value, out var existingValue))
        {
            if (existingValue is TTable existingTable)
            {
                //Scope change will set this to Closed upon exit, so the next declaration should throw
                if (existingTable.Type is TOMLType.HeaderTable && existingTable.State is TomlTableState.Open)
                    return existingTable;


                //Existing header table is already closed -> redeclaration error.
                if (existingTable.Type is TOMLType.HeaderTable && existingTable.State is TomlTableState.Closed)
                    throw CreateFatalError(TokenStream.Count - 1, 
                        $"Cannot redefine the existing table '{tableKey.Value}' because it was already declared explicitly.");
            }


            else
                throw CreateFatalError(TokenStream.Count - 1,
                    $"Cannot define the table '{tableKey.Value}', because a value for it already exists: {existingValue}");
        }


        TTable table = new(TOMLType.HeaderTable);
        containing.Add(tableKey.Value, table);

        return table;
    }


    private TTable AddArrayTable(TTable containingTable)
    {
        TokenStream.Dequeue(); //Dequeue declstart dummy token.

        if (TokenStream.Peek().TokenType is ImplicitHeaderTable)
            containingTable = ResolveHeaderPath(containingTable);


        if (!TokenStream.TryDequeue(out var keyToken))
            throw new TomlInternalException(new(
                  "Uncaught syntax error: Arraytable header's dotted key is missing the table itself.",
                  ErrorSeverity.Fatal,
                  ErrorDomain.Parser));

        if (keyToken.TokenType is Eof || Values[keyToken.ValueIndex] is not TKey key)
            throw CreateFatalError(TokenStream.Count - 1,
                $"Expected an arraytable declaration, but found a token of type '{keyToken.TokenType}'");


        //Arraytables are a bit different when it comes to "redeclarations", since every new declaration adds a new table to the array,
        //and every reference to it should return its last defined table.
        if (containingTable.Values.TryGetValue(key.Value, out TObject? existingValue))
        {
            //Not an arraytable
            if (existingValue is not TArray existingArrayTable)
                throw CreateFatalError(TokenStream.Count - 1,
                    $"A value for the key '{key.Value}' already exists, but had the type {existingValue.Type}, instead of arraytable.");


            //Arraytable, but the wrong kind
            if (existingValue.Type is TOMLType.Array)
                throw CreateFatalError(TokenStream.Count - 1, 
                    $"Statically defined array '{key.Value}' cannot be appended to.");


            else
            {
                //This element is inaccesible from now on, but it's better to close it properly.
                ((TTable)existingArrayTable.Values[^1]).CloseTable();

                TTable newElement = new(TOMLType.HeaderTable);
                existingArrayTable.Add(newElement);
                return newElement;
            }
        }


        TTable firstElement = new(TOMLType.HeaderTable);
        containingTable.Add(key.Value, new TArray(with: firstElement));

        return firstElement;
    }


    private void AddKeyValuePair(TTable to)
    {
        if (!TokenStream.TryDequeue(out var keyToken))
            throw CreateFatalError(TokenStream.Count - 1,
                "Not enough tokens to resolve the key/value pair, possibly because of a syntax error.");


        if (keyToken.TokenType is Eof || Values[keyToken.ValueIndex] is not TKey key)
        {
            LogParseError(TokenStream.Count - 2,
                $"Expected a key to start a key/value pair, but found a token of type '{keyToken.TokenType}'",
                ErrorSeverity.Error);
            
            return;
        }

        if (to.Values.TryGetValue(key.Value, out TObject? value))
        {
            LogParseError(TokenStream.Count - 1,
                 $"Redefiniton: a value for the key '{key.Value}' already exists: '{value}' Type: {value.Type}",
                 ErrorSeverity.Error);

            //Skip the value if its key is invalid, since this is a non-fatal error, to avoid causing a fatal error later.
            TokenStream.Dequeue();
            return;
        }

        if (TokenStream.Peek().TokenType is InlineTableStart)
        {
            //Dequeue the opening delimiter '{'.
            TokenStream.Dequeue();

            TTable inlineTable = new(TOMLType.InlineTable);

            to.Add(key.Value, ParseInlineTable(inlineTable));
        }


        else
            to.Add(key.Value, ResolveValue());
    }


    //Needed because arrays and inline tables are values as well (which in turn contain values), requiring more than a simple dequeue.
    private TObject ResolveValue() => ResolveValue(TokenStream.Dequeue());


    private TObject ResolveValue(TOMLValue token) => token.TokenType switch
    {
        TableDecl or ArrayTableDecl => throw CreateFatalError(TokenStream.Count - 2, "Table and arraytable declarations are not allowed as values; use inline tables or arrays instead."),
        ArrayStart => ParseArray(),
        InlineTableStart => ParseInlineTable(new(TOMLType.InlineTable)), //Nested inline table or array element.
        _ => Values[token.ValueIndex],                                   //Single value tokens; simply return the value.
    };


    private TArray ParseArray()
    {
        //when control is passed to this method, array-start is already consumed.
        TArray array = [];

        while (TokenStream.TryPeek(out var token))
        {

            if (token.TokenType is TomlTokenType.ArrayEnd)
            {
                TokenStream.Dequeue();
                break;
            }

            else
                array.Add(ResolveValue()); //Arrays can accept any value type as well.
        }

        return array;
    }

    private TTable ParseInlineTable(TTable inlineTable)
    {
        //When control is passed to this method, InlineTableStart is already consumed.

        TTable currentScope = inlineTable;

        while (TokenStream.TryPeek(out var token))
        {
            if (token.TokenType is ImplicitKeyValueTable)
            {
                AddKeyValuePair(to: ResolveKeyValuePath(inlineTable));


                currentScope = inlineTable; //reset scope to root (inline table)
            }


            else if (token.TokenType is Key) //direct add to root
                AddKeyValuePair(to: currentScope);


            else if (token.TokenType is InlineTableEnd)
            {
                TokenStream.Dequeue();
                break;
            }

            else
                throw CreateFatalError(TokenStream.Count - 2, 
                    $"Syntax error: Unexpected token in inline table: {token}. Expected a key/value pair or '}}'.");
        }


        inlineTable.CloseInlineTable();

        return inlineTable;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TObject NextValue() => Values[TokenStream.Dequeue().ValueIndex];
}
