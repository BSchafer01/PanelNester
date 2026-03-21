using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Web.WebView2.Wpf;
using PanelNester.Desktop.Bridge;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class WebViewBridgeReadinessSpecs
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Open_project_waits_for_ui_ready_before_executing_script()
    {
        var completion = new TaskCompletionSource<(bool ScriptStartedBeforeReady, bool OpenResult)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var webView = new WebView2();
                var dispatcher = new BridgeMessageDispatcher();
                var bridge = new WebViewBridge(
                    webView,
                    dispatcher,
                    new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
                    Path.Combine(Path.GetTempPath(), $"PanelNester.WebViewBridgeReadiness.{Guid.NewGuid():N}"));

                var scriptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var scriptResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                bridge.ScriptExecutorOverride = (_, _) =>
                {
                    scriptStarted.TrySetResult();
                    return scriptResult.Task;
                };

                var openTask = bridge.OpenProjectAsync(@"C:\temp\startup-open.pnest");

                Thread.Sleep(TimeSpan.FromMilliseconds(50));
                var startedBeforeReady = scriptStarted.Task.IsCompleted;

                var ready = new BridgeMessageEnvelope(
                    BridgeMessageTypes.BridgeUiReady,
                    Guid.NewGuid().ToString("N"),
                    JsonSerializer.SerializeToElement(new BridgeUiReadyRequest(), SerializerOptions));
                bridge.HostReadinessGate.TrySignalReady(ready);

                scriptStarted.Task.Wait(TimeSpan.FromSeconds(1));
                scriptResult.TrySetResult("true");

                var openResult = openTask.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                completion.TrySetResult((startedBeforeReady, openResult));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        var (scriptStartedBeforeReady, openResult) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        thread.Join(TimeSpan.FromSeconds(1));

        Assert.False(scriptStartedBeforeReady);
        Assert.True(openResult);
    }
}
