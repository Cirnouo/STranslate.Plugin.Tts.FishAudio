using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Reflection;

const string AppliedKey = "0123456789abcdef0123456789abcdef";
const string DraftKey = "abcdef0123456789abcdef0123456789";

StartupValidationShowsAppliedStatus();
EditingDraftApiKeyDoesNotClearAppliedStatus();

Console.WriteLine("SettingsViewModel API key behavior tests passed.");

static void StartupValidationShowsAppliedStatus()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var pendingCredit = Task.FromResult<(WalletCreditResponse?, long)>((new WalletCreditResponse { Credit = "12.34" }, 42L));
    var viewModel = new SettingsViewModel(CreateContext(), settings, pendingCredit);

    AssertEqual(true, viewModel.IsApiKeyValid, "Startup credit validation should mark the applied key valid");
    AssertEqual(ApiKeyStatusKind.Success, viewModel.ApiKeyStatusKind, "Startup credit validation should show applied status");
    AssertEqual("12.34", viewModel.UserCredit, "Startup credit validation should populate account credit");
}

static void EditingDraftApiKeyDoesNotClearAppliedStatus()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var viewModel = new SettingsViewModel(CreateContext(), settings, null)
    {
        IsApiKeyValid = true,
        ApiKeyStatusKind = ApiKeyStatusKind.Success,
        UserCredit = "12.34",
    };

    viewModel.ApiKey = DraftKey;

    AssertEqual(true, viewModel.IsApiKeyValid, "Editing the draft key should not clear the applied valid key state");
    AssertEqual(ApiKeyStatusKind.Success, viewModel.ApiKeyStatusKind, "Editing the draft key should not clear the applied status");
    AssertEqual("12.34", viewModel.UserCredit, "Editing the draft key should not clear applied account credit");
    AssertEqual(AppliedKey, settings.ApiKey, "Editing the draft key should not persist until confirm");
}

static IPluginContext CreateContext()
{
    return DispatchProxy.Create<IPluginContext, ContextProxy>();
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected: {expected}; Actual: {actual}");
}

public class ContextProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == nameof(IPluginContext.GetTranslation))
            return args?[0]?.ToString() ?? "";

        return GetDefault(targetMethod.ReturnType);
    }

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
