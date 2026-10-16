#if NETFRAMEWORK
// Omogućava sintaksu x[^1] na .NET Framework-u (kompajler traži tip System.Index).
namespace System
{
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;
        public Index(int value, bool fromEnd = false) { _value = fromEnd ? ~value : value; }
        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;
        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value) => new Index(value, true);
        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;
        public static implicit operator Index(int value) => FromStart(value);
        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object obj) => obj is Index i && Equals(i);
        public override int GetHashCode() => _value;
    }
}
#endif
