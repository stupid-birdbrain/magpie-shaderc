namespace ShaderCompilation.Models;

public struct ReflectedShaderData {
    public string EntryPoint;
    public byte[] Code;
    public string ReflectedCode;

    private byte[] _bytes;
    public ReadOnlySpan<byte> Bytes => _bytes.AsSpan();

    public IReadOnlyList<ReflectedSampler> Samplers;
    public IReadOnlyList<ReflectedStorageImage> StorageImages;
    public IReadOnlyList<ReflectedUniformBuffer> UniformBuffers;
    public IReadOnlyList<ReflectedPushConstantBlock> PushConstants;

    public override string ToString() {
        return $"entry: {EntryPoint}\n" +
               $"samplers: {Samplers.Count}\n" +
               $"ubuffers: {UniformBuffers.Count}\n" +
               $"pushconstants: {PushConstants.Count}\n" +
               $"simages: {StorageImages.Count}\n";
    }
}