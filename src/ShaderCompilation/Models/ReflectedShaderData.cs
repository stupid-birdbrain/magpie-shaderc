namespace ShaderCompilation.Models;

public struct ReflectedShaderData {
    public uint Samplers;
    public uint StorageBuffers;
    public string EntryPoint;
    public byte[] Code;
    public string ReflectedCode;

    private byte[] _bytes;
    public ReadOnlySpan<byte> Bytes => _bytes.AsSpan();

    public IReadOnlyList<ReflectedSampler> ReadSamplers;
    public IReadOnlyList<ReflectedStorageImage> StorageImages;
    public IReadOnlyList<ReflectedUniformBuffer> UniformBuffers;

    public override string ToString() {
        return $"entry: {EntryPoint}\n" +
               $"ubuffers: {UniformBuffers.Count}\n" +
               $"samplers: {Samplers}\n" +
               $"sbuffers: {StorageBuffers}\n" +
               $"simages: {StorageImages.Count}\n" +
               $"reflected code:\n{(ReflectedCode)}";
    }
}