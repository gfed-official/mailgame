using System;
using System.Text;

namespace PerformativeMail.Sim.Net
{
    /// <summary>Thrown when a received message is truncated or malformed. The receiver drops the message.</summary>
    public sealed class NetFormatException : Exception
    {
        public NetFormatException(string message) : base(message) { }
    }

    /// <summary>Little-endian binary writer for network messages.</summary>
    public sealed class NetWriter
    {
        private byte[] _buf;
        private int _len;

        public NetWriter(int capacity = 256) { _buf = new byte[capacity]; }

        public int Length => _len;
        public void Reset() => _len = 0;

        public byte[] ToArray()
        {
            var copy = new byte[_len];
            Buffer.BlockCopy(_buf, 0, copy, 0, _len);
            return copy;
        }

        private void Ensure(int extra)
        {
            if (_len + extra <= _buf.Length) return;
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + extra));
        }

        public void U8(byte v) { Ensure(1); _buf[_len++] = v; }
        public void I8(sbyte v) => U8((byte)v);
        public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);

        public void U16(ushort v)
        {
            Ensure(2);
            _buf[_len++] = (byte)v;
            _buf[_len++] = (byte)(v >> 8);
        }

        public void I16(short v) => U16((ushort)v);

        public void U32(uint v)
        {
            Ensure(4);
            _buf[_len++] = (byte)v;
            _buf[_len++] = (byte)(v >> 8);
            _buf[_len++] = (byte)(v >> 16);
            _buf[_len++] = (byte)(v >> 24);
        }

        public void I32(int v) => U32((uint)v);

        public void U64(ulong v)
        {
            U32((uint)v);
            U32((uint)(v >> 32));
        }

        public void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));

        /// <summary>UTF-8 with a u16 byte-length prefix.</summary>
        public void Str(string s)
        {
            int n = Encoding.UTF8.GetByteCount(s);
            if (n > ushort.MaxValue) throw new ArgumentException("string too long for the wire");
            U16((ushort)n);
            Ensure(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _len);
            _len += n;
        }
    }

    /// <summary>Little-endian binary reader. Every read is bounds-checked and throws <see cref="NetFormatException"/>.</summary>
    public sealed class NetReader
    {
        private readonly byte[] _buf;
        private int _pos;

        public NetReader(byte[] data) { _buf = data; }

        public int Remaining => _buf.Length - _pos;
        public bool AtEnd => _pos >= _buf.Length;

        private void Need(int n)
        {
            if (_pos + n > _buf.Length) throw new NetFormatException("message truncated");
        }

        public byte U8() { Need(1); return _buf[_pos++]; }
        public sbyte I8() => (sbyte)U8();
        public bool Bool() => U8() != 0;

        public ushort U16()
        {
            Need(2);
            ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public short I16() => (short)U16();

        public uint U32()
        {
            Need(4);
            uint v = (uint)(_buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int I32() => (int)U32();

        public ulong U64()
        {
            ulong lo = U32();
            ulong hi = U32();
            return lo | (hi << 32);
        }

        public float F32() => BitConverter.Int32BitsToSingle(I32());

        public string Str()
        {
            int n = U16();
            Need(n);
            string s = Encoding.UTF8.GetString(_buf, _pos, n);
            _pos += n;
            return s;
        }

        /// <summary>Reads a list count and rejects counts that cannot fit in the rest of the message.</summary>
        public int Count16(int minBytesPerElement)
        {
            int n = U16();
            if ((long)n * minBytesPerElement > Remaining) throw new NetFormatException("list count exceeds message size");
            return n;
        }
    }
}
