using System.Text;
using static Toml.Tokenization.Constants;
using Toml.Runtime;
using System.Runtime.CompilerServices;
using System.Diagnostics;


namespace Toml.Writer;


public class TomlWriter
{   
    //could change visitor signatures to return stringbuilder explicitly to enable chaining if later required.
    public virtual void Visit(TTable table)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TArray array)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TKey key)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TComment comment)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TString str)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TInteger integer)
    {
        switch (integer.Metadata)
        {
            case Tokenization.TomlTokenMetadata.Hex: AppendHex(); break;

            case Tokenization.TomlTokenMetadata.Decimal: AppendDecimal(); break;

            case Tokenization.TomlTokenMetadata.Binary: AppendBin(); break;

            case Tokenization.TomlTokenMetadata.Octal: AppendOct(); break;
        }


        void AppendHex()
        {
            Span<char> result = stackalloc char[HexStrBufferSize];
            HexString(integer.Value, ref result);

            if (!GroupAndAppend(result, TomlNumberFormatOptions.NumberForms.Hex))
                _sb.Append(result);
        }


        void AppendBin()
        {
            Span<char> result = stackalloc char[BinStrBufferSize];
            BinaryString(integer.Value, ref result);

            if (!GroupAndAppend(result, TomlNumberFormatOptions.NumberForms.Binary))
                _sb.Append(result);
        }


        void AppendOct()
        {
            Span<char> result = stackalloc char[OctStrBufferSize];
            OctalString(integer.Value, ref result);

            if (!GroupAndAppend(result, TomlNumberFormatOptions.NumberForms.Octal))
                _sb.Append(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AppendDecimal()
        {
            if (_options.NumberFormat.ExplicitPositiveSign && integer.Value > 0)
                _sb.Append('+');

            _sb.Append(integer.Value);
        }
    }

    public virtual void Visit(TBool boolean)
    {
        throw new NotImplementedException();
    }

    public virtual void Visit(TFloat number)
    {
        if (number.Value is double.PositiveInfinity && _options.NumberFormat.ExplicitPositiveSign)
            _sb.Append('+');

        else if(number.Value is double.NaN && _options.FloatFormat.ExplicitNanSign)
        {
            if(number.Metadata is Tokenization.TomlTokenMetadata.NegativeNan)
                _sb.Append('-');
            
            else if(number.Metadata is Tokenization.TomlTokenMetadata.PositiveNan)
                _sb.Append('+');
        }
    }


    private StringBuilder _sb;

    private TomlDeserializeOptions _options;

    #region CTOR
    public TomlWriter(int capacity) => _sb = new(capacity);

    public TomlWriter() => _sb = new();

    public TomlWriter(TomlDeserializeOptions options) : this() => _options = options;

    public TomlWriter(int capacity, TomlDeserializeOptions options) : this(capacity) => _options = options;
    #endregion


    internal const int HexStrBufferSize = 16 + 2;
    internal const int OctStrBufferSize = 22 + 2;
    internal const int BinStrBufferSize = 64 + 2;

    internal static readonly string HexLookupUpper = "0123456789ABCDEF";
    internal static readonly string HexLookupLower = "0123456789abcdef";


    internal void HexString(long value, ref Span<char> chars)
    {
        Debug.Assert(chars.Length is HexStrBufferSize); //this is technically no longer needed, leaving here in case of refactors
        Debug.Assert(value >= 0);

        ArgumentOutOfRangeException.ThrowIfNegative(value);

        
        ref readonly string lookup = ref (_options.IntegerFormat.HexUseUpperCase ? ref HexLookupUpper : ref HexLookupLower);

        int i = chars.Length;

        do
        {
            chars[--i] = lookup[(int)(value & 15)];
            value >>= 4;
        } while (value is not 0);

        chars[i - 1] = 'x';
        chars[i - 2] = '0';

        chars = chars.Slice(i - 2);//final result
    }


    internal static void OctalString(long value, ref Span<char> chars)
    {
        //CREDITS: .NET for original version of the algorithm from Convert.cs
        Debug.Assert(chars.Length is OctStrBufferSize); //this is technically no longer needed, leaving here in case of refactors

        ArgumentOutOfRangeException.ThrowIfNegative(value);

        int i = chars.Length;

        do
        {
            chars[--i] = (char)('0' + (value & 7));
            value >>= 3;
        }
        while (value != 0);

        chars[i - 1] = 'o';
        chars[i - 2] = '0';

        chars = chars.Slice(i - 2);
    }


    internal static void BinaryString(long value, ref Span<char> chars)
    {
        Debug.Assert(chars.Length is BinStrBufferSize);
        Debug.Assert(value >= 0);

        ArgumentOutOfRangeException.ThrowIfNegative(value);

        int i = chars.Length;

        do
        {
            chars[--i] = (char)('0' + (value & 1));
            value >>= 1;
        } while (value != 0);

        chars[i - 1] = 'b';
        chars[i - 2] = '0';

        chars = chars.Slice(i - 2);
    }


    private bool GroupAndAppend(ReadOnlySpan<char> str, TomlNumberFormatOptions.NumberForms format)
    {
        //None of these are modified, just makes it more readable. No readonly locals in C# 12...
        int mainGroup = _options.NumberFormat.MainDigitGroup;
        int lastGroup = _options.NumberFormat.LastDigitGroup;

        //Invariants.
        if (!_options.NumberFormat.ApplyTo.HasFlag(format) || mainGroup < 1 || str.Length < lastGroup)
            return false;


        //Too short to group.
        if (str.Length < lastGroup + 1)
        {
            _sb.Append(str);
            return true;
        }

       
        /*The main group count is the (integral) number of main groups this number 
          can be divided into.
          First, the last group's length is subtracted to get the available # of
          digits that needs to be divided into main groups.
          
          The remainder of the division is the remaining digits after accounting
          for all main groups as well as the last group. This group consists of 
          the (left-most) most significant digits, and therefore gets added first.
          
          If remainder is 0, it means the number fits perfectly within the main
          groups (for example "123 456" or "DEAD BEEF").
          
          If the remainder is not 0, then the left-most group is pushed to buffer,
          after which all the main groups are added.

          Lastly, the lastGroup number group get push to buffer.
         */
        var (mGroupCount, msigDigitGroup) = Math.DivRem(str.Length - lastGroup, mainGroup);


        //Append most significant group if the number can't be evenly distributed into main groups.
        if (msigDigitGroup is not 0)
            _sb.Append(str.Slice(0, msigDigitGroup)).Append('_');
        

        //Append main group. Includes most sig. group as well when |str| % lastGroup == 0
        for (int i = 0; i < mGroupCount; i++)
            _sb.Append(str.Slice(mainGroup * i + msigDigitGroup, mainGroup)).Append('_');


        _sb.Append(str.Slice(msigDigitGroup + mGroupCount * mainGroup, lastGroup));

        return true;
    }
}



public record struct TomlDeserializeOptions
{
    //Applies to file and newlines within multiline strings.
    public LineEnding LineEndings { get; set; }

    public TomlNumberFormatOptions NumberFormat { get; set; }

    public TomlIntegerFormatOptions IntegerFormat { get; set; }

    public TomlFloatFormatOptions FloatFormat { get; set; }

    public TomlArrayFormatOptions ArrayFormat { get; set; }

    public TomlDateTimeFormatOptions DateTimeOptions { get; set; }


    public enum LineEnding
    {
        /// <summary>
        /// Decide line endings based on the current operating system:
        /// <para>-<see langword="CRLF"/> if the current OS is Windows</para>
        /// <para>-<see langword="LF"/> otherwise</para>
        /// <para>See <see href="https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md">supported platforms</see> for more information.</para>
        /// </summary>
        Auto,

        /// <summary>
        /// Use <see langword="LF"/> (Unix) line endings.
        /// </summary>
        LF,

        /// <summary>
        /// Use <see langword="CRLF"/> (Windows) line endings.
        /// </summary>
        CRLF
    }
}


public record struct TomlNumberFormatOptions
{
    public int MainDigitGroup { get; set; }

    public int LastDigitGroup
    {
        get => _lastDigitGroup; //set to main group size by default.
        set => _lastDigitGroup = value;
    }

    private int _lastDigitGroup;

    //Note: if targets include floats then 'inf' becomes '+inf' and 'e' becomes 'e+'
    public bool ExplicitPositiveSign { get; set; }

    public NumberForms ApplyTo { get; set; }

    public TomlNumberFormatOptions()
    {
        MainDigitGroup = 3;
        LastDigitGroup = 3;
    }

    [Flags]
    public enum NumberForms
    {
        Float = 1,
        Decimal = 2,
        NonPrefixed = Float | Decimal,
        Hex = 4,
        Binary = 8,
        Octal = 16,
        Prefixed = Hex | Binary | Octal,
        Integer = Prefixed | Decimal,
    }
}


public record struct TomlIntegerFormatOptions
{
    //Hexadecimal digits should use upper case letters
    public bool HexUseUpperCase { get; set; }
}



public record struct TomlFloatFormatOptions
{
    public bool UpperCaseExponential { get; set; }

    public bool ExplicitNanSign { get; set; }
}



public struct TomlDateTimeFormatOptions
{
    public DateTimeSeparator Separator { get; set; }

    public enum DateTimeSeparator { TLower, TUpper, Space }

    //How many digits to write. If less than actual value,
    //it will be truncated: for example, if set to 3 digits then 1.456999 becomes 1.456 
    //If more, value will be zero padded: 0.123 to 7 digits: 0.1230000
    //precision must be positive and cannot be more than the maximum supported precision
    public int SubSecondPrecision
    {
        get => _subSecPrec;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, FracSec_MaxPrecisionDigits);
            _subSecPrec = value;
        }
    }

    private int _subSecPrec;
}

public record struct TomlStringFormatOptions
{
    public int LineWrapLimit { get; set; }
}

public record struct TomlArrayFormatOptions
{
    //If linewraplimit is set, it will take precedence over item limit. If the whole array fits
    //under the line limit, maxelementsperline will be respected.
    public int MaxElementsPerLine { get; set; }
}
