namespace ShaderCompilation.Models;

public readonly struct ReflectedVertexInput(string name, uint location, ShaderDataType dataType, uint arraySize) {
    public readonly string Name = name;
    public readonly uint Location = location;
    public readonly ShaderDataType DataType = dataType;
    public readonly uint ArraySize = arraySize;

    public override string ToString() {
        return $"vertex input data: {Name}, location: {Location}, type: {DataType}, arraysize: {ArraySize}";
    }
}