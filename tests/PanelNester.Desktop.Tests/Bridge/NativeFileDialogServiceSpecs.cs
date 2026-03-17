using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PanelNester.Desktop.Bridge;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class NativeFileDialogServiceSpecs
{
    [Fact]
    public async Task Save_async_marshals_to_the_ui_dispatcher_and_returns_the_renamed_pdf_path()
    {
        using var harness = DispatcherHarness.Start();
        var renamedPath = Path.Combine(Path.GetTempPath(), $"renamed-{Guid.NewGuid():N}.pdf");

        Window? capturedOwner = null;
        string? capturedFileName = null;
        string? capturedDefaultExtension = null;
        string? capturedFilter = null;
        var invokedThreadId = 0;

        var service = new NativeFileDialogService(
            harness.Dispatcher,
            () => harness.OwnerWindow,
            static (_, _) => throw new NotSupportedException(),
            (dialog, owner) =>
            {
                invokedThreadId = Environment.CurrentManagedThreadId;
                capturedOwner = owner;
                capturedFileName = dialog.FileName;
                capturedDefaultExtension = dialog.DefaultExt;
                capturedFilter = dialog.Filter;
                dialog.FileName = renamedPath;
                return true;
            });

        var response = await service.SaveAsync(
            new SaveFileDialogRequest(
                "Export PanelNester PDF report",
                "Workshop Cabinets Nesting Report.pdf",
                [new FileDialogFilter("PDF files", ["pdf"])],
                ".pdf"));

        Assert.True(response.Success);
        Assert.Equal(renamedPath, response.FilePath);
        Assert.Equal(harness.DispatcherThreadId, invokedThreadId);
        Assert.Same(harness.OwnerWindow, capturedOwner);
        Assert.Equal("Workshop Cabinets Nesting Report.pdf", capturedFileName);
        Assert.Equal("pdf", capturedDefaultExtension);
        Assert.Contains("*.pdf", capturedFilter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_async_serializes_cancel_and_retry_requests()
    {
        var retryPath = Path.Combine(Path.GetTempPath(), $"retry-{Guid.NewGuid():N}.pdf");
        var firstDialogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstDialogToClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDialogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        var service = new NativeFileDialogService(
            null,
            static () => null,
            static (_, _) => throw new NotSupportedException(),
            (dialog, _) =>
            {
                var current = Interlocked.Increment(ref invocationCount);
                if (current == 1)
                {
                    firstDialogEntered.TrySetResult();
                    allowFirstDialogToClose.Task.GetAwaiter().GetResult();
                    return false;
                }

                secondDialogEntered.TrySetResult();
                dialog.FileName = retryPath;
                return true;
            });

        var request = new SaveFileDialogRequest(
            "Export PanelNester PDF report",
            "Retry Test.pdf",
            [new FileDialogFilter("PDF files", ["pdf"])],
            ".pdf");

        var cancelledTask = Task.Run(() => service.SaveAsync(request));
        await firstDialogEntered.Task;

        var retriedTask = Task.Run(() => service.SaveAsync(request));
        await Task.WhenAny(secondDialogEntered.Task, Task.Delay(TimeSpan.FromMilliseconds(100)));

        Assert.False(secondDialogEntered.Task.IsCompleted);

        allowFirstDialogToClose.TrySetResult();

        var cancelled = await cancelledTask;
        var retried = await retriedTask;

        Assert.False(cancelled.Success);
        Assert.Equal("cancelled", cancelled.Error!.Code);
        Assert.Null(cancelled.Error.UserMessage);
        Assert.True(retried.Success);
        Assert.Equal(retryPath, retried.FilePath);
        Assert.Equal(2, invocationCount);
    }

    [Fact]
    public async Task Open_async_serializes_rapid_cancel_and_retry_without_deadlock()
    {
        var selectedPath = Path.Combine(Path.GetTempPath(), $"open-{Guid.NewGuid():N}.pnest");
        var firstDialogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstDialogToClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDialogEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        var service = new NativeFileDialogService(
            null,
            static () => null,
            (dialog, _) =>
            {
                var current = Interlocked.Increment(ref invocationCount);
                if (current == 1)
                {
                    firstDialogEntered.TrySetResult();
                    allowFirstDialogToClose.Task.GetAwaiter().GetResult();
                    return false;
                }

                secondDialogEntered.TrySetResult();
                dialog.FileName = selectedPath;
                return true;
            },
            static (_, _) => throw new NotSupportedException());

        var request = new OpenFileDialogRequest("Open PanelNester project", null);

        var cancelledTask = Task.Run(() => service.OpenAsync(request));
        await firstDialogEntered.Task;

        var retriedTask = Task.Run(() => service.OpenAsync(request));
        await Task.WhenAny(secondDialogEntered.Task, Task.Delay(TimeSpan.FromMilliseconds(100)));

        Assert.False(secondDialogEntered.Task.IsCompleted);

        allowFirstDialogToClose.TrySetResult();

        var cancelled = await cancelledTask;
        var retried = await retriedTask;

        Assert.False(cancelled.Success);
        Assert.Equal("cancelled", cancelled.Error!.Code);
        Assert.True(retried.Success);
        Assert.Equal(selectedPath, retried.FilePath);
        Assert.Equal(2, invocationCount);
    }

    private sealed class DispatcherHarness : IDisposable
    {
        private readonly Thread _thread;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private DispatcherHarness(Thread thread)
        {
            _thread = thread;
        }

        public Dispatcher Dispatcher { get; private set; } = null!;

        public Window OwnerWindow { get; private set; } = null!;

        public int DispatcherThreadId { get; private set; }

        public static DispatcherHarness Start()
        {
            DispatcherHarness? harness = null;
            var thread = new Thread(() =>
            {
                harness!.DispatcherThreadId = Environment.CurrentManagedThreadId;
                harness.Dispatcher = Dispatcher.CurrentDispatcher;
                harness.OwnerWindow = new Window();
                harness.ready.SetResult();
                Dispatcher.Run();
            });

            harness = new DispatcherHarness(thread);
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            harness.ready.Task.GetAwaiter().GetResult();
            return harness;
        }

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join();
        }
    }
}
