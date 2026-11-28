using System;
using AIA.Plugins.SDK;
using Xunit;

namespace AIA.Plugins.Tests;

public class SyncReconcilerTests
{
    private static readonly DateTime BaselineDueUtc = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private const SyncCapabilities ReadCapabilities =
        SyncCapabilities.ReadSourceCompletion | SyncCapabilities.ReadSourceDueDate;

    private const SyncCapabilities FullCapabilities =
        ReadCapabilities | SyncCapabilities.WriteSourceCompletion | SyncCapabilities.WriteSourceDueDate;

    [Fact]
    public void Decide_ReturnsNone_WhenNothingChanged()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc,
            FullCapabilities);

        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void Decide_ReturnsSourceToAia_WhenOnlySourceCompletionChanged()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(completed: true),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc,
            FullCapabilities);

        Assert.Equal(SyncAction.SourceToAia, action);
    }

    [Fact]
    public void Decide_ReturnsSourceToAia_WhenOnlySourceDueChanged()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(dueUtc: BaselineDueUtc.AddDays(1)),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc,
            FullCapabilities);

        Assert.Equal(SyncAction.SourceToAia, action);
    }

    [Fact]
    public void Decide_ReturnsAiaToSource_WhenOnlyAiaCompletionChangedAndWritable()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(),
            aiaCompleted: true,
            aiaDueUtc: BaselineDueUtc,
            FullCapabilities);

        Assert.Equal(SyncAction.AiaToSource, action);
    }

    [Fact]
    public void Decide_ReturnsNone_WhenOnlyAiaDueChangedButSourceDueNotWritable()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc.AddDays(1),
            ReadCapabilities | SyncCapabilities.WriteSourceCompletion);

        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void Decide_ReturnsNone_WhenOnlyAiaCompletionChangedButSourceNotWritable()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(),
            aiaCompleted: true,
            aiaDueUtc: BaselineDueUtc,
            ReadCapabilities);

        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void Decide_ReturnsConflictAiaWins_WhenBothSidesChanged()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(completed: true, dueUtc: BaselineDueUtc.AddDays(1)),
            aiaCompleted: true,
            aiaDueUtc: BaselineDueUtc.AddDays(2),
            FullCapabilities);

        Assert.Equal(SyncAction.ConflictAiaWins, action);
    }

    [Fact]
    public void Decide_ReturnsNone_WhenDueDatesDifferOnlyBySeconds()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(lastSourceDueUtc: BaselineDueUtc),
            CreateSource(dueUtc: BaselineDueUtc.AddSeconds(42)),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc.AddSeconds(59),
            FullCapabilities);

        Assert.Equal(SyncAction.None, action);
        Assert.Equal(
            SyncReconciler.Normalize(BaselineDueUtc),
            SyncReconciler.Normalize(BaselineDueUtc.AddSeconds(59)));
    }

    [Fact]
    public void Decide_IgnoresSourceCompletion_WhenNotReadable()
    {
        var action = SyncReconciler.Decide(
            CreateRecord(),
            CreateSource(completed: true),
            aiaCompleted: false,
            aiaDueUtc: BaselineDueUtc,
            SyncCapabilities.ReadSourceDueDate);

        Assert.Equal(SyncAction.None, action);
    }

    private static SyncRecord CreateRecord(
        bool? lastSourceCompleted = false,
        DateTime? lastSourceDueUtc = null,
        bool? lastAiaCompleted = false,
        DateTime? lastAiaDueUtc = null)
    {
        return new SyncRecord
        {
            SourceKey = "test-source",
            SourceId = "item-1",
            SourceUrl = "https://example.com/items/1",
            TargetKind = SyncTargetKind.Task,
            AiaId = Guid.NewGuid(),
            LastSourceCompleted = lastSourceCompleted,
            LastSourceDueUtc = lastSourceDueUtc ?? BaselineDueUtc,
            LastAiaCompleted = lastAiaCompleted,
            LastAiaDueUtc = lastAiaDueUtc ?? BaselineDueUtc,
            LastSyncedUtc = BaselineDueUtc
        };
    }

    private static ExternalSyncState CreateSource(bool? completed = false, DateTime? dueUtc = null)
    {
        return new ExternalSyncState
        {
            SourceId = "item-1",
            SourceUrl = "https://example.com/items/1",
            Title = "Item 1",
            IsCompleted = completed,
            DueUtc = dueUtc ?? BaselineDueUtc
        };
    }
}
