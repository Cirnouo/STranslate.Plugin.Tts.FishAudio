using NUnit.Framework;

// Pure row metadata. Test bodies retain direct named scene bindings.
internal static class NativeTestCaseSource
{
    internal static TestCaseData Row(string canonicalId, int ordinal, string name, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(canonicalId) || ordinal < 0 || string.IsNullOrWhiteSpace(name) ||
            arguments.Any(value => value is not (null or string or bool or int or double) && value is not Enum))
            throw new ArgumentException("native-case-metadata-invalid");
        return new TestCaseData(arguments).SetName(name)
            .SetProperty("FishAudioCaseId", canonicalId)
            .SetProperty("FishAudioProfileOrdinal", ordinal)
            .SetProperty("Order", ordinal);
    }
}

