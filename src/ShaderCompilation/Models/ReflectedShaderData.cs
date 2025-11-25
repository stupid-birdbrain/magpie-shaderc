namespace ShaderCompilation.Models;

public struct ReflectedShaderData {
    public uint Samplers;
    public uint StorageBuffers;
    public uint StorageImages;
    public uint UniformBuffers;
    public string EntryPoint;
    public byte[] Code;
    public string ReflectedCode;

    private byte[] _bytes;
    public ReadOnlySpan<byte> Bytes => _bytes.AsSpan();
    
    private ReflectedSampler[] _samplers;
    //public ReadOnlySpan<byte> Samplers => _samplers.AsSpan();

    public override string ToString() {
        return $"entry: {EntryPoint}\n" +
               $"ubuffers: {UniformBuffers}\n" +
               $"samplers: {Samplers}\n" +
               $"sbuffers: {StorageBuffers}\n" +
               $"simages: {StorageImages}\n" +
               $"reflected code:\n{(ReflectedCode)}";
    }
}