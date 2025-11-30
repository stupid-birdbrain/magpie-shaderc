using System.Text;

namespace ShaderCompilation.Models;

public readonly struct ReflectedUniformBuffer(string name, uint binding, uint set, IReadOnlyList<ReflectedBufferMember> members, uint size) {
    public readonly string Name = name;
    public readonly uint Binding = binding;
    public readonly uint Set = set;
    public readonly IReadOnlyList<ReflectedBufferMember> Members = members;
    public readonly uint Size = size;

    public override string ToString() {
        var sb = new StringBuilder();
        
        sb.AppendLine($"uniform buffer name: {Name}");
        sb.AppendLine($"uniform buffer binding: {Binding}");
        sb.AppendLine($"uniform buffer set: {Set}");
        sb.AppendLine($"uniform buffer size: {Size}");

        foreach(var member in Members) {
            sb.AppendLine(member.ToString());
        }
        
        return sb.ToString();
    }
}

public readonly struct ReflectedBufferMember(string name, ShaderDataType dataType, uint offset, uint size) {
    public readonly string Name = name;
    public readonly ShaderDataType DataType = dataType;
    public readonly uint Offset = offset;
    public readonly uint Size = size;

    public override string ToString() {
        var sb = new StringBuilder();
        
        sb.AppendLine($"member name: {Name}");
        sb.AppendLine($"member size: {Size}");
        sb.AppendLine($"member offset: {Offset}");
        
        return sb.ToString();
    }
}