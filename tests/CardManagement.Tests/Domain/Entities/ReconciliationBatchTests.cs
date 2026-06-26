using CardManagement.Domain.PlatformServices.Reconciliation;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class ReconciliationBatchTests
{
    [Fact]
    public void Create_ValidInput_CreatesBatchWithPendingStatus()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS,
            new DateOnly(2024, 1, 15),
            "abc123hash",
            100,
            "operator@example.com");

        Assert.NotEqual(Guid.Empty, batch.Id);
        Assert.Equal(ProcessorType.NIBSS, batch.Processor);
        Assert.Equal(new DateOnly(2024, 1, 15), batch.SettlementDate);
        Assert.Equal("abc123hash", batch.FileHash);
        Assert.Equal(BatchStatus.Pending, batch.Status);
        Assert.Equal(100, batch.TotalRows);
        Assert.Equal(0, batch.ParsedRows);
        Assert.Equal(0, batch.ErrorRows);
        Assert.Equal("operator@example.com", batch.CreatedBy);
        Assert.Null(batch.CompletedAtUtc);
    }

    [Fact]
    public void Create_EmptyFileHash_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationBatch.Create(ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "", 100, "user"));
    }

    [Fact]
    public void Create_NegativeTotalRows_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationBatch.Create(ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", -1, "user"));
    }

    [Fact]
    public void Create_EmptyCreatedBy_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationBatch.Create(ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, ""));
    }

    [Fact]
    public void StartProcessing_FromPending_TransitionsToProcessing()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.Interswitch, new DateOnly(2024, 2, 1), "hash", 50, "user");

        batch.StartProcessing();

        Assert.Equal(BatchStatus.Processing, batch.Status);
    }

    [Fact]
    public void StartProcessing_FromNonPending_ThrowsInvalidOperationException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();

        Assert.Throws<InvalidOperationException>(() => batch.StartProcessing());
    }

    [Fact]
    public void RecordParseProgress_ValidValues_UpdatesRowCounts()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.Cardify, new DateOnly(2024, 3, 10), "hash", 100, "user");
        batch.StartProcessing();

        batch.RecordParseProgress(90, 10);

        Assert.Equal(90, batch.ParsedRows);
        Assert.Equal(10, batch.ErrorRows);
    }

    [Fact]
    public void RecordParseProgress_NotProcessing_ThrowsInvalidOperationException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");

        Assert.Throws<InvalidOperationException>(() => batch.RecordParseProgress(50, 5));
    }

    [Fact]
    public void RecordParseProgress_ExceedsTotalRows_ThrowsArgumentException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();

        Assert.Throws<ArgumentException>(() => batch.RecordParseProgress(90, 20));
    }

    [Fact]
    public void RecordParseProgress_NegativeParsedRows_ThrowsArgumentException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();

        Assert.Throws<ArgumentException>(() => batch.RecordParseProgress(-1, 5));
    }

    [Fact]
    public void MarkCompleted_FromProcessing_TransitionsToCompleted()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();

        batch.MarkCompleted();

        Assert.Equal(BatchStatus.Completed, batch.Status);
        Assert.NotNull(batch.CompletedAtUtc);
    }

    [Fact]
    public void MarkCompleted_FromPending_ThrowsInvalidOperationException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");

        Assert.Throws<InvalidOperationException>(() => batch.MarkCompleted());
    }

    [Fact]
    public void MarkFailed_FromPending_TransitionsToFailed()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");

        batch.MarkFailed();

        Assert.Equal(BatchStatus.Failed, batch.Status);
        Assert.NotNull(batch.CompletedAtUtc);
    }

    [Fact]
    public void MarkFailed_FromProcessing_TransitionsToFailed()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();

        batch.MarkFailed();

        Assert.Equal(BatchStatus.Failed, batch.Status);
    }

    [Fact]
    public void MarkFailed_FromCompleted_ThrowsInvalidOperationException()
    {
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS, new DateOnly(2024, 1, 15), "hash", 100, "user");
        batch.StartProcessing();
        batch.MarkCompleted();

        Assert.Throws<InvalidOperationException>(() => batch.MarkFailed());
    }
}
