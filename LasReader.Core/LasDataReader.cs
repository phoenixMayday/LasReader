using System.Globalization;
using System.Numerics;

namespace LasReader.Core;

internal sealed class LasDataReader
{
    private const double SmallestNormal = 2.2250738585072014E-308;
    private readonly char _delimiter;
    private readonly bool _isV30;
    private readonly bool _wrapped;
    private readonly string _nullToken;
    private int _nextColumn;
    private int _recordStartLine;

    internal LasDataReader(int curveCount, LasVersion version, char delimiter, bool wrapped,
        string nullToken, int lineNumber)
    {
        _isV30 = version == LasVersion.V30;
        if (_isV30 && wrapped)
            throw InvalidLine(lineNumber, "LAS 3.0 requires WRAP NO.");

        _delimiter = delimiter;
        _wrapped = wrapped;
        _nullToken = nullToken.Trim();
        NullValue = ReadNumber(_nullToken, lineNumber);
        Columns = new List<double>[curveCount];
        ExactValues = new Dictionary<int, string>?[curveCount];
        for (int column = 0; column < curveCount; column++)
            Columns[column] = [];
    }

    internal List<double>[] Columns { get; }
    internal Dictionary<int, string>?[] ExactValues { get; }
    internal double NullValue { get; }

    // Both inputs must have already passed finite numeric parsing.
    internal static bool AreEquivalentNumbers(ReadOnlySpan<char> left, ReadOnlySpan<char> right) =>
        left.SequenceEqual(right) || new DecimalToken(left).Equals(new DecimalToken(right));

    internal void ReadLine(ReadOnlySpan<char> line, int lineNumber)
    {
        if (!_isV30)
        {
            int comment = line.IndexOf('#');
            if (comment == 0 || (comment > 0 && char.IsWhiteSpace(line[comment - 1])))
                line = line[..comment];
        }

        if (line.Trim().IsEmpty && (_delimiter != '\t' || !line.Contains('\t')))
            return;

        bool indexLine = _wrapped && _nextColumn == 0;
        if (_nextColumn == 0)
            _recordStartLine = lineNumber;

        bool hasMore = true;
        var column = _nextColumn;
        while (hasMore)
        {
            ReadOnlySpan<char> token;
            if (_delimiter == ' ')
            {
                line = line.TrimStart();
                if (line.IsEmpty)
                    break;

                int end = 0;
                while (end < line.Length && !IsSpaceSeparator(line[end]))
                    end++;
                token = line[..end];
                line = line[end..];
            }
            else
            {
                int end = line.IndexOf(_delimiter);
                hasMore = end >= 0;
                token = hasMore ? line[..end].Trim() : line.Trim();
                line = hasMore ? line[(end + 1)..] : [];
            }

            if (indexLine && column > 0)
                throw InvalidLine(lineNumber, "The wrapped record's index must be alone on its own line.");

            if (column == Columns.Length)
                throw InvalidLine(lineNumber, _wrapped
                    ? $"Too many values for the wrapped record starting on line {_recordStartLine}."
                    : $"Expected {Columns.Length} data values in this row.");

            AddValue(token, column, lineNumber);
            column++;
        }

        if (!_wrapped && column != Columns.Length)
            throw InvalidLine(lineNumber, $"Expected {Columns.Length} data values in this row, found {column}.");

        _nextColumn = column == Columns.Length ? 0 : column;
    }

    internal void Finish(int lineNumber)
    {
        if (_nextColumn != 0)
            throw InvalidLine(lineNumber, $"The wrapped record starting on line {_recordStartLine} "
                + $"is incomplete. Expected {Columns.Length} values, found {_nextColumn}.");
    }

    private bool IsSpaceSeparator(char character) => _isV30 ? character == ' ' : char.IsWhiteSpace(character);

    private void AddValue(ReadOnlySpan<char> token, int column, int lineNumber)
    {
        List<double> values = Columns[column];
        if (_isV30 && token.IsEmpty)
        {
            if (column == 0)
                throw InvalidLine(lineNumber, "The index value is missing.");
            values.Add(double.NaN);
            return;
        }

        double value = ReadNumber(token, lineNumber);
        if (value == NullValue && AreEquivalentNumbers(token, _nullToken))
        {
            if (_isV30 && column == 0)
                throw InvalidLine(lineNumber, "The index value cannot be the null marker.");
            values.Add(double.NaN);
            return;
        }

        // Normal values with at most 15 significant digits round-trip through binary64.
        // Verify the decimal spelling near the limits of binary64 or for higher precision inputs.
        if (token.Length > 15 || Math.Abs(value) < SmallestNormal)
        {
            Span<char> formatted = stackalloc char[32];
            value.TryFormat(formatted, out int length, "R", CultureInfo.InvariantCulture);
            if (!AreEquivalentNumbers(token, formatted[..length]))
                (ExactValues[column] ??= new Dictionary<int, string>()).Add(values.Count, token.ToString());
        }

        values.Add(value);
    }

    private static double ReadNumber(ReadOnlySpan<char> token, int lineNumber)
    {
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || token.Contains('\0'))
        {
            throw InvalidLine(lineNumber, $"'{token}' is not a finite number.");
        }

        return value;
    }

    private static FormatException InvalidLine(int lineNumber, string reason) =>
        new($"LAS line {lineNumber}: {reason}");

    // Represent the exact decimal as its nonzero significant digits and a power of ten.
    // Parsing a double first validates the syntax, while this representation distinguishes
    // different input numbers that happen to map to the same double.
    private readonly ref struct DecimalToken
    {
        private readonly ReadOnlySpan<char> _digits;
        private readonly ReadOnlySpan<char> _exponent;
        private readonly int _exponentOffset;
        private readonly bool _isNegative;

        internal DecimalToken(ReadOnlySpan<char> token)
        {
            token = token.Trim();
            _isNegative = token[0] == '-';
            if (token[0] is '+' or '-')
                token = token[1..];

            int exponentStart = token.IndexOfAny('e', 'E');
            if (exponentStart >= 0)
            {
                _exponent = token[(exponentStart + 1)..];
                token = token[..exponentStart];
            }

            int first = 0;
            while (first < token.Length && token[first] is '0' or '.')
                first++;

            if (first == token.Length)
                return;

            int last = token.Length - 1;
            while (token[last] is '0' or '.')
                last--;

            int point = token.IndexOf('.');
            _exponentOffset = point < 0 ? token.Length - 1 - last
                : last > point ? point - last : point - last - 1;
            _digits = token[first..(last + 1)];
            DigitCount = _digits.Length - (point > first && point < last ? 1 : 0);
        }

        internal int DigitCount { get; }
        internal bool IsZero => _digits.IsEmpty;

        internal bool Equals(DecimalToken other)
        {
            if (IsZero || other.IsZero)
                return IsZero && other.IsZero;
            if (_isNegative != other._isNegative || DigitCount != other.DigitCount)
                return false;

            var otherDigits = other._digits;
            foreach (char digit in _digits)
            {
                if (digit == '.')
                    continue;
                if (otherDigits[0] == '.')
                    otherDigits = otherDigits[1..];
                if (digit != otherDigits[0])
                    return false;
                otherDigits = otherDigits[1..];
            }

            if (TryGetExponent(out long exponent) && other.TryGetExponent(out long otherExponent))
                return exponent == otherExponent;

            return GetLargeExponent() == other.GetLargeExponent();
        }

        private bool TryGetExponent(out long exponent)
        {
            exponent = 0;
            if (_exponent.IsEmpty)
            {
                exponent = _exponentOffset;
                return true;
            }

            // Leave room for the adjustment from the mantissa's decimal point and trailing zeroes.
            if (!long.TryParse(_exponent, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long raw)
                || raw < long.MinValue + int.MaxValue || raw > long.MaxValue - int.MaxValue)
                return false;

            exponent = raw + _exponentOffset;
            return true;
        }

        private BigInteger GetLargeExponent() => _exponent.IsEmpty ? _exponentOffset
            : BigInteger.Parse(_exponent, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) + _exponentOffset;
    }
}
