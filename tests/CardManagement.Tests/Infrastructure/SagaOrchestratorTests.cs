using CardManagement.Application.DTOs;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Persistence;
using CardManagement.Infrastructure.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class SagaOrchestratorTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly SagaStateRepository _repository;
    private readonly FakeSagaEventPublisher _eventPublisher;
    private readonly FakeDelayProvider _delayProvider;
    private readonly SagaOrchestrator _orchestrator;

    public SagaOrchestratorTests()
    {
        var options = new DbContextOptionsBuilder<CardManagementDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CardManagementDbContext(options);
        _repository = new SagaStateRepository(_dbContext);
        _eventPublisher = new FakeSagaEventPublisher();
        _delayProvider = new FakeDelayProvider();

        var sagaOptions = Options.Create(new SagaOptions
        {
            MaxCompensationRetries = 3,
            InitialRetryDelayMs = 1000
        });

        _orchestrator = new SagaOrchestrator(
            _repository,
            _eventPublisher,
            _delayProvider,
            sagaOptions,
            NullLogger<SagaOrchestrator>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ReturnSuccessAndPublishesCompleted()
    {
        // Arrange
        var executionOrder = new List<string>();
        var steps = new List<SagaStepDefinition>
        {
            new("Step1", ct => { executionOrder.Add("Step1"); return Task.CompletedTask; }, ct => Task.CompletedTask),
            new("Step2", ct => { executionOrder.Add("Step2"); return Task.CompletedTask; }, ct => Task.CompletedTask),
            new("Step3", ct => { executionOrder.Add("Step3"); return Task.CompletedTask; }, ct => Task.CompletedTask)
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-001", steps, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.FailedStep);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("TX-001", result.TransactionReference);
        Assert.Equal(new List<string> { "Step1", "Step2", "Step3" }, executionOrder);

        // Verify saga-completed event published
        Assert.Single(_eventPublisher.CompletedEvents);
        Assert.Equal("TX-001", _eventPublisher.CompletedEvents[0]);

        // Verify state persisted as Completed
        var state = await _dbContext.SagaStates.FirstAsync();
        Assert.Equal(SagaStatus.Completed, state.Status);
        Assert.Equal(3, state.CurrentStepIndex);
    }

    [Fact]
    public async Task ExecuteAsync_PartialFailure_CompensatesInReverseOrder()
    {
        // Arrange
        var compensationOrder = new List<string>();
        var steps = new List<SagaStepDefinition>
        {
            new("Step1", ct => Task.CompletedTask, ct => { compensationOrder.Add("Comp1"); return Task.CompletedTask; }),
            new("Step2", ct => Task.CompletedTask, ct => { compensationOrder.Add("Comp2"); return Task.CompletedTask; }),
            new("Step3", ct => throw new InvalidOperationException("Step3 failed"), ct => { compensationOrder.Add("Comp3"); return Task.CompletedTask; })
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-002", steps, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Step3", result.FailedStep);
        Assert.Equal("Step3 failed", result.ErrorMessage);

        // Compensations run in reverse: Step2 then Step1 (Step3 failed so not compensated)
        Assert.Equal(new List<string> { "Comp2", "Comp1" }, compensationOrder);

        // Verify saga-rolled-back event published
        Assert.Single(_eventPublisher.RolledBackEvents);
        Assert.Equal("TX-002", _eventPublisher.RolledBackEvents[0].TransactionReference);
        Assert.Equal("Step3", _eventPublisher.RolledBackEvents[0].FailedStep);

        // Verify state persisted as RolledBack
        var state = await _dbContext.SagaStates.FirstAsync();
        Assert.Equal(SagaStatus.RolledBack, state.Status);
    }

    [Fact]
    public async Task ExecuteAsync_CompensationRetryWithBackoff_SucceedsAfterRetries()
    {
        // Arrange
        var compensationAttempts = 0;
        var steps = new List<SagaStepDefinition>
        {
            new("Step1", ct => Task.CompletedTask, ct =>
            {
                compensationAttempts++;
                if (compensationAttempts < 3)
                    throw new Exception("Compensation transient failure");
                return Task.CompletedTask;
            }),
            new("Step2", ct => throw new Exception("Step2 failed"), ct => Task.CompletedTask)
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-003", steps, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(3, compensationAttempts); // 2 failures + 1 success

        // Verify exponential backoff delays: 1000ms, 2000ms
        Assert.Equal(2, _delayProvider.Delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), _delayProvider.Delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), _delayProvider.Delays[1]);

        // Saga should be rolled back (compensation eventually succeeded)
        Assert.Single(_eventPublisher.RolledBackEvents);
        var state = await _dbContext.SagaStates.FirstAsync();
        Assert.Equal(SagaStatus.RolledBack, state.Status);
    }

    [Fact]
    public async Task ExecuteAsync_CompensationExhaustsRetries_FlagsManualIntervention()
    {
        // Arrange
        var steps = new List<SagaStepDefinition>
        {
            new("Step1", ct => Task.CompletedTask, ct => throw new Exception("Compensation always fails")),
            new("Step2", ct => throw new Exception("Step2 failed"), ct => Task.CompletedTask)
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-004", steps, CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        // Verify manual intervention event published
        Assert.Single(_eventPublisher.ManualInterventionEvents);
        Assert.Equal("TX-004", _eventPublisher.ManualInterventionEvents[0].TransactionReference);

        // Verify state persisted as FailedManualIntervention
        var state = await _dbContext.SagaStates.FirstAsync();
        Assert.Equal(SagaStatus.FailedManualIntervention, state.Status);

        // Verify exponential backoff: initial attempts = MaxCompensationRetries delays
        // MaxRetries = 3, so 3 delays (attempt 0 fails, delay, attempt 1 fails, delay, attempt 2 fails, delay, attempt 3 fails)
        Assert.Equal(3, _delayProvider.Delays.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), _delayProvider.Delays[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), _delayProvider.Delays[1]);
        Assert.Equal(TimeSpan.FromMilliseconds(4000), _delayProvider.Delays[2]);
    }

    [Fact]
    public async Task ExecuteAsync_StatePersistedBeforeEachStep()
    {
        // Arrange
        var persistedStates = new List<SagaStatus>();
        var persistedStepIndices = new List<int>();

        var steps = new List<SagaStepDefinition>
        {
            new("Step1", async ct =>
            {
                // Check state was persisted (initial create) before executing
                var state = await _dbContext.SagaStates.FirstOrDefaultAsync(ct);
                if (state != null)
                {
                    persistedStates.Add(state.Status);
                    persistedStepIndices.Add(state.CurrentStepIndex);
                }
            }, ct => Task.CompletedTask),
            new("Step2", async ct =>
            {
                // State should have been updated after Step1 completed
                var state = await _dbContext.SagaStates.FirstOrDefaultAsync(ct);
                if (state != null)
                {
                    persistedStates.Add(state.Status);
                    persistedStepIndices.Add(state.CurrentStepIndex);
                }
            }, ct => Task.CompletedTask)
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-005", steps, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        // Before Step1 executes, state was persisted with Running status at step 0
        Assert.Equal(SagaStatus.Running, persistedStates[0]);
        Assert.Equal(0, persistedStepIndices[0]);

        // Before Step2 executes, state was persisted with Running status at step 1
        Assert.Equal(SagaStatus.Running, persistedStates[1]);
        Assert.Equal(1, persistedStepIndices[1]);
    }

    [Fact]
    public async Task ExecuteAsync_FirstStepFails_NoCompensationsNeeded()
    {
        // Arrange
        var compensationCalled = false;
        var steps = new List<SagaStepDefinition>
        {
            new("Step1", ct => throw new Exception("First step fails"), ct => { compensationCalled = true; return Task.CompletedTask; })
        };

        // Act
        var result = await _orchestrator.ExecuteAsync("TX-006", steps, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Step1", result.FailedStep);

        // No compensations needed since no steps completed
        Assert.False(compensationCalled);

        // Saga is rolled back (no steps to compensate)
        var state = await _dbContext.SagaStates.FirstAsync();
        Assert.Equal(SagaStatus.RolledBack, state.Status);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

/// <summary>
/// Fake saga event publisher for unit testing that records published events.
/// </summary>
internal class FakeSagaEventPublisher : ISagaEventPublisher
{
    public List<string> CompletedEvents { get; } = new();
    public List<(string TransactionReference, string FailedStep, string ErrorMessage)> RolledBackEvents { get; } = new();
    public List<(string TransactionReference, string FailedStep, int RetriesExhausted)> ManualInterventionEvents { get; } = new();

    public Task PublishSagaCompletedAsync(string transactionReference, CancellationToken ct)
    {
        CompletedEvents.Add(transactionReference);
        return Task.CompletedTask;
    }

    public Task PublishSagaRolledBackAsync(string transactionReference, string failedStep, string errorMessage, CancellationToken ct)
    {
        RolledBackEvents.Add((transactionReference, failedStep, errorMessage));
        return Task.CompletedTask;
    }

    public Task PublishSagaManualInterventionRequiredAsync(string transactionReference, string failedStep, int retriesExhausted, CancellationToken ct)
    {
        ManualInterventionEvents.Add((transactionReference, failedStep, retriesExhausted));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake delay provider for unit testing that records delays without actually waiting.
/// </summary>
internal class FakeDelayProvider : IDelayProvider
{
    public List<TimeSpan> Delays { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
