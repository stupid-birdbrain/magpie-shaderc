namespace ShaderCompilation.Models;

public readonly struct ReflectedStorageBuffer(
    string name,
    string typeName,
    uint binding,
    uint set,
    uint byteLength,
    ReflectedStorageBuffer.SsboFlags flags) {
    
    public readonly string Name = name;
    public readonly string TypeName = typeName;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
    public readonly uint ByteLength = byteLength;
    public readonly SsboFlags Flags = flags;

    [Flags]
    public enum SsboFlags : byte {
        ReadWrite = 0,
        ReadOnly = 1,
        WriteOnly = 2
    }
}