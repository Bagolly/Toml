using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Toml
{
    //Ripped .NET internal code UwU
    public ref struct ValueStringBuilder //TLDR; MUST BE PASSED AS REF WHEN ARGUMENT!
    {
        private char[]? _arrayToReturnToPool;
        private Span<char> _chars;
        private int _appendedCharCount;

        public ValueStringBuilder(Span<char> initialBuffer)
        {
            _arrayToReturnToPool = null;
            _chars = initialBuffer;
            _appendedCharCount = 0;
        }

        public ValueStringBuilder(int initialCapacity)
        {
            _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
            _chars = _arrayToReturnToPool;
            _appendedCharCount = 0;
        }

        public int Length
        {
            readonly get => _appendedCharCount;
            set
            {
                Debug.Assert(value >= 0);
                Debug.Assert(value <= _chars.Length);
                _appendedCharCount = value;
            }
        }

        public readonly int Capacity => _chars.Length;

        public void EnsureCapacity(int capacity)
        {
            Debug.Assert(capacity >= 0); //Not expected to be called with a negative value

            if ((uint)capacity > (uint)_chars.Length)// If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
                Grow(capacity - _appendedCharCount);
        }


        /// <summary>
        /// Get a pinnable reference to the builder.
        /// Does not ensure there is a null char after <see cref="Length"/>
        /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
        /// the explicit method call, and write eg "fixed (char* c = builder)"
        /// </summary>
        public readonly ref char GetPinnableReference() => ref MemoryMarshal.GetReference(_chars);


        /// <summary>
        /// Get a pinnable reference to the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ref char GetPinnableReference(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }

            return ref MemoryMarshal.GetReference(_chars);
        }

        public ref char this[int index]
        {
            get
            {
                Debug.Assert(index < _appendedCharCount);
                return ref _chars[index];
            }
        }

        /// <summary>
        /// [CAUTION] This method calls Dispose()! Only call once at the end of the object's lifetime!
        /// </summary>
        public override string ToString()
        {
            string s = _chars[.._appendedCharCount].ToString();
            Dispose();
            return s;
        }

        public readonly Span<char> RawChars => _chars;


        /// <summary>
        /// Returns a span around the contents of the builder.
        /// </summary>
        /// <param name="terminate">Ensures that the builder has a null char after <see cref="Length"/></param>
        public ReadOnlySpan<char> AsSpan(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }

            return _chars[.._appendedCharCount];
        }

        public readonly ReadOnlySpan<char> AsSpan() => _chars[.._appendedCharCount];
        public readonly ReadOnlySpan<char> AsSpan(int start) => _chars[start.._appendedCharCount];
        public readonly ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);


        public bool TryCopyTo(Span<char> destination, out int charsWritten)
        {
            if (_chars[.._appendedCharCount].TryCopyTo(destination))
            {
                charsWritten = _appendedCharCount;
                Dispose();
                return true;
            }
            else
            {
                charsWritten = 0;
                Dispose();
                return false;
            }
        }


        public void Insert(int index, char value, int count)
        {
            if (_appendedCharCount > _chars.Length - count)
                Grow(count);

            int remaining = _appendedCharCount - index;

            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            _chars.Slice(index, count).Fill(value);

            _appendedCharCount += count;
        }


        public void Insert(int index, string? s)
        {
            if (s is null)
                return;

            int count = s.Length;

            if (_appendedCharCount > (_chars.Length - count))
                Grow(count);


            int remaining = _appendedCharCount - index;

            _chars.Slice(index, remaining).CopyTo(_chars[(index + count)..]);
            s.CopyTo(_chars[index..]);

            _appendedCharCount += count;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(char c)
        {
            int pos = _appendedCharCount;

            if ((uint)pos < (uint)_chars.Length)
            {
                _chars[pos] = c;
                _appendedCharCount = pos + 1;
            }

            else
                GrowAndAppend(c);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(string? s)
        {
            if (s is null)
                return;

            int pos = _appendedCharCount;

            if (s.Length == 1 && (uint)pos < (uint)_chars.Length) // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
            {
                _chars[pos] = s[0];
                _appendedCharCount = pos + 1;
            }

            else
                AppendSlow(s);
        }


        private void AppendSlow(string s)
        {
            int pos = _appendedCharCount;

            if (pos > _chars.Length - s.Length)
                Grow(s.Length);

            s.CopyTo(_chars[pos..]);
            _appendedCharCount += s.Length;
        }


        public void Append(char c, int count)
        {
            if (_appendedCharCount > _chars.Length - count)
                Grow(count);


            Span<char> dst = _chars.Slice(_appendedCharCount, count);

            for (int i = 0; i < dst.Length; i++)
                dst[i] = c;

            _appendedCharCount += count;
        }


        public unsafe void Append(char* value, int length)
        {
            int pos = _appendedCharCount;

            if (pos > _chars.Length - length)
                Grow(length);

            Span<char> dst = _chars.Slice(_appendedCharCount, length);

            for (int i = 0; i < dst.Length; i++)
                dst[i] = *value++;

            _appendedCharCount += length;
        }


        public void Append(ReadOnlySpan<char> value)
        {
            int pos = _appendedCharCount;

            if (pos > _chars.Length - value.Length)
                Grow(value.Length);

            value.CopyTo(_chars[_appendedCharCount..]);
            _appendedCharCount += value.Length;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<char> AppendSpan(int length)
        {
            int origPos = _appendedCharCount;

            if (origPos > _chars.Length - length)
                Grow(length);

            _appendedCharCount = origPos + length;
            return _chars.Slice(origPos, length);
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowAndAppend(char c)
        {
            Grow(1);
            Append(c);
        }


        /// <summary>
        /// Resize the internal buffer either by doubling current buffer size or
        /// by adding <paramref name="additionalCapacityBeyondPos"/> to
        /// <see cref="_pos"/> whichever is greater.
        /// </summary>
        /// <param name="additionalCapacityBeyondPos">
        /// Number of chars requested beyond current position.
        /// </param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int additionalCapacityBeyondPos)
        {
            Debug.Assert(additionalCapacityBeyondPos > 0);
            Debug.Assert(_appendedCharCount > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

            // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative
            char[] poolArray = ArrayPool<char>.Shared.Rent((int)Math.Max((uint)(_appendedCharCount + additionalCapacityBeyondPos), (uint)_chars.Length * 2));

            _chars[.._appendedCharCount].CopyTo(poolArray);

            char[]? toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = poolArray;

            if (toReturn != null)
                ArrayPool<char>.Shared.Return(toReturn);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            char[]? toReturn = _arrayToReturnToPool;
            this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
            if (toReturn != null)
                ArrayPool<char>.Shared.Return(toReturn);
        }
    }
}
