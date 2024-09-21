using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using Toml.Parser;
using Toml.Reader;
using Toml.Runtime;
using Toml.Tokenization;

namespace TomlTesting;

[Category("Self Report")]
public class InvalidCaseTester
{
    private static readonly string _testCaseFolder = "../../../tests/invalid";


    [Test]
    [Category("Invalid Input")]
    public void TestArray() => Assert.That(AllTestsPass(category: "array"));


    [Test]
    [Category("Invalid Input")]
    public void TestBool() => Assert.That(AllTestsPass(category: "bool"));


    [Test]
    [Category("Invalid Input")]
    public void TestControl() => Assert.That(AllTestsPass(category: "control"));


    [Test]
    [Category("Invalid Input")]
    public void TestDateTime() => Assert.That(AllTestsPass(category: "datetime"));


    [Test]
    [Category("Invalid Input")]
    public void TestEncoding() => Assert.That(AllTestsPass(category: "encoding"));


    [Test]
    [Category("Invalid Input")]
    public void TestFloat() => Assert.That(AllTestsPass(category: "float"));


    [Test]
    [Category("Invalid Input")]
    public void TestInlineTable() => Assert.That(AllTestsPass(category: "inline-table"));


    [Test]
    [Category("Invalid Input")]
    public void TestInteger() => Assert.That(AllTestsPass(category: "integer"));


    [Test]
    [Category("Invalid Input")]
    public void TestKey() => Assert.That(AllTestsPass(category: "key"));


    [Test]
    [Category("Invalid Input")]
    public void TestLocalDate() => Assert.That(AllTestsPass(category: "local-date"));


    [Test]
    [Category("Invalid Input")]
    public void TestLocalDateTime() => Assert.That(AllTestsPass(category: "local-datetime"));


    [Test]
    [Category("Invalid Input")]
    public void TestLocalTime() => Assert.That(AllTestsPass(category: "local-time"));


    [Test]
    [Category("Invalid Input")]
    public void TestSpec() => Assert.That(AllTestsPass(category: "spec"));


    [Test]
    [Category("Invalid Input")]
    public void TestString() => Assert.That(AllTestsPass(category: "string"));


    [Test]
    [Category("Invalid Input")]
    public void TestTable() => Assert.That(AllTestsPass(category: "table"));



    private static bool AllTestsPass(string category) => Directory
        .GetFiles($"{_testCaseFolder}/{category}")
        .Select(RunTestCase)
        .All(static result => result);


    private static bool RunTestCase(string fPath)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            using FileStream fs = new(fPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using TomlStreamSource source = new(fs);
            
            TOMLTokenizer t = new(source);
            var (tStream, values) = t.TokenizeFile();

            
            //Syntax error reported by tokenizer.
            if (t.ErrorLog.Count != 0)    
                return true;
            

            TOMLParser p = new(tStream, values);
            var root = p.Parse();

            //Fail; parser finished without an error on an invalid file.
            ReportError(0, string.Empty);
            return false;
        }

        catch (Exception ex)
        {   
            //Error caught and reported by parser or tokenizer.
            if (ex is TomlRuntimeException or TomlReaderException)
                return true;

            //Unexpected exception; indicates an untested scenario or internal bug.
            ReportError(-2, $"{ex.GetType()} : '{ex.StackTrace}'");
            return false;
        }


        static void ReportError([ConstantExpected] int resultCode, string msg)
        {
            switch (resultCode)
            {
                case 0:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"\nAn invalid file was accepted.");
                    break;
                case -2:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"\nAn unhandled exception was thrown: {msg}");
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Error.WriteLine($"\nInvalid return code: " + resultCode);
                    break;
            }
            Console.ResetColor();
        }
    }
}

[Category("Self Report")]
public class ValidCaseTester
{
    private static readonly string _testCaseFolder = "../../../tests/valid";


    [Test]
    [Category("Valid Input")]
    public void TestArray() => Assert.That(AllTestsPass(category: "array"));


    [Test]
    [Category("Valid Input")]
    public void TestBool() => Assert.That(AllTestsPass(category: "bool"));


    [Test]
    [Category("Valid Input")]
    public void TestComment() => Assert.That(AllTestsPass(category: "comment"));


    [Test]
    [Category("Valid Input")]
    public void TestDateTime() => Assert.That(AllTestsPass(category: "datetime"));

    
    [Test]
    [Category("Valid Input")]
    public void TestFloat() => Assert.That(AllTestsPass(category: "float"));


    [Test]
    [Category("Valid Input")]
    public void TestInlineTable() => Assert.That(AllTestsPass(category: "inline-table"));


    [Test]
    [Category("Valid Input")]
    public void TestInteger() => Assert.That(AllTestsPass(category: "integer"));


    [Test]
    [Category("Valid Input")]
    public void TestKey() => Assert.That(AllTestsPass(category: "key"));


    [Test]
    public void TestMisc() => Assert.That(AllTestsPass(category: "misc"));


    [Test]
    [Category("Valid Input")]
    public void TestSpec() => Assert.That(AllTestsPass(category: "spec"));


    [Test]
    [Category("Valid Input")]
    public void TestString() => Assert.That(AllTestsPass(category: "string"));


    [Test]
    [Category("Valid Input")]
    public void TestTable() => Assert.That(AllTestsPass(category: "table"));



    private static bool AllTestsPass(string category) => Directory
        .GetFiles($"{_testCaseFolder}/{category}")
        .Select(RunTestCase)
        .All(static result => result);


    private static bool RunTestCase(string fPath)
    {   
        if(fPath.EndsWith(".json"))
            return true;

        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            using FileStream fs = new(fPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using TomlStreamSource source = new(fs);

            TOMLTokenizer t = new(source);
            var (tStream, values) = t.TokenizeFile();


            //Syntax error reported by tokenizer.
            if (t.ErrorLog.Count != 0)
            {
                StringBuilder sb = new($"Tokenizer reported {t.ErrorLog.Count} errors. Error details:\n");

                foreach (var error in t.ErrorLog)
                    sb.AppendLine(error);


                ReportError(-1, sb.ToString());
                return false;
            }

            
            TOMLParser p = new(tStream, values);
            var root = p.Parse();

            //Pass; valid file processed successfully.
            return true;
        }


        catch (Exception ex)
        {
            //Parser reported error on a valid error.
            if (ex is TomlRuntimeException or TomlReaderException)
                ReportError(-1, ex.Message);
            
            //Unexpected exception; indicates an untested scenario or internal bug.
            else
                ReportError(-2, $"{ex.GetType()} : '{ex.StackTrace}'");
            
            return false;
        }


        static void ReportError([ConstantExpected] int resultCode, string msg)
        {
            switch (resultCode)
            {
                case -1:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nA valid file was rejected. Reason: {msg ?? "None given."}");
                    break;
                case -2:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"\nAn unhandled exception was thrown: {msg}");
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Error.WriteLine($"\nInvalid return code: " + resultCode);
                    break;
            }
            Console.ResetColor();
        }
    }
}


//Needs toml json mapper tests.
