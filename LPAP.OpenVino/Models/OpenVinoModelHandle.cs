using OpenVinoSharp;

namespace LPAP.OpenVino.Models
{
	public sealed class OpenVinoModelHandle : IDisposable
	{
		public string Name { get; }
		public string XmlPath { get; }
		public string Device { get; }

		public Model Model { get; }
		public CompiledModel Compiled { get; }

		public OpenVinoPortInfo[] Inputs { get; }
		public OpenVinoPortInfo[] Outputs { get; }

		internal OpenVinoModelHandle(
			string name,
			string xmlPath,
			string device,
			Model model,
			CompiledModel compiled,
			OpenVinoPortInfo[] inputs,
			OpenVinoPortInfo[] outputs)
		{
			this.Name = name;
			this.XmlPath = xmlPath;
			this.Device = device;
			this.Model = model;
			this.Compiled = compiled;
			this.Inputs = inputs;
			this.Outputs = outputs;
		}

		public void Dispose()
		{
			// dispose order: infer requests are created per call; compiled+model here
			this.Compiled.Dispose();
			this.Model.Dispose();
		}
	}
}
