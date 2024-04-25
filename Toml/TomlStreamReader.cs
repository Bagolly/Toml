﻿using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Toml
{
    sealed class TOMLStreamReader : IDisposable
    {
        internal StreamReader BaseReader { get; init; }

        internal int Line { get; private set; }

        internal int Column { get; private set; }

        public TOMLStreamReader(Stream source)
        {
            if (!source.CanRead || !source.CanSeek)
                throw new ArgumentException("Streams without read or seeking support cannot be used.");

            BaseReader = new(source, Encoding.UTF8); //Encoding is UTF-8 by default.
            Line = 1;
            Column = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipWhiteSpace(bool skipNewLine = false)
        {
        SKIP:
            switch (Peek())
            {
                case ' ' or '\t':
                case '\r' when skipNewLine && PeekNext() is '\n': //annoying fucking windows crlf line endings
                case '\n' when skipNewLine:                       //normal person line ending
                    Read(); goto SKIP;
                default: return;
            }
        }

        /// <summary>
        /// Returns the next available character, consuming it. 
        /// </summary>
        /// <returns>The next available character, or <see langword="EOF"/>('\0') if no more characters are available to read.</returns>
        public char Read()
        {
            int readResult = BaseReader.Read();
            switch (readResult)
            {
                case -1: return '\0';

                case '\n':
                    ++Line;
                    Column = 0;
                    return (char)readResult;

                default:
                    ++Column;
                    return (char)readResult;
            }
        }

        /// <summary>
        /// Returns the next available character, consuming it. Ignores tab or skip.
        /// </summary>
        /// <remarks>
        /// <paramref name="skipNewLine"/>: ignore LF and CRLF line-endings as well.
        /// </remarks>
        /// <returns>The next available character, or <see langword="EOF"/>('\0') if no more characters are available to read.</returns>
        public char ReadSkip(bool skipNewLine = false)
        {
            SkipWhiteSpace(skipNewLine);
            return Read();
        }


        /// <summary>
        /// Returns the next available character, without consuming it.
        /// </summary>
        /// <returns><see langword="EOF"/> ('\0') if there are no characters to be read; otherwise, the next available character.</returns>
        public char Peek()
        {
            int peekResult = BaseReader.Peek();
            return peekResult is -1 ? '\0' : (char)peekResult;
        }


        /// <summary>
        /// Returns the next available character, without consuming it. Ignores tab or space.
        /// </summary>
        /// <remarks>
        /// <paramref name="skipNewLine"/>: ignore LF and CRLF line-endings as well.
        /// </remarks>
        /// <returns><see langword="EOF"/> ('\0') if there are no characters to be read; otherwise, the next available character.</returns>
        public char PeekSkip(bool skipNewLine = false)
        {
            SkipWhiteSpace(skipNewLine);
            return Peek();
        }


        /// <summary>
        /// Returns the character after the next available character, without consuming it. Ignores tab or space.
        /// </summary>
        /// <returns>The character after the next available character, or <see langword="EOF"/> ('\0') if the end of the stream is reached first.</returns>
        public char PeekNext()
        {
            if (Peek() == -1)
                return '\0';

            BaseReader.Read();

            int peekResult = Peek();

            //BaseReader.BaseStream.Position -= 1; //Idk about this. Dont change for now.

            if (peekResult is -1)
                return '\0';

            return (char)peekResult;
        }


        /// <summary>
        /// Returns the character after the next available character, without consuming it. Ignores tab or space.
        /// </summary>
        /// <returns>The character after the next available character, or <see langword="EOF"/> ('\0') if the end of the stream is reached first.</returns>
        public char PeekNextSkip()
        {
            SkipWhiteSpace();
            return PeekNext();
        }


        /// <summary>
        /// Consumes the next character from the stream if it matches <paramref name="c"/>.
        /// </summary>
        /// <returns> <see langword="true"/> if <paramref name="c"/> matches the next character; otherwise <see langword="false"/>.</returns>
        public bool MatchNext(char c)
        {
            if (Peek() == c)
            {
                Read();

                if (c == '\n')
                {
                    ++Line;
                    Column = 0;
                }

                else
                    ++Column;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Consumes characters while they match <paramref name="c"/>, and stores the number of matches.
        /// </summary>
        /// <returns>The number of sequential occurrences of <paramref name="c"/> from the stream's current position, or 0 if no characters matched.</returns>
        public int MatchCount(char c)
        {
            int cnt = 0;
            while (MatchNext(c))
                ++cnt;
            return cnt;
        }


        /// <summary>
        /// Consumes either a CR or CRLF line ending if it matches one.
        /// </summary>
        /// <returns><see langword="true"/> if a line ending was matched; otherwise <see langword="false"/>.</returns>
        public bool MatchLineEnding() => MatchNext('\n') || MatchNext('\r') && MatchNext('\n');

        /// <summary>
        /// Consumes characters while they match <paramref name="c"/>.
        /// </summary>
        /// <param name="c"></param>
        public void SkipWhile(char c) { while (MatchNext(c)) ; }


        /// <summary>
        /// Consumes the next character from the stream if it matches <paramref name="c"/>. Ignores tab or space.
        /// </summary>
        /// <returns> <see langword="true"/> if <paramref name="c"/> matches the next character; otherwise <see langword="false"/>.</returns>
        public bool MatchNextSkip(char c)
        {
            SkipWhiteSpace();

            return MatchNext(c);
        }

        public void Dispose() => BaseReader.Dispose();
    }
}