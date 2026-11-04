using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Apache.Arrow;
using Apache.Arrow.C;
using SunDoge.Lance.Interop;
using NativeScanner = SunDoge.Lance.Interop.LanceScanner;

namespace SunDoge.Lance;

internal static class LanceAsyncScan
{
    internal static async IAsyncEnumerable<RecordBatch> Read(
        LanceDataset dataset,
        LanceScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new PollReader(dataset.CreateScanner(options));
        while (await reader.ReadNextAsync(cancellationToken).ConfigureAwait(false) is { } batch)
            yield return batch;
    }

    private sealed class PollReader : IDisposable
    {
        private readonly NativeHandle _scanner;
        private readonly WakeState _wakeState = new();
        private GCHandle _context;
        private bool _firstPoll = true;
        private int _disposed;

        internal PollReader(NativeHandle scanner)
        {
            _scanner = scanner;
            try { _context = GCHandle.Alloc(_wakeState); }
            catch { scanner.Dispose(); throw; }
        }

        ~PollReader() => Dispose();

        internal async ValueTask<RecordBatch?> ReadNextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wake = _wakeState.Reset();
                PollResult result;
                if (_firstPoll)
                {
                    _firstPoll = false;
                    // v0.1.9 materialize_stream blocks on query preparation. Await it
                    // before disposing the scanner, even if cancellation arrives meanwhile.
                    result = await Task.Run(Poll).ConfigureAwait(false);
                }
                else result = Poll();

                if (cancellationToken.IsCancellationRequested)
                {
                    result.Batch?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                switch (result.Status)
                {
                    case LancePollStatus.LANCE_POLL_READY: return result.Batch;
                    case LancePollStatus.LANCE_POLL_FINISHED: return null;
                    case LancePollStatus.LANCE_POLL_PENDING:
                        // A callback may fire before Poll returns. The signal is created
                        // before polling so that notification is not lost.
                        await wake.WaitAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    default: throw new InvalidOperationException("Unexpected native poll status.");
                }
            }
        }

        private unsafe PollResult Poll()
        {
            using var lease = _scanner.Acquire();
            LanceBatch* batch = null;
            var status = NativeMethods.lance_scanner_poll_next(
                (NativeScanner*)lease.Pointer, &Wake, (void*)GCHandle.ToIntPtr(_context), &batch);
            if (status == LancePollStatus.LANCE_POLL_ERROR) throw LanceException.FromLastError();
            return new PollResult(status, batch == null ? null : ImportBatch(batch));
        }

        private static unsafe RecordBatch ImportBatch(LanceBatch* batch)
        {
            ArrowArray array = default;
            ArrowSchema schema = default;
            try
            {
                LanceException.Check(NativeMethods.lance_batch_to_arrow(batch, &array, &schema));
                var managedSchema = CArrowSchemaImporter.ImportSchema((CArrowSchema*)&schema);
                return CArrowArrayImporter.ImportRecordBatch((CArrowArray*)&array, managedSchema);
            }
            finally
            {
                if (array.release != null) array.release(&array);
                if (schema.release != null) schema.release(&schema);
                NativeMethods.lance_batch_free(batch);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static unsafe void Wake(void* context)
        {
            try
            {
                // Never call back into the scanner here. Continuations are scheduled
                // asynchronously so none can close it on its own callback thread.
                ((WakeState)GCHandle.FromIntPtr((nint)context).Target!).Signal();
            }
            catch
            {
                // Managed exceptions must never escape an unmanaged callback.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // Native close cancels pending callbacks and waits for active callbacks.
            // Only after that retirement boundary may their GCHandle be freed.
            _scanner?.Dispose();
            if (_context.IsAllocated) _context.Free();
            GC.SuppressFinalize(this);
        }
    }

    private readonly record struct PollResult(LancePollStatus Status, RecordBatch? Batch);

    private sealed class WakeState
    {
        private TaskCompletionSource _signal = NewSignal();
        internal Task Reset()
        {
            var next = NewSignal();
            Volatile.Write(ref _signal, next);
            return next.Task;
        }
        internal void Signal() => Volatile.Read(ref _signal).TrySetResult();
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
