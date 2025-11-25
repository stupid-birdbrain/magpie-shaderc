namespace ShaderCompilation.Models;

public readonly struct ReflectedUniformProperty(string name, string typeName, uint binding, uint set, uint byteLength) {
    public readonly string Name = name;
    public readonly string TypeName = typeName;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
    public readonly uint ByteLength = byteLength;
}