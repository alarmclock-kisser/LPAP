using System.Threading.Channels;
using Microsoft.ML.OnnxRuntime;

namespace LPAP.Onnx.Runtime;

/// <summary>
/// Bounded async inference runner: avoids Task.Run-per-call spam; provides backpressure.
/// </summary>
public sealed class OnnxRunner : IAsyncDisposable
{
	private readonly InferenceSession _session;
	private readonly Channel<WorkItem> _queue;
	private readonly List<Task> _workers;

	private sealed record WorkItem(
		IReadOnlyList<NamedOnnxValue> Inputs,
		TaskCompletionSource<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>> Tcs,
		CancellationToken Ct);

	public OnnxRunner(InferenceSession session, int workerCount, int capacity)
	{
		this._session = session;

		this._queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(Math.Max(1, capacity))
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = false,
			SingleWriter = false
		});

		workerCount = Math.Max(1, workerCount);
		this._workers = new List<Task>(workerCount);
		for (int i = 0; i < workerCount; i++)
		{
			this._workers.Add(Task.Run(this.WorkerLoop));
		}
	}

	public async Task<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>> RunAsync(
		IReadOnlyList<NamedOnnxValue> inputs,
		CancellationToken ct = default)
	{
		var tcs = new TaskCompletionSource<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		await this._queue.Writer.WriteAsync(new WorkItem(inputs, tcs, ct), ct).ConfigureAwait(false);
		return await tcs.Task.ConfigureAwait(false);
	}

	private async Task WorkerLoop()
	{
		await foreach (var wi in this._queue.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			try
			{
				wi.Ct.ThrowIfCancellationRequested();
				// ORT run is synchronous. We isolate it on a worker thread.
				var outputs = this._session.Run(wi.Inputs);
				wi.Tcs.TrySetResult(outputs);
			}
			catch (Exception ex)
			{
				wi.Tcs.TrySetException(ex);
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		this._queue.Writer.TryComplete();
		try { await Task.WhenAll(this._workers).ConfigureAwait(false); } catch { /* ignore */ }
		this._session.Dispose();
	}
}
