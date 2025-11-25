namespace ShaderCompilation.Models;

public readonly struct ReflectedPushConstant(string propertyName, string memberName, uint offset, uint size) {
    public readonly string MemberName = memberName;
    public readonly string PropertyName = propertyName;
    public readonly uint Offset = offset;
    public readonly uint Size = size;
}