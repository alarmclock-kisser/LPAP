namespace LPAP.OpenVino.Models
{
	public sealed record OpenVinoModelInfo(
		string Name,
		string XmlPath,
		string? BinPath,
		IReadOnlyList<OpenVinoPortInfo> Inputs,
		IReadOnlyList<OpenVinoPortInfo> Outputs
	);

	public sealed record OpenVinoPortInfo(
		string? AnyName,
		long[] Shape,
		string ElementType
	);
}
