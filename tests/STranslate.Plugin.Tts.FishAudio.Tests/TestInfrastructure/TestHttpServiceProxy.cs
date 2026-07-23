using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

public class TestHttpServiceProxy : DispatchProxy
{
    public int GetCallCount { get; private set; }
    public int GetAsBytesCallCount { get; private set; }
    public int GetAsStreamCallCount { get; private set; }
    public int PostAsBytesCallCount { get; private set; }
    public List<string> GetUrls { get; } = [];
    public List<(string Url, Options? Options)> GetOptionsByUrl { get; } = [];
    public string GetResponseJson { get; set; } = "{\"credit\":\"1.00\"}";
    public byte[] GetBytesResponse { get; set; } = new byte[] { 9 };
    public Stream GetStreamResponse { get; set; } = new MemoryStream(new byte[] { 9 });
    public byte[] PostBytes { get; set; } = new byte[] { 1 };
    public Exception? GetException { get; set; }
    public Exception? PostException { get; set; }
    public Func<string, Options?, CancellationToken, Task<string>>? GetAsyncHandler { get; set; }
    public Func<string, object?, Options?, CancellationToken, Task<byte[]>>? PostAsBytesAsyncHandler { get; set; }
    public TaskCompletionSource GetStreamReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? LastGetUrl { get; private set; }
    public Options? LastGetOptions { get; private set; }
    public string? LastGetStreamUrl { get; private set; }
    public Options? LastGetStreamOptions { get; private set; }
    public string? LastPostUrl { get; private set; }
    public object? LastPostBody { get; private set; }
    public Options? LastPostOptions { get; private set; }

    public static (IHttpService Service, TestHttpServiceProxy Proxy) Create()
    {
        var service = DispatchProxy.Create<IHttpService, TestHttpServiceProxy>();
        return (service, (TestHttpServiceProxy)(object)service);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == nameof(IHttpService.GetAsync)
            && targetMethod.ReturnType == typeof(Task<string>))
        {
            GetCallCount++;
            LastGetUrl = GetStringArgument(args, args?.Length == 4 ? 1 : 0);
            GetUrls.Add(LastGetUrl ?? "");
            LastGetOptions = GetOptionsArgument(args);
            GetOptionsByUrl.Add((LastGetUrl ?? "", LastGetOptions));
            var ct = GetCancellationTokenArgument(args);

            if (GetAsyncHandler is not null)
                return GetAsyncHandler(LastGetUrl ?? "", LastGetOptions, ct);

            return GetException is not null
                ? Task.FromException<string>(GetException)
                : Task.FromResult(GetResponseJson);
        }

        if (targetMethod.Name == nameof(IHttpService.GetAsBytesAsync)
            && targetMethod.ReturnType == typeof(Task<byte[]>))
        {
            GetAsBytesCallCount++;
            return Task.FromResult(GetBytesResponse);
        }

        if (targetMethod.Name == nameof(IHttpService.GetAsStreamAsync)
            && targetMethod.ReturnType == typeof(Task<Stream>))
        {
            GetAsStreamCallCount++;
            var hasClientName = args?.Length == 4;
            LastGetStreamUrl = GetStringArgument(args, hasClientName ? 1 : 0);
            LastGetStreamOptions = GetOptionsArgument(args);
            GetStreamReturned.SetResult();
            return Task.FromResult(GetStreamResponse);
        }

        if (targetMethod.Name == nameof(IHttpService.PostAsBytesAsync)
            && targetMethod.ReturnType == typeof(Task<byte[]>))
        {
            PostAsBytesCallCount++;
            var hasClientName = args?.Length == 5;
            LastPostUrl = GetStringArgument(args, hasClientName ? 1 : 0);
            LastPostBody = args?[hasClientName ? 2 : 1];
            LastPostOptions = args?[hasClientName ? 3 : 2] as Options;
            if (PostAsBytesAsyncHandler is not null)
            {
                return PostAsBytesAsyncHandler(
                    LastPostUrl ?? "",
                    LastPostBody,
                    LastPostOptions,
                    GetCancellationTokenArgument(args));
            }

            return PostException is not null
                ? Task.FromException<byte[]>(PostException)
                : Task.FromResult(PostBytes);
        }

        return GetDefault(targetMethod.ReturnType);
    }

    private static string? GetStringArgument(object?[]? args, int index) =>
        args is not null && args.Length > index ? args[index] as string : null;

    private static Options? GetOptionsArgument(object?[]? args) =>
        args?.OfType<Options>().FirstOrDefault();

    private static CancellationToken GetCancellationTokenArgument(object?[]? args) =>
        args?.OfType<CancellationToken>().FirstOrDefault() ?? CancellationToken.None;

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        if (type == typeof(Task))
            return Task.CompletedTask;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = type.GetGenericArguments()[0];
            var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
