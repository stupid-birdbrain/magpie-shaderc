using Vortice.SpirvCross;

namespace ShaderCompilation.Models;

public enum ShaderDataType {
    Unknown,
    Float, 
    Vector2, 
    Vector3, 
    Vector4,
    Int, 
    IVector2, 
    IVector3, 
    IVector4,
    UInt, UVector2, UVector3, UVector4,
    Bool,
    Matrix2x2,
    Matrix3x3,
    Matrix4x4
}

public class DataTypeExt {
    public static ShaderDataType SpvTypeToDataType(Basetype basetype, uint vectorSize, uint columns) {
        // its a matrix!
        if (columns > 1) {
            switch (columns) {
                case 4 when vectorSize == 4 && basetype == Basetype.Fp32:
                    return ShaderDataType.Matrix4x4;
                case 3 when vectorSize == 3 && basetype == Basetype.Fp32:
                    return ShaderDataType.Matrix3x3;
                case 2 when vectorSize == 2 && basetype == Basetype.Fp32:
                    return ShaderDataType.Matrix2x2;
            }
        }

        // i guess its not a matrix........
        return basetype switch
        {
            Basetype.Fp32 => vectorSize switch
            {
                1 => ShaderDataType.Float,
                2 => ShaderDataType.Vector2,
                3 => ShaderDataType.Vector3,
                4 => ShaderDataType.Vector4,
                _ => ShaderDataType.Unknown
            },
            Basetype.Int32 => vectorSize switch
            {
                1 => ShaderDataType.Int,
                2 => ShaderDataType.IVector2,
                3 => ShaderDataType.IVector3,
                4 => ShaderDataType.IVector4,
                _ => ShaderDataType.Unknown
            },
            Basetype.Uint32 => vectorSize switch
            {
                1 => ShaderDataType.UInt,
                2 => ShaderDataType.UVector2,
                3 => ShaderDataType.UVector3,
                4 => ShaderDataType.UVector4,
                _ => ShaderDataType.Unknown
            },
            Basetype.Boolean => vectorSize == 1 ? ShaderDataType.Bool : ShaderDataType.Unknown,
            _ => ShaderDataType.Unknown
        };
    }
}