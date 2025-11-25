namespace ShaderCompilation.Models;

public readonly struct ReflectedSampler(string name, uint binding, uint set) {
    public readonly string Name = name;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
}