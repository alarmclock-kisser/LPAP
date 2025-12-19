using ManagedCuda.BasicTypes;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace LPAP.Cuda;

internal sealed class CudaMem : IDisposable
{
	public Guid Id { get; } = Guid.NewGuid();

	public CUdeviceptr[] DevicePointers { get; private set; } = [];
	public IntPtr[] Pointers { get; private set; } = [];
	public IntPtr[] Lengths { get; private set; } = [];

	public IntPtr IndexPointer { get; private set; } = IntPtr.Zero;
	public IntPtr IndexLength { get; private set; } = IntPtr.Zero;

	public Type ElementType { get; private set; } = typeof(void);
	public int ElementSize { get; private set; }
	public long TotalLength { get; private set; }
	public long TotalSize { get; private set; }

	public string Message { get; set; } = string.Empty;

	public int Count => this.Pointers.Length;

	public CUdeviceptr? this[long index]
		=> index >= 0 && index < this.DevicePointers.LongLength ? this.DevicePointers[index] : null;

	public CudaMem(CUdeviceptr pointer, IntPtr length, Type elementType)
	{
		if (pointer.Pointer == IntPtr.Zero)
		{
			throw new ArgumentException("Pointer must not be zero.", nameof(pointer));
		}
		if (length == IntPtr.Zero || length.ToInt64() <= 0)
		{
			throw new ArgumentException("Length must be positive.", nameof(length));
		}

		this.DevicePointers = [pointer];
		this.Pointers = [(IntPtr) pointer.Pointer];
		this.Lengths = [length];
		this.ElementType = elementType;

		this.UpdateProperties();
	}

	public CudaMem(CUdeviceptr[] pointers, IntPtr[] lengths, Type elementType)
	{
		if (pointers.Length != lengths.Length || pointers.Length == 0)
		{
			throw new ArgumentException("Pointers and lengths must be non-empty and aligned.");
		}

		if (pointers.Any(p => p.Pointer == IntPtr.Zero))
		{
			throw new ArgumentException("Pointers must not contain zero handles.", nameof(pointers));
		}
		if (lengths.Any(l => l == IntPtr.Zero || l.ToInt64() <= 0))
		{
			throw new ArgumentException("Lengths must be positive.", nameof(lengths));
		}

		this.DevicePointers = pointers.ToArray();
		this.Pointers = pointers.Select(p => (IntPtr) p.Pointer).ToArray();
		this.Lengths = lengths.ToArray();
		this.ElementType = elementType;

		this.UpdateProperties();
	}

	public void Dispose()
	{
		this.DevicePointers = [];
		this.Pointers = [];
		this.Lengths = [];
		this.IndexPointer = IntPtr.Zero;
		this.IndexLength = IntPtr.Zero;
		this.TotalLength = 0;
		this.TotalSize = 0;
		this.ElementType = typeof(void);
		this.ElementSize = 0;
		this.Message = string.Empty;

		GC.SuppressFinalize(this);
	}

	private void UpdateProperties()
	{
		this.ElementSize = Marshal.SizeOf(this.ElementType);
		this.TotalLength = this.Lengths.Sum(static len => len.ToInt64());
		this.TotalSize = this.TotalLength * this.ElementSize;
		this.IndexPointer = this.Pointers.FirstOrDefault(IntPtr.Zero);
		this.IndexLength = this.Lengths.FirstOrDefault(IntPtr.Zero);
	}
}
