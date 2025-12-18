namespace LPAP.Onnx.Runtime;

public sealed record OnnxOptions
{
    public int DeviceId { get; init; } = 0;

    /// <summary>
    /// Max number of concurrent inference workers using the same model instance.
    /// For GPU, 1 is often best; 2 can help on some cards if VRAM allows.
    /// </summary>
    public int WorkerCount { get; init; } = 1;

    /// <summary>
    /// Bounded queue capacity for backpressure.
    /// </summary>
    public int QueueCapacity { get; init; } = 8;

    /// <summary>
    /// When true, try CUDA EP; otherwise CPU.
    /// </summary>
    public bool PreferCuda { get; init; } = true;

    /// <summary>
    /// Graph optimization level for ORT.
    /// </summary>
    public Microsoft.ML.OnnxRuntime.GraphOptimizationLevel GraphOptimizationLevel { get; init; }
        = Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL;
}
