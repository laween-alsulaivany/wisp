using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wisp.App.ViewModels;
using Wisp.Core.Enums;
using Wisp.Core.Interfaces;
using Xunit;

namespace Wisp.App.Tests;

public sealed class FeedbackViewModelTests
{
    [Theory]
    [InlineData(3, FeedbackVariant.Silent)]
    [InlineData(10, FeedbackVariant.Quick)]
    [InlineData(45, FeedbackVariant.Normal)]
    [InlineData(5, FeedbackVariant.Quick)]
    [InlineData(15, FeedbackVariant.Quick)]
    [InlineData(15.01, FeedbackVariant.Normal)]
    public void Duration_alone_selects_the_variant(double minutes, FeedbackVariant expected)
    {
        var states = Substitute.For<IGameStateService>();
        var model = Create(minutes, states);
        Assert.Equal(expected, model.Variant);
        Assert.Equal(expected != FeedbackVariant.Silent, model.ShouldShow);
        Assert.Empty(states.ReceivedCalls());
        if (expected == FeedbackVariant.Silent)
        {
            Assert.Empty(model.Options);
            Assert.False(model.DismissCommand.CanExecute(null));
        }
    }

    [Theory]
    [InlineData(10, FeedbackType.KeepGoing)]
    [InlineData(10, FeedbackType.NotFeelingIt)]
    [InlineData(10, FeedbackType.TechnicalIssue)]
    [InlineData(10, FeedbackType.Interrupted)]
    [InlineData(45, FeedbackType.KeepGoing)]
    [InlineData(45, FeedbackType.MaybeLater)]
    [InlineData(45, FeedbackType.Finished)]
    [InlineData(45, FeedbackType.Dropped)]
    public async Task Buttons_delegate_exact_feedback_and_session_to_the_service(double minutes, FeedbackType type)
    {
        var states = Substitute.For<IGameStateService>();
        var model = Create(minutes, states);
        await model.SubmitCommand.ExecuteAsync(model.Options.Single(o => o.Type == type));
        await states.Received(1).ApplyFeedbackAsync(42, type, Arg.Any<CancellationToken>());
        Assert.True(model.IsCompleted);
        Assert.False(model.DismissCommand.CanExecute(null));
        Assert.Single(states.ReceivedCalls());
    }

    [Fact]
    public async Task Ignoring_feedback_records_pending_once_and_closes_without_resubmitting()
    {
        var states = Substitute.For<IGameStateService>();
        var model = Create(45, states);
        var closed = 0;
        model.Completed += (_, _) => closed++;
        await model.DismissCommand.ExecuteAsync(null);
        await model.DismissCommand.ExecuteAsync(null);
        await states.Received(1).ApplyFeedbackAsync(42, FeedbackType.Pending, Arg.Any<CancellationToken>());
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Failed_save_allows_retry_without_closing()
    {
        var states = Substitute.For<IGameStateService>();
        states.ApplyFeedbackAsync(42, FeedbackType.Pending, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException()));
        var model = Create(45, states);
        await model.DismissCommand.ExecuteAsync(null);
        Assert.False(model.IsCompleted);
        Assert.True(model.DismissCommand.CanExecute(null));
        Assert.NotEmpty(model.Status);
    }

    private static FeedbackViewModel Create(double minutes, IGameStateService states) =>
        new(42, TimeSpan.FromMinutes(minutes), states, NullLogger<FeedbackViewModel>.Instance);
}
