using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RttyDecoder.Core.DeepRtty;

/// <summary>
/// Modelul DeepRTTY pe ONNX Runtime. Implicit se incarca din resursa inclusa in DLL; se poate da si
/// calea unui alt fisier .onnx (de ex. o versiune mai noua a modelului).
/// </summary>
public sealed class DeepRttyModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly object _lock = new();

    private DeepRttyModel(InferenceSession session)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
    }

    private static SessionOptions Options(int intraOpThreads)
    {
        var o = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (intraOpThreads > 0) o.IntraOpNumThreads = intraOpThreads;
        return o;
    }

    /// <summary>Incarca modelul inclus in RttyDecoder.Core.dll (cateva sute de ms).</summary>
    public static DeepRttyModel LoadEmbedded(int intraOpThreads = 2)
    {
        using var s = typeof(DeepRttyModel).Assembly.GetManifestResourceStream(DeepRttyConst.ModelResource)
                      ?? throw new InvalidOperationException("Modelul DeepRTTY lipseste din resursele assembly-ului.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        using var options = Options(intraOpThreads);
        return new DeepRttyModel(new InferenceSession(ms.ToArray(), options));
    }

    /// <summary>Incarca un model .onnx de pe disc.</summary>
    public static DeepRttyModel LoadFile(string path, int intraOpThreads = 2)
    {
        using var options = Options(intraOpThreads);
        return new DeepRttyModel(new InferenceSession(path, options));
    }

    /// <summary>Inferenta [1, 1, T, 13] -&gt; [1, T', 33]. Thread-safe.</summary>
    public (float[] Data, long[] Dims)? Run(float[] input, long[] dims)
    {
        var tensor = new DenseTensor<float>(input, dims.Select(d => (int)d).ToArray());
        lock (_lock)
        {
            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
            var output = results.First().AsTensor<float>();
            return (output.ToArray(), output.Dimensions.ToArray().Select(d => (long)d).ToArray());
        }
    }

    public void Dispose() => _session.Dispose();
}
