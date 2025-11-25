namespace ShaderCompilation.Models;

public readonly struct ReflectedMember(string name, ShaderDataType type, uint offset, uint size, uint arrayElements) {
    public readonly string Name = name;
    public readonly ShaderDataType DataType = type;
    public readonly uint Offset = offset;
    public readonly uint Size = size;
    public readonly uint ArrayElements = arrayElements; // 0 if not an array
}