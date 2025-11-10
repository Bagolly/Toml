using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Columns;

namespace Toml.Diagnostics;


public enum ErrorSeverity
{   
    Info,
    Warning,
    Error,
    Fatal,
}

public enum ErrorDomain
{
    Reader,
    Tokenizer,
    Parser,
    Type,
    Internal
}

public enum ErrorReportPolicy
{
    /// <summary>
    /// Try to aggregate errors instead of throwing. Fatal errors will still be thrown.
    /// </summary>
    Aggregate,
    /// <summary>
    /// Throw instantly when encountering an error at, or above the sepcified throw threshold.
    /// </summary>
    Throw,
}

/*
public enum ErrorMessageIndex
{
    InvalidUTF8,
    TrailingChars,
    Comment_ControlChar,
    Value_Eof,
    Value_Invalid,
    Bool_NotLower,
    Bool_ExpectTrue,
    Bool_ExpectFalse,
    KeyVal_NoSep,
    Key_Eof,
    Key_FragMissing,
    Table_DeclEmpty,
    Table_DeclUnterminated,
    ArrayTable_DeclEmpty,
    ArrayTable_DeclUnterminated,
    Array_Unterminated,
    Inline_CommaInEmpty,
    Inline_TrailingComma,
    Inline_Multiline,
    Inline_ExpectTerminate,
    Str_InvalidQuoteCnt,
    StrB_Multiline,
    StrB_TrailingBackslash,
    StrB_Unterminated,
    StrML_TrailingBackslash,
    StrML_Unterminated,
    StrL_Multiline,
    StrL_ControlChar,
    StrL_Unterminated,
    StrL_MControlChar,
    StrLM_Unterminated,
    StrM_TrailingQuotes,
    Int_PrefixSigned,
    Int_SepBetweenPrefix,
    Float_ExpectInf,
    Float_ExpectNan,
    Int_PrefixInvalid,
    Int_SepNoDigits,
    Int_SepIsLast,
    Int_Invalid,
    Dt_YearOutOfRange,
    Dt_DashInvalid,
    T_HourOutOfRange,
    Dt_SemicolonInvalid,
    Float_ExpEof,
    Float_SepBetweenExp,
    Float_DotEof,
    Float_DotNoDigits,
    Dt_MonthDayOutOfRange,
    Dt_Invalid,
    T_HourInvalid,
    T_SemicolonHourMin,
    T_SemiColonMinSec,
    T_InvalidCharacter,
    T_Invalid,
    T_OutOfRange,
    Dto_ZoneInvalid,
    TFracSec_Truncated,
    TFracSec_NoDigits,
    Escape_NotFound,
    Escape_OutOfRange,
    Escape_MissingDigits,
    Escape_InvalidDigit
}
*/


public class TomlError : IEquatable<TomlError>
{
    public ErrorSeverity Severity { get; private init; }
    public ErrorDomain Domain { get; private init; }
    public string Message { get; private init; }

    public TomlError(string msg, ErrorSeverity s, ErrorDomain d)
    {
        Severity = s;
        Domain = d;
        Message = msg;
    }

    private static readonly string MessageTemplate =
    """
    Severity: {0}
    Domain:   {1}
    Message:  {2}
    """;

    public override string ToString() 
        => string.Format(format: MessageTemplate, Severity, Domain, Message);

    public override int GetHashCode()
        => HashCode.Combine(Domain, Severity, Message);

    public bool Equals(TomlError? other)
        => other    != null &&
           Domain   == other.Domain &&
           Severity == other.Severity &&
           Message  == other.Message;

    public override bool Equals(object? obj)
        => Equals(obj as TomlError);
}


public sealed class TomlSyntaxError : TomlError, IEquatable<TomlSyntaxError>
{
    public int Line { get; private init; }
    public int Column { get; private init; }

    private static readonly string MessageTemplate =
        """
        Severity: {0} 
        Domain:   {1} 
        Position: line {2}; column {3}
        Message:  '{4}'
        """;

    public TomlSyntaxError(int l, int c, string m, ErrorSeverity s, ErrorDomain d) : base(m, s, d)
    {
        Line = l;
        Column = c;
    }

    public override string ToString() =>
        string.Format(format: MessageTemplate,
                              Severity,
                              Domain,
                              Line,
                              Column,
                              Message);

    public bool Equals(TomlSyntaxError? other) => 
        other  != null &&
        Line   == other.Line && 
        Column == other.Column &&
        Domain == other.Domain;

    public override bool Equals(object? obj) => 
        obj is TomlSyntaxError other && Equals(other);
    
    public override int GetHashCode() => 
        HashCode.Combine(Line, Column, (int)Domain);
}


public sealed class TomlParserError : TomlError, IEquatable<TomlParserError>
{   
    public int TokenIndex { get; private init; }

    private static readonly string MessageTemplate =
    """
    Severity: {0} 
    Domain:   {1} 
    Index:    {2}
    Message:  '{3}'
    """;

    public TomlParserError(int i, string m, ErrorSeverity s, ErrorDomain d) 
        : base(m, s, d) => TokenIndex = i;

    public override string ToString() =>
        string.Format(format: MessageTemplate,
                              Severity,
                              Domain,
                              TokenIndex,
                              Message);

    public bool Equals(TomlParserError? other) =>
        other != null &&
        TokenIndex == other.TokenIndex &&
        Domain == other.Domain;

    public override bool Equals(object? obj) =>
        obj is TomlParserError other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(TokenIndex, (int)Domain); //One token can only produce one unique error.
}


public abstract class TomlExcpetionBase : ApplicationException
{
    public TomlError Error { get; init; }

    public TomlExcpetionBase(TomlError error) : base(error.Message)
        => Error = error;
}

/// <summary>
/// Represents exceptions that occured in the tokenization phase.
/// <para>Usually indicates a syntactic or other formatting issue.</para>
/// </summary>
public sealed class TomlTokenizerException(TomlSyntaxError error)
    : TomlExcpetionBase(error) { }


/// <summary>
/// Represents exceptions that occured in the reading phase.
/// <para>Usually indicates an encoding issue.</para>
/// </summary>
public sealed class TomlReaderException(TomlSyntaxError error) : TomlExcpetionBase(error) { }


/// <summary>
/// Represents exceptions that occured in the parsing phase.
/// <para>Usually indicates a semantic issue.</para>
/// </summary>
public sealed class TomlParserException(TomlParserError error) : TomlExcpetionBase(error) { }


/// <summary>
/// Represents exceptions that occur during construction or usage of TOML types.
/// <para>Usually indicates misuse of a TOML type.</para>
/// </summary>
public sealed class TomlTypeException(TomlError error) : TomlExcpetionBase(error) { }


/// <summary>
/// Represents unexpected exceptions or assertion failures that indicate an internal error.
/// <para>Always indicates an unexpected internal error.</para>
/// </summary>
public sealed class TomlInternalException(TomlError error) : TomlExcpetionBase(error) { }


public sealed class TomlDiagnosticsManager
{
    private readonly ErrorReportPolicy _policy;

    private readonly ErrorSeverity _thresHold;

    private readonly List<TomlError> _errors;

    internal TomlDiagnosticsManager(ErrorReportPolicy policy, ErrorSeverity throwThreshold)
    {
        _errors = new();
        _policy = policy;
        _thresHold = throwThreshold;
    }
    
    internal void Add(TomlError error)
    {
        if (error.Severity is ErrorSeverity.Fatal)
            ThrowError(error);

        if (_policy is ErrorReportPolicy.Throw && error.Severity >= _thresHold)
            ThrowError(error);

        _errors.Add(error);
    }

    public IEnumerable<TomlError> Errors => _errors;

    public IEnumerable<TomlError> ByDomain(ErrorDomain domain) 
        => _errors.Where(error => error.Domain == domain);

    public IEnumerable<TomlError> BySeverity(ErrorSeverity severity)
        => _errors.Where(err => err.Severity >= severity);

    public bool HasErrors => _errors.Count > 0;

    internal int ErrorCount => _errors.Count;


    [DoesNotReturn, StackTraceHidden]
    private static void ThrowError(TomlError error) => throw error.Domain switch
    {
        ErrorDomain.Reader => new TomlReaderException((TomlSyntaxError)error),
        ErrorDomain.Tokenizer => new TomlTokenizerException((TomlSyntaxError)error),
        ErrorDomain.Parser => new TomlParserException((TomlParserError)error),
        ErrorDomain.Type => new TomlTypeException(error),
        _ => new TomlInternalException(error),
    };
}
