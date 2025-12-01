namespace ShaderCompilation.Models;

public readonly struct ReflectedPushConstantMember(string name, ShaderDataType dataType, uint offset, uint size) {
    public readonly string Name = name;
    public readonly ShaderDataType DataType = dataType;
    public readonly uint Offset = offset;
    public readonly uint Size = size;

    public override string ToString() {
        return $"name: {Name}, type: {DataType}, offset: {Offset}, size: {Size}";
    }
}

public readonly struct ReflectedPushConstantBlock(string name, IReadOnlyList<ReflectedPushConstantMember> members, uint size) {
    public readonly string Name = name;
    public readonly IReadOnlyList<ReflectedPushConstantMember> Members = members;
    public readonly uint Size = size;

    public override string ToString() {
        return $"block: {Name}, size: {Size} bytes, members: {Members.Count}";
    }
}