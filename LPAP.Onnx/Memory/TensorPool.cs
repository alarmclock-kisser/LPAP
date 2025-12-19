using System.Buffers;

namespace LPAP.Onnx.Memory;

/// <summary>
/// Minimal pool for float buffers used by audio/tensors. Keeps allocations low.
/// </summary>
public sealed class TensorPool
{
	private readonly ArrayPool<float> _pool = ArrayPool<float>.Shared;

	public float[] Rent(int length) => this._pool.Rent(length);
	public void Return(float[] buffer, bool clear = false) => this._pool.Return(buffer, clearArray: clear);
}
