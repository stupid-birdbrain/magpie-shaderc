namespace ShaderCompilation.Models;

public readonly struct ReflectedSampler(string name, uint binding, uint set, ShaderDataType dataType) {
    public readonly string Name = name;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
    public readonly ShaderDataType DataType = dataType;
}

public readonly struct ReflectedStorageImage(string name, uint binding, uint set, ShaderDataType dataType) {
    public readonly string Name = name;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
    public readonly ShaderDataType DataType = dataType;
}