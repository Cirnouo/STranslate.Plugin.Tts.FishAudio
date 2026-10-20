using System.Text;

namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal interface IStreamingTextSource
{
    int Length { get; }
    char this[int index] { get; }
}

internal sealed class StringStreamingTextSource(string text) : IStreamingTextSource
{
    public int Length => text.Length;
    public char this[int index] => text[index];
}

internal readonly record struct StreamingTextChunk(string Text, bool FlushSafe, bool IsFinal);

internal sealed class StreamingTextReader(IStreamingTextSource source)
{
    private int _position;
    private readonly MarkupState _markup = new();
    private bool _readable;
    internal bool HasRemaining => _position < source.Length;

    internal StreamingTextChunk ReadNext(int target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
        var text = new StringBuilder(target * 2);
        var sentence = 0;
        var newline = 0;
        var word = 0;
        var safe = 0;
        for (var scalars = 0; scalars < target && HasRemaining; scalars++)
        {
            var c = source[_position++];
            text.Append(c);
            Rune rune;
            if (char.IsHighSurrogate(c) && _position < source.Length && char.IsLowSurrogate(source[_position]))
            {
                var low = source[_position++];
                text.Append(low);
                rune = new Rune(c, low);
            }
            else
                rune = char.IsSurrogate(c) ? Rune.ReplacementChar : new Rune(c);

            if (_markup.Accept(rune) && !Rune.IsWhiteSpace(rune) && !Rune.IsPunctuation(rune) && !Rune.IsControl(rune))
                _readable = true;
            if (!_markup.IsSafe || !_readable) continue;
            safe = text.Length;
            if (rune.Value is '.' or '!' or '?' or '。' or '！' or '？') sentence = safe;
            else if (rune.Value is '\r' or '\n') newline = safe;
            else if (Rune.IsWhiteSpace(rune)) word = safe;
        }

        // Finished short input can stop immediately, including ambiguous/unclosed markup.
        // Otherwise prefer a natural boundary inside this bounded window. No tail scan occurs.
        var chosen = !HasRemaining ? text.Length : sentence != 0 ? sentence : newline != 0 ? newline : word != 0 ? word : safe;
        if (chosen == 0) return new(text.ToString(), false, !HasRemaining);
        var flushSafe = chosen <= safe && _readable;
        if (chosen < text.Length)
        {
            _position -= text.Length - chosen;
            _markup.Reset(); // Every selectable boundary has all recognized spans closed.
        }
        if (flushSafe) _readable = false;
        return new(text.ToString(0, chosen), flushSafe, !HasRemaining);
    }

    private sealed class MarkupState
    {
        private static readonly string[] PairedTags = ["whisper", "emphasis", "soft", "fast", "slow", "stress", "forceful"];
        private readonly int[] _depth = new int[PairedTags.Length];
        private readonly StringBuilder _token = new(32);
        private int _phonemeDepth;
        private int _directionDepth;
        private bool _inToken;
        private bool _longToken;
        internal bool IsSafe => !_inToken && _directionDepth == 0 && _phonemeDepth == 0 && !_depth.Any(d => d != 0);

        internal bool Accept(Rune rune)
        {
            var c = rune.Value;
            if (_inToken)
            {
                if (c == '>')
                {
                    _inToken = false;
                    if (!_longToken) ApplyToken(_token.ToString());
                    _token.Clear();
                    _longToken = false;
                }
                else if (_token.Length + rune.Utf16SequenceLength <= 32) _token.Append(rune.ToString());
                else _longToken = true;
                return false;
            }
            if (_directionDepth != 0)
            {
                if (c == '[') _directionDepth++;
                else if (c == ']') _directionDepth--;
                return false;
            }
            if (c == '<') { _inToken = true; return false; }
            if (c == '[') { _directionDepth = 1; return false; }
            return true;
        }

        private void ApplyToken(string token)
        {
            if (token == "|phoneme_start|") _phonemeDepth++;
            else if (token == "|phoneme_end|") _phonemeDepth = Math.Max(0, _phonemeDepth - 1);
            else
            {
                for (var i = 0; i < PairedTags.Length; i++)
                {
                    if (token == PairedTags[i]) { _depth[i]++; return; }
                    if (token == "/" + PairedTags[i]) { _depth[i] = Math.Max(0, _depth[i] - 1); return; }
                }
            }
            // Speaker tags are atomic. Unknown tags (including experimental language tags)
            // stay byte-for-byte unchanged; only their angle-delimited token is protected.
            // The protocol gives no escaping/nesting grammar. We conservatively hold flush
            // across recognized open spans, bracket directions and incomplete '<' tokens.
            // Token recognition uses 32 UTF-16 units of state, never a whole-tag allocation.
        }

        internal void Reset()
        {
            Array.Clear(_depth);
            _phonemeDepth = _directionDepth = 0;
            _inToken = _longToken = false;
            _token.Clear();
        }
    }
}
