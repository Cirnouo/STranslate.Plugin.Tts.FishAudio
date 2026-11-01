namespace FishAudio.Testing.Execution;

internal static partial class FrameworkLifecycleClient
{
    static partial void InstallActionOwnership(string caseId, ref IDisposable? scope)
    {
        scope = RegressionActionScope.BeginCase(caseId, action => PublishAction(action.CaseId,
            action.Kind, action.Ordinal, action.Timestamp, action.Frequency, action.QueuedTimestamp));
    }
}
