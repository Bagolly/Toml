using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using Toml.Diagnostics;
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

            var testerConfig = new TomlConfig(
                TomlCommentMode.Validate, 
                ErrorReportPolicy.Throw, 
                ErrorSeverity.Warning);

            TOMLTokenizer t = new(source, testerConfig);
            _ = t.TokenizeFile();

            
            //Syntax error reported by tokenizer.
            if (t.Logger.HasErrors)    
                return true;
            

            TOMLParser p = new(t);
            var root = p.Parse();

            //Fail; parser finished without an error on an invalid file.
            ReportError(0, string.Empty, fPath);

            Console.Error.WriteLine(fPath);
            return false;
        }

        catch (Exception ex)
        {   
            //Not an uncaught .NET exception
            if (ex is ApplicationException)
                return true;

            //Unexpected exception; likely an untested scenario or some internal bug.
            ReportError(-2, $"{ex.GetType()} : '{ex.StackTrace}'", fPath);
            return false;
        }


        static void ReportError([ConstantExpected] int resultCode, string innerMsg, string filePath)
        {
            Console.Error.WriteLine($"On testcase '{filePath}'");
            switch (resultCode)
            {
                case 0:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"\nAn invalid file was accepted.");
                    break;
                case -2:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine($"\nAn unhandled exception was thrown: {innerMsg}");
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
    [Category("Valid Input")]
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
        .All(RunTestCase);

    private static bool RunTestCase(string fPath)
    {   
        if(fPath.EndsWith(".json"))
            return true;

        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            using FileStream fs = new(fPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using TomlStreamSource source = new(fs);

            var testerConfig = new TomlConfig(
             TomlCommentMode.Validate,
             ErrorReportPolicy.Throw,
             ErrorSeverity.Warning);

            TOMLTokenizer t = new(source, testerConfig);
            _ = t.TokenizeFile();


            //Syntax error reported by tokenizer.
            if (t.Logger.HasErrors)
            {
                StringBuilder sb = new($"Tokenizer reported {t.Logger.ErrorCount} errors. Error details:\n");

                foreach (var error in t.Logger.Errors)
                    sb.AppendLine(error.Message);


                ReportError(-1, sb.ToString(), fPath);
                return false;
            }

            
            TOMLParser p = new(t);
            var root = p.Parse();

            //Pass; valid file processed successfully.
            return true;
        }


        catch (Exception ex)
        {
            //Parser reported error on a valid error.
            if (ex is ApplicationException)
                ReportError(-1, ex.Message, fPath);
            
            //Unexpected exception; indicates an untested scenario or internal bug.
            else
                ReportError(-2, $"{ex.GetType()} : '{ex.StackTrace}'", fPath);
            
            return false;
        }


        static void ReportError([ConstantExpected] int resultCode, string msg, string fileName)
        {
            Console.Error.WriteLine($"On testcase '{fileName}'");
         
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
