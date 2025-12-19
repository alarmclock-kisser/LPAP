using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.VectorTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LPAP.Cuda;

internal sealed class CudaRegister : IDisposable
{
	private readonly PrimaryContext _ctx;
	private readonly ConcurrentDictionary<Guid, CudaMem> _memory = new();
	private readonly ConcurrentDictionary<CudaStream, int> _streams = new();
	private bool _disposed;

	internal CudaRegister(PrimaryContext ctx)
	{
		this._ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
		this.EnsureContext();
	}

	public long TotalFree => this.GetTotalFreeMemory();
	public long TotalMemory => this.GetTotalMemory();
	public long TotalAllocated => this._memory.Values.Sum(m => m.TotalSize);
	public int RegisteredMemoryObjects => this._memory.Count;
	public int ThreadsActive => this._streams.Count(kv => kv.Value > 0);
	public int ThreadsIdle => this._streams.Count(kv => kv.Value <= 0);
	public int MaxThreads => this.GetProcessorsCount();

	public IReadOnlyCollection<CudaMem> Memory => (IReadOnlyCollection<CudaMem>) this._memory.Values;

	public CudaMem? this[Guid id] => this._memory.TryGetValue(id, out var mem) ? mem : null;

	public CudaMem? this[IntPtr indexPointer]
		=> this._memory.Values.FirstOrDefault(m => m.IndexPointer == indexPointer);

	public void Dispose()
	{
		if (this._disposed)
		{
			return;
		}

		this._disposed = true;

		foreach (var mem in this._memory.Values.ToArray())
		{
			this.FreeMemory(mem);
		}
		this._memory.Clear();

		foreach (var stream in this._streams.Keys)
		{
			try
			{
				stream.Dispose();
			}
			catch (Exception ex)
			{
				CudaLog.Warn("Failed to dispose CUDA stream", ex.Message);
			}
		}

		this._streams.Clear();
		GC.SuppressFinalize(this);
	}

	private void EnsureContext()
	{
		this._ctx.SetCurrent();
	}

	public long GetTotalMemory()
	{
		this.EnsureContext();
		return this._ctx.GetTotalDeviceMemorySize();
	}

	public long GetTotalFreeMemory()
	{
		this.EnsureContext();
		return this._ctx.GetFreeDeviceMemorySize();
	}

	public int GetProcessorsCount()
	{
		this.EnsureContext();
		return this._ctx.GetDeviceInfo().MultiProcessorCount;
	}

	public long FreeMemory(CudaMem mem)
	{
		this.EnsureContext();
		long freed = mem.TotalSize;

		foreach (var devicePtr in mem.DevicePointers)
		{
			try
			{
				this._ctx.FreeMemory(devicePtr);
			}
			catch (Exception ex)
			{
				CudaLog.Warn("Failed to free device pointer", ex.Message);
			}
		}

		if (this._memory.TryRemove(mem.Id, out _))
		{
			mem.Dispose();
		}
		else
		{
			freed = -freed;
		}

		return freed;
	}

	public long FreeMemory(Guid id)
	{
		var mem = this[id];
		return mem == null ? 0 : this.FreeMemory(mem);
	}

	public long FreeMemory(IntPtr indexPointer)
	{
		var mem = this[indexPointer];
		return mem == null ? 0 : this.FreeMemory(mem);
	}

	public CudaStream? GetStream(ulong? id = null)
	{
		this.EnsureContext();
		CudaStream? stream = null;

		if (id.HasValue)
		{
			stream = this._streams.Keys.FirstOrDefault(s => s.ID == id.Value);
			if (stream == null)
			{
				CudaLog.Warn($"Stream {id.Value} not found");
			}

			return stream;
		}

		int engines = this._ctx.GetDeviceInfo().AsyncEngineCount;
		if (this._streams.Count < engines)
		{
			stream = this.CreateStream();
			if (stream != null)
			{
				return stream;
			}
		}

		stream = this._streams.OrderBy(kv => kv.Value).FirstOrDefault().Key;
		if (stream == null)
		{
			CudaLog.Warn("No CUDA streams available");
		}

		return stream;
	}

	internal IEnumerable<CudaStream>? GetManyStreams(int maxCount = 0, IEnumerable<ulong>? ids = null)
	{
		this.EnsureContext();
		if (maxCount <= 0)
		{
			maxCount = Math.Max(1, this.MaxThreads - this._streams.Count);
		}
		if (maxCount <= 0)
		{
			return null;
		}

		var result = new List<CudaStream>(maxCount);
		var idList = ids?.ToList();
		for (int i = 0; i < maxCount; i++)
		{
			CudaStream? stream;
			if (idList != null && i < idList.Count)
			{
				stream = this.GetStream(idList[i]);
			}
			else
			{
				stream = this.GetStream();
			}

			if (stream == null)
			{
				break;
			}

			result.Add(stream);
		}

		return result.Count == maxCount ? result : null;
	}

	internal async Task<IEnumerable<CudaStream>?> GetManyStreamsAsync(int maxCount = 0, IEnumerable<ulong>? ids = null)
	{
		var streams = this.GetManyStreams(maxCount, ids);
		if (streams == null)
		{
			return null;
		}

		foreach (var stream in streams)
		{
			if (!this._streams.ContainsKey(stream) && this._streams.TryAdd(stream, 0))
			{
				await Task.Run(stream.Synchronize).ConfigureAwait(false);
			}
		}

		return streams;
	}

	private CudaStream? CreateStream()
	{
		try
		{
			var stream = new CudaStream();
			if (this._streams.TryAdd(stream, 0))
			{
				stream.Synchronize();
				return stream;
			}

			stream.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to create CUDA stream", ex.Message);
		}

		return null;
	}

	public CudaMem? AllocateSingle<T>(IntPtr length) where T : unmanaged
	{
		this.EnsureContext();
		if (length == IntPtr.Zero || length.ToInt64() <= 0)
		{
			return null;
		}

		try
		{
			var deviceVariable = new CudaDeviceVariable<T>((long) length);
			var mem = new CudaMem(deviceVariable.DevicePointer, length, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			deviceVariable.Dispose();
			mem.Dispose();
			CudaLog.Warn("Failed to track allocated CUDA memory");
		}
		catch (Exception ex)
		{
			CudaLog.Error("Allocation failed", ex.Message);
		}

		return null;
	}

	public async Task<CudaMem?> AllocateSingleAsync<T>(IntPtr length) where T : unmanaged
	{
		this.EnsureContext();
		if (length == IntPtr.Zero || length.ToInt64() <= 0)
		{
			return null;
		}

		var stream = this.GetStream();
		if (stream == null)
		{
			return null;
		}

		this._streams[stream]++;
		try
		{
			var deviceVariable = new CudaDeviceVariable<T>((long) length, stream);
			var mem = new CudaMem(deviceVariable.DevicePointer, length, typeof(T));
			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			deviceVariable.Dispose();
			mem.Dispose();
			CudaLog.Warn("Failed to track async allocated CUDA memory");
		}
		catch (Exception ex)
		{
			CudaLog.Error("Async allocation failed", ex.Message);
		}
		finally
		{
			this._streams[stream]--;
		}

		return null;
	}

	public CudaMem? AllocateGroup<T>(IntPtr[] lengths) where T : unmanaged
	{
		this.EnsureContext();
		if (lengths.Length == 0 || lengths.Any(l => l == IntPtr.Zero || l.ToInt64() <= 0))
		{
			return null;
		}

		try
		{
			var deviceVars = lengths.Select(l => new CudaDeviceVariable<T>((long) l)).ToArray();
			var pointers = deviceVars.Select(v => v.DevicePointer).ToArray();
			var mem = new CudaMem(pointers, lengths, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			foreach (var deviceVar in deviceVars)
			{
				deviceVar.Dispose();
			}
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Group allocation failed", ex.Message);
		}

		return null;
	}

	public async Task<CudaMem?> AllocateGroupAsync<T>(IntPtr[] lengths) where T : unmanaged
	{
		this.EnsureContext();
		if (lengths.Length == 0 || lengths.Any(l => l == IntPtr.Zero || l.ToInt64() <= 0))
		{
			return null;
		}

		var stream = this.GetStream();
		if (stream == null)
		{
			return null;
		}

		this._streams[stream]++;
		try
		{
			var deviceVars = lengths.Select(l => new CudaDeviceVariable<T>((long) l, stream)).ToArray();
			var pointers = deviceVars.Select(v => v.DevicePointer).ToArray();
			var mem = new CudaMem(pointers, lengths, typeof(T));
			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			foreach (var deviceVar in deviceVars)
			{
				deviceVar.Dispose();
			}
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Async group allocation failed", ex.Message);
		}
		finally
		{
			this._streams[stream]--;
		}

		return null;
	}

	public CudaMem? PushData<T>(IEnumerable<T> data) where T : unmanaged
	{
		this.EnsureContext();
		var materialized = data?.ToArray();
		if (materialized == null || materialized.Length == 0)
		{
			return null;
		}

		IntPtr length = (nint) materialized.LongLength;
		try
		{
			var deviceVar = new CudaDeviceVariable<T>(materialized.Length);
			this._ctx.CopyToDevice(deviceVar.DevicePointer, materialized);
			var mem = new CudaMem(deviceVar.DevicePointer, length, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			deviceVar.Dispose();
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to push data", ex.Message);
		}

		return null;
	}

	public CudaMem? PushChunks<T>(IEnumerable<IEnumerable<T>> chunks) where T : unmanaged
	{
		this.EnsureContext();
		var chunkList = chunks?.Select(c => c?.ToArray() ?? []).ToList();
		if (chunkList == null || chunkList.Count == 0)
		{
			return null;
		}

		IntPtr[] lengths = chunkList.Select(chunk => (nint) chunk.LongLength).ToArray();
		try
		{
			var deviceVars = lengths.Select(l => new CudaDeviceVariable<T>((long) l)).ToArray();
			for (int i = 0; i < deviceVars.Length; i++)
			{
				if (chunkList[i].Length > 0)
				{
					this._ctx.CopyToDevice(deviceVars[i].DevicePointer, chunkList[i]);
				}
			}

			var mem = new CudaMem(deviceVars.Select(v => v.DevicePointer).ToArray(), lengths, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			foreach (var deviceVar in deviceVars)
			{
				deviceVar.Dispose();
			}
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to push chunks", ex.Message);
		}

		return null;
	}

	public async Task<CudaMem?> PushDataAsync<T>(IEnumerable<T> data, ulong? streamId = null) where T : unmanaged
	{
		this.EnsureContext();
		var materialized = data?.ToArray();
		if (materialized == null || materialized.Length == 0)
		{
			return null;
		}

		var stream = this.GetStream(streamId);
		if (stream == null)
		{
			return null;
		}

		this._streams[stream]++;
		try
		{
			var deviceVar = new CudaDeviceVariable<T>(materialized.Length, stream);
			deviceVar.AsyncCopyToDevice(materialized, stream);
			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			var mem = new CudaMem(deviceVar.DevicePointer, (nint) materialized.LongLength, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			deviceVar.Dispose();
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to push data asynchronously", ex.Message);
		}
		finally
		{
			this._streams[stream]--;
		}

		return null;
	}

	public async Task<CudaMem?> PushChunksAsync<T>(IEnumerable<IEnumerable<T>> chunks, ulong? streamId = null) where T : unmanaged
	{
		this.EnsureContext();
		var chunkList = chunks?.Select(chunk => chunk?.ToArray() ?? []).ToList();
		if (chunkList == null || chunkList.Count == 0)
		{
			return null;
		}

		var stream = this.GetStream(streamId);
		if (stream == null)
		{
			return null;
		}

		this._streams[stream]++;
		IntPtr[] lengths = chunkList.Select(chunk => (nint) chunk.LongLength).ToArray();
		try
		{
			var deviceVars = lengths.Select(l => new CudaDeviceVariable<T>((long) l, stream)).ToArray();
			for (int i = 0; i < deviceVars.Length; i++)
			{
				if (chunkList[i].Length > 0)
				{
					deviceVars[i].AsyncCopyToDevice(chunkList[i], stream);
				}
			}

			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			var mem = new CudaMem(deviceVars.Select(v => v.DevicePointer).ToArray(), lengths, typeof(T));
			if (this._memory.TryAdd(mem.Id, mem))
			{
				return mem;
			}

			foreach (var deviceVar in deviceVars)
			{
				deviceVar.Dispose();
			}
			mem.Dispose();
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to push chunks asynchronously", ex.Message);
		}
		finally
		{
			this._streams[stream]--;
		}

		return null;
	}

	public T[] PullData<T>(IntPtr indexPointer, bool keep = false, int groupIndex = 0) where T : unmanaged
	{
		this.EnsureContext();
		var mem = this[indexPointer];
		if (mem == null || mem.ElementType != typeof(T) || mem.IndexLength == IntPtr.Zero)
		{
			return [];
		}

		groupIndex = Math.Clamp(groupIndex, 0, mem.Count - 1);
		if (groupIndex > 0)
		{
			indexPointer = mem.Pointers[groupIndex];
		}

		long count = mem.IndexLength.ToInt64();
		if (count <= 0 || count > int.MaxValue)
		{
			return [];
		}

		T[] data = new T[(int) count];
		var deviceVar = new CudaDeviceVariable<T>(new CUdeviceptr(indexPointer), (SizeT) (count * Marshal.SizeOf<T>()));
		this._ctx.CopyToHost(data, deviceVar.DevicePointer);
		this._ctx.Synchronize();

		if (!keep)
		{
			this.FreeMemory(mem);
		}

		return data;
	}

	public List<T[]> PullChunks<T>(IntPtr indexPointer, bool keep = false) where T : unmanaged
	{
		this.EnsureContext();
		var mem = this[indexPointer];
		if (mem == null || mem.Count == 0)
		{
			return [];
		}

		List<T[]> chunks = new(mem.Count);
		for (int i = 0; i < mem.Count; i++)
		{
			long count = mem.Lengths[i].ToInt64();
			if (count <= 0 || count > int.MaxValue)
			{
				chunks.Add([]);
				continue;
			}

			var deviceVar = new CudaDeviceVariable<T>(mem.DevicePointers[i], count);
			T[] host = new T[count];
			this._ctx.CopyToHost(host, deviceVar.DevicePointer);
			chunks.Add(host);
		}

		this._ctx.Synchronize();
		if (!keep)
		{
			this.FreeMemory(mem);
		}

		return chunks;
	}

	public async Task<T[]> PullDataAsync<T>(IntPtr indexPointer, bool keep = false, ulong? streamId = null) where T : unmanaged
	{
		this.EnsureContext();
		var mem = this[indexPointer];
		if (mem == null || mem.ElementType != typeof(T))
		{
			return [];
		}

		long count = mem.IndexLength.ToInt64();
		if (count <= 0 || count > int.MaxValue)
		{
			return [];
		}

		var stream = this.GetStream(streamId);
		if (stream == null)
		{
			return [];
		}

		this._streams[stream]++;
		try
		{
			T[] data = new T[count];
			int byteSize = data.Length * Marshal.SizeOf<T>();
			unsafe
			{
				fixed (T* ptr = data)
				{
					var result = ManagedCuda.DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
						new CUdeviceptr((IntPtr) ptr),
						new CUdeviceptr(indexPointer),
						(SizeT) byteSize,
						stream.Stream);
					if (result != CUResult.Success)
					{
						throw new CudaException(result);
					}
				}
			}

			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			if (!keep)
			{
				this.FreeMemory(mem);
			}

			return data;
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to pull data asynchronously", ex.Message);
			return [];
		}
		finally
		{
			this._streams[stream]--;
		}
	}

	public async Task<List<T[]>> PullChunksAsync<T>(IntPtr indexPointer, bool keep = false, ulong? streamId = null) where T : unmanaged
	{
		this.EnsureContext();
		var mem = this[indexPointer];
		if (mem == null || mem.Count == 0)
		{
			return [];
		}

		var stream = this.GetStream(streamId);
		if (stream == null)
		{
			return [];
		}

		this._streams[stream]++;
		var chunks = new List<T[]>(mem.Count);
		try
		{
			for (int i = 0; i < mem.Count; i++)
			{
				long count = mem.Lengths[i].ToInt64();
				if (count <= 0 || count > int.MaxValue)
				{
					chunks.Add([]);
					continue;
				}

				T[] data = new T[count];
				int byteSize = data.Length * Marshal.SizeOf<T>();
				unsafe
				{
					fixed (T* ptr = data)
					{
						var result = ManagedCuda.DriverAPINativeMethods.AsynchronousMemcpy_v2.cuMemcpyAsync(
							new CUdeviceptr((IntPtr) ptr),
							new CUdeviceptr(indexPointer),
							(SizeT) byteSize,
							stream.Stream);
						if (result != CUResult.Success)
						{
							throw new CudaException(result);
						}
					}
				}

				chunks.Add(data);
			}

			await Task.Run(stream.Synchronize).ConfigureAwait(false);

			if (!keep)
			{
				this.FreeMemory(mem);
			}

			return chunks;
		}
		catch (Exception ex)
		{
			CudaLog.Error("Failed to pull chunks asynchronously", ex.Message);
			return [];
		}
		finally
		{
			this._streams[stream]--;
		}
	}
}