using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Dia2Lib;
using Toml.Runtime;
using Toml.Tokenization;
using static Toml.Tokenization.TomlTokenType;
namespace Toml.Parser;


sealed class TOMLParser
{
    private TTable Root;

    public Queue<TOMLValue> TokenStream { get; init; }

    public List<TObject> Values { get; init; }

    public TOMLParser(Queue<TOMLValue> tokenStream, List<TObject> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(tokenStream), "No tokens provided.");
        ArgumentException.ThrowIfNullOrEmpty(nameof(values), "No values provided");

        TokenStream = tokenStream;
        Values = values;
        Root = [];
    }


    public TTable Parse()
    {
        TTable currentScope = Root;

        while (TokenStream.TryPeek(out var currentToken))
        {
            switch (currentToken.TokenType)
            {
                case Eof:
                    goto ENDLOOP;                                                                       //we have named loops at home:

                case Key: //simple keys restricted to current scope.
                    AddKeyValuePair(to: currentScope);
                    break;

                case Table: //dotted keys for key/value pairs, or table and arraytable declarations.
                    AddKeyValuePair(ResolvePath(origin: currentScope));
                    //these will switch the current scope to the resolved value, but a dotted key cannot chnage
                    //the scope. In that it returns null and the scope does not change.
                    break;

                case ArrayTableDeclStart:
                    currentScope = AddArrayTable(to: Root); //todo add consume dummy token
                    break;

                case TableDeclStart:
                    currentScope = AddTable(to: Root);
                    break;

                default:
                    Console.WriteLine("Parser: unexpected token: " + currentToken);
                    goto ENDLOOP;
            }
        }

    ENDLOOP:
        return Root;
    }


    private TTable ResolvePath(TTable origin)
    {
        while (TokenStream.Peek().TokenType == Table)
        {
            TFragment current = (TFragment)NextValue();

            if (origin.Values.TryGetValue(current.Value, out var existingValue))
            {   
                if(existingValue is TArray arrayTable)
                {
                    /* This one can happen, if for example the user referres to an actual existing array in a dotted path.
                       This can be just a coincidence, or a misunderstanding of the way TOML works, but it can happen.*/
                    if (arrayTable.Type is not TObject.TOMLType.ArrayTable) 
                        throw new Exception($"Path invalid: the key fragment {current.Value} referred to a normal array.");

                    /* Arraytables must be initialized with a new table upon each declaration, as is required by the spec. 
                       Therefore, an existing and defined arraytable that is empty would mean an internal bug.*/
                    Debug.Assert(arrayTable.Values.Count > 0);

                    /* Similarly, if the cast fails there is an internal bug, as arraytables (as verified above) can
                       only ever contain Tables.*/
                    return ResolvePath(origin: (TTable)arrayTable.GetLast());                       
                }

                if (existingValue is not TTable subTable)
                    throw new Exception($"Path invalid: the value for '{current.Value}' is not a table, but <{existingValue.Type}>.");

                return ResolvePath(origin: subTable);
            }

            else
            {
             
                TTable subTable = new();
                origin.Add(current.Value, subTable);

                return ResolvePath(origin: subTable);
               // return BuildPath(from: origin);
            }
        }

        return origin;

        //TODO: implement this thing
        /*TTable BuildPath(TTable from) 
        {
            //if a fragment is discovered not to exist, then the checks can be elided, and the path directly built.
            return null;
        }*/
    }


    private TTable AddTable(TTable to)
    {
        TokenStream.Dequeue(); //Dequeue declstart dummy token.

        if (TokenStream.Peek().TokenType == Table) //the path to the table is a dotted key.
        {
            to = ResolvePath(origin: Root); //update scope to the resolved path
        }


        if (!TokenStream.TryDequeue(out var keyToken))
            throw new Exception("Could not resolve the name of the table, possibly because of a syntax error.");


        if (keyToken.TokenType == Eof || Values[keyToken.ValueIndex] is not TFragment key)
            throw new Exception($"Expected a table declaration, but found a token of type <{keyToken.TokenType}>");


        if (to.Values.TryGetValue(key.Value, out TObject? value))//todo: change to redecalrationexception
            throw new Exception($"A value for the key '{key.Value}' already exists: {value} (Type <{value.Type}>)");


        TTable table = new();
        to.Add(key.Value, table);

        return table;
    }


    private TTable AddArrayTable(TTable to)
    {
        TokenStream.Dequeue(); //Dequeue declstart dummy token.

        if (TokenStream.Peek().TokenType == Table) //the path to the table is a dotted key.
            to = ResolvePath(origin: Root); //updte scope to the resolved path
        

        if (!TokenStream.TryDequeue(out var keyToken))
            throw new Exception("Could not resolve the name of the arraytable, possibly because of a syntax error.");


        if (keyToken.TokenType == Eof || Values[keyToken.ValueIndex] is not TFragment key)
            throw new Exception($"Expected a table declaration, but found a token of type <{keyToken.TokenType}>");


        //Arraytables are a bit different when it comes to redeclarations. Every new declaration adds a new table to the array,
        //and every reference to it should return its last defined table.
        if (to.Values.TryGetValue(key.Value, out TObject? existingValue))
        {
            if (existingValue is not TArray existingArrayTable)
                throw new Exception($"A value for the key '{key.Value}' already exists, but had the type {existingValue.Type}, instead of arraytable.");

            if (existingValue.Type is TObject.TOMLType.Array)
                throw new Exception($"Statically defined array '{key.Value}' cannot be appended to.");

            else
            {
                TTable newElement = new();
                existingArrayTable.Add(newElement);
                return newElement;
            }
        }


        TTable firstElement = new();
        to.Add(key.Value, new TArray(from: firstElement));

        return firstElement;
    }


    private void AddKeyValuePair(TTable to)
    {
        if (!TokenStream.TryDequeue(out var keyToken))
            throw new Exception("Not enough tokens to resolve the key/value pair, possibly because of a syntax error.");


        if (keyToken.TokenType == Eof || Values[keyToken.ValueIndex] is not TFragment key)
            throw new Exception($"Expected a key to start a key/value pair, but found a token of type <{keyToken.TokenType}>");


        if (to.Values.TryGetValue(key.Value, out TObject? value))//todo: change to redecalrationexception
            throw new Exception($"A value for the key '{key.Value}' already exists: {value} : {value.Type}");


        to.Add(key.Value, ResolveValue());
    }


    //Needed because arrays and inline tables are values as well (which in turn contain values), requiring more than a simple dequeue.
    private TObject ResolveValue()
    {
        var token = TokenStream.Dequeue();

        return token.TokenType switch
        {
            TableDecl or ArrayTableDecl => throw new Exception("Table and arraytable declarations are not allowed as values; use inline tables or arrays instead."),
            ArrayStart => ParseArray(),
            InlineTableStart => ParseInlineTable(),
            _ => Values[token.ValueIndex],  //single value tokens like integers, bools, etc.; simply return the value.
        };
    }


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

    private TTable ParseInlineTable() //todo: inline tables cannot used to add keys or subtables to existibg tables
    {
        //When control is passed to this method, InlineTableStart is already consumed.

        TTable inlineTable = new();

        while (TokenStream.TryPeek(out var token))
        {

            if (token.TokenType is TomlTokenType.InlineTableEnd)
            {
                TokenStream.Dequeue();
                break;
            }

            else
                AddKeyValuePair(inlineTable);
        }

        return inlineTable;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TObject NextValue() => Values[TokenStream.Dequeue().ValueIndex];
}
