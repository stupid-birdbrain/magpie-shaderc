namespace ShaderCompilation.Models; 

public readonly record struct ReflectedStorageBufferBlock(
    string Name,
    string TypeName,
    uint Binding,
    uint Set,
    uint ByteLength,
    ReflectedStorageBufferBlock.SsboFlags Flags,
    IReadOnlyList<ReflectedStorageBufferMember> Members) {

    [Flags]
    public enum SsboFlags : byte {
        None = 0,
        ReadWrite = 0,
        ReadOnly = 1,
        WriteOnly = 2
    }

    public override string ToString() {
        return $"storage buffer info: {Name}, binding: {Binding}, set: {Set}, size: {ByteLength} bytes, flags: {Flags}, member count: {Members.Count}";
    }
}

public readonly struct ReflectedStorageBufferMember(
    string name,
    ShaderDataType dataType,
    uint offset,
    uint size,
    uint arraySize) {
    public readonly string Name = name;
    public readonly ShaderDataType DataType = dataType;
    public readonly uint Offset = offset;
    public readonly uint Size = size;
    public readonly uint ArraySize = arraySize;

    public override string ToString() {
        return $"storage buffer member info: {Name}, type: {DataType}, offset: {Offset}, size: {Size}, arraysize: {ArraySize}";
    }
}