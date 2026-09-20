using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.Services.Hosting;
using Wisp.Core.Dtos;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class UpdateWorkerTests
{
    [Fact]
    public async Task Checks_on_startup_publishes_result_and_stops_without_another_request()
    {
        var checker = Substitute.For<IUpdateChecker>();
        var result = new UpdateCheckResult(true, "https://github.com/laween-alsulaivany/wisp/releases/tag/v2.0.0");
        checker.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns(result);
        var status = new UpdateStatus();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        status.Changed += (_, _) => published.TrySetResult();
        using var worker = new UpdateCheckWorker(checker, status, NullLogger<UpdateCheckWorker>.Instance);
        try
        {
            await worker.StartAsync(default);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            status.Current.Should().Be(result);
        }
        finally { await worker.StopAsync(default); }
        await checker.Received(1).CheckForUpdateAsync(Arg.Any<CancellationToken>());
    }
}
