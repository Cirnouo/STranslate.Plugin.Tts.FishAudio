using System.Text;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using static TestAssertions;

internal static class StreamingTextSuite
{
    internal static Task FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync()
    {
        var prefix = string.Concat(Enumerable.Repeat("😀", 100));
        var source = new ReadSentinelSource(prefix + new string('x', 100_000), prefix.Length);
        var reader = new StreamingTextReader(source);
        var chunk = reader.ReadNext(100);
        AssertEqual(prefix, chunk.Text, "First unpunctuated prefix must contain exactly 100 Unicode scalars");
        AssertEqual(true, chunk.FlushSafe, "A complete ordinary prefix must be flushable");
        AssertEqual(false, chunk.IsFinal, "The unread tail must remain pending");
        return Task.CompletedTask;
    }

    internal static Task MarkupStaysOrderedAcrossWindowsWithoutDuplicateDirectionsAsync()
    {
        foreach (var text in new[]
        {
            "[" + new string('d', 120) + "]<|speaker:0|><whisper>" + new string('w', 130) + "</whisper> hello.",
            "<|phoneme_start|>" + new string('p', 180) + "<|phoneme_end|> hello.",
            "<emphasis><soft>" + new string('e', 110) + "</soft></emphasis> done.",
            "<fast>a</fast><slow>b</slow><stress>c</stress><forceful>d</forceful>",
            "[whisper] 你好😀。 Another sentence. " + new string('t', 500),
        })
        {
            var reader = new StreamingTextReader(new StringStreamingTextSource(text));
            var chunks = new List<StreamingTextChunk>();
            while (reader.HasRemaining) chunks.Add(reader.ReadNext(chunks.Count == 0 ? 100 : 300));
            AssertEqual(text, string.Concat(chunks.Select(c => c.Text)), "Splitting must preserve exact text and markup order");
            AssertEqual(false, chunks.Any(c => c.Text.Length > 0 && char.IsHighSurrogate(c.Text[^1])), "No chunk may split a surrogate pair");
            if (text.StartsWith('[') && text.Length > 100 && text[99] == 'd')
                AssertEqual(false, chunks[0].FlushSafe, "An unfinished bracket direction must not flush");
            if (text.StartsWith("<|phoneme_start|>", StringComparison.Ordinal))
                AssertEqual(false, chunks[0].FlushSafe, "An unfinished phoneme span must not flush");
        }
        return Task.CompletedTask;
    }

    internal static Task ControlOnlyAndIncompleteInputNeverClaimsReadablePrefixAsync()
    {
        foreach (var text in new[] { " \r\n\t", "[whisper]", "<|speaker:0|>", "<whisper>", "<|phoneme_start|>abc", "[unfinished" })
        {
            var reader = new StreamingTextReader(new StringStreamingTextSource(text));
            var chunk = reader.ReadNext(100);
            AssertEqual(false, chunk.FlushSafe, "Control-only or incomplete text must not promise a physical first word");
            AssertEqual(text, chunk.Text, "Ambiguous input must still pass through unchanged");
            AssertEqual(true, chunk.IsFinal, "Short inputs end directly");
        }
        return Task.CompletedTask;
    }

    internal static Task BoundaryPriorityAndHugeTailDoNotChangeFirstPrefixAsync()
    {
        const string beginning = "First sentence. Second line\nthird word ";
        var text = beginning + new string('x', 1000);
        var shortReader = new StreamingTextReader(new StringStreamingTextSource(text));
        var hugeReader = new StreamingTextReader(new StringStreamingTextSource(text + new string('z', 1_000_000)));
        var chunk = shortReader.ReadNext(100);
        AssertEqual("First sentence.", chunk.Text, "Sentence boundaries take precedence within one bounded window");
        AssertEqual(chunk.Text, hugeReader.ReadNext(100).Text, "An unvisited tail cannot affect first-prefix selection");
        return Task.CompletedTask;
    }

    internal sealed class ReadSentinelSource(string text, int permittedCharacters) : IStreamingTextSource
    {
        internal int PermittedCharacters { get; set; } = permittedCharacters;
        internal int CharactersRead { get; private set; }
        public int Length => text.Length;
        public char this[int index]
        {
            get
            {
                if (index >= PermittedCharacters) throw new InvalidOperationException("Unread tail was enumerated before playback began.");
                CharactersRead++;
                return text[index];
            }
        }
    }
}
