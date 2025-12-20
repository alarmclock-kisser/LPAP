namespace LPAP.OpenVino.Util
{
	public sealed record OpenVinoDeviceInfo(
		string DeviceId,          // e.g. "GPU.0" / "CPU"
		string? FullDeviceName,   // e.g. "Intel(R) Arc(TM) A770 Graphics"
		string? DeviceName        // fallback/alternate
	);
}
