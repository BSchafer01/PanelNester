using System.Collections.Concurrent;
using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Bridge;

internal sealed class CutPlanGenerationCoordinator
{
    private readonly ConcurrentDictionary<string, Operation> _operations =
        new(StringComparer.Ordinal);

    public Operation Begin(string operationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var operation = new Operation(this, operationId, cancellationToken);
        if (!_operations.TryAdd(operationId, operation))
        {
            operation.Dispose();
            throw new BridgeDispatchException(
                "cut-plan-generation-active",
                $"Cut Plan generation operation '{operationId}' is already active.");
        }

        return operation;
    }

    public bool Cancel(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return false;
        }

        return operation.TryCancel();
    }

    public StockLengthGenerationProgress? GetProgress(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return _operations.TryGetValue(operationId, out var operation)
            ? operation.GetProgress()
            : null;
    }

    private void Complete(string operationId, Operation operation) =>
        _operations.TryRemove(new KeyValuePair<string, Operation>(operationId, operation));

    internal sealed class Operation : IProgress<StockLengthGenerationProgress>, IDisposable
    {
        private readonly CutPlanGenerationCoordinator _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly object _sync = new();
        private StockLengthGenerationProgress _progress = new()
        {
            Phase = StockLengthGenerationProgressPhase.OptimizationGroups,
            Label = "Preparing Cut Plan generation"
        };
        private bool _disposed;

        internal Operation(
            CutPlanGenerationCoordinator owner,
            string operationId,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            OperationId = operationId;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public string OperationId { get; }

        public CancellationToken Token => _cancellation.Token;

        public void Report(StockLengthGenerationProgress value)
        {
            lock (_sync)
            {
                _progress = value;
            }
        }

        public StockLengthGenerationProgress GetProgress()
        {
            lock (_sync)
            {
                return _progress;
            }
        }

        public bool TryCancel()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }
            _owner.Complete(OperationId, this);
            _cancellation.Dispose();
        }
    }
}
