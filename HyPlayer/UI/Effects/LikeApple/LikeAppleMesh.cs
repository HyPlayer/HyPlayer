using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace HyPlayer.UI.Effects.LikeApple
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct LikeApplePinchVertex
    {
        public LikeApplePinchVertex(Vector2 from, Vector2 to, Vector2 textureCoordinate)
        {
            From = from;
            To = to;
            TextureCoordinate = textureCoordinate;
        }

        public readonly Vector2 From;
        public readonly Vector2 To;
        public readonly Vector2 TextureCoordinate;
    }

    internal static class LikeAppleMesh
    {
        private const int PortraitControlPointCount = 6;
        private const int PortraitSubdivisionLevels = 2;
        private const int LandscapeControlPointCount = 9;
        private const int LandscapeSubdivisionLevels = 2;

        // TSLBackdrop/PinchVertexMap contains five selector slots. Slots 2 and 3
        // are byte-for-byte identical, so the four visual meshes are weighted
        // 1:1:2:1 by the original selector.
        internal static int SelectPreset()
        {
            return Random.Shared.Next(5) switch
            {
                0 => 0,
                1 => 1,
                2 or 3 => 2,
                _ => 3,
            };
        }

        // MediaCoreUI creates wideMesh and compactMesh independently. Unlike
        // the compact table, all five entries in the wide table are unique.
        internal static int SelectLandscapePreset()
        {
            return Random.Shared.Next(LandscapePresets.Length);
        }

        internal static (LikeApplePinchVertex[] Vertices, ushort[] Indices) Create(
            int presetIndex,
            bool isVerticalLayout = true)
        {
            MeshPreset[] presets = isVerticalLayout ? Presets : LandscapePresets;
            presetIndex = Math.Clamp(presetIndex, 0, presets.Length - 1);
            MeshPreset preset = presets[presetIndex];
            int controlPointCount = isVerticalLayout
                ? PortraitControlPointCount
                : LandscapeControlPointCount;
            int subdivisionLevels = isVerticalLayout
                ? PortraitSubdivisionLevels
                : LandscapeSubdivisionLevels;
            Vector2[,] from = ToGrid(preset.From, controlPointCount);
            Vector2[,] to = ToGrid(preset.To, controlPointCount);

            for (int level = 0; level < subdivisionLevels; level++)
            {
                from = Subdivide(from);
                to = Subdivide(to);
            }

            int rows = from.GetLength(0);
            int columns = from.GetLength(1);
            var vertices = new LikeApplePinchVertex[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                float v = 1f - row / (float)(rows - 1);
                for (int column = 0; column < columns; column++)
                {
                    float u = column / (float)(columns - 1);
                    vertices[row * columns + column] = new LikeApplePinchVertex(
                        from[row, column] * 2f - Vector2.One,
                        to[row, column] * 2f - Vector2.One,
                        new Vector2(u, v));
                }
            }

            var indices = new ushort[(rows - 1) * (columns - 1) * 6];
            int index = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    ushort bottomLeft = (ushort)(row * columns + column);
                    ushort bottomRight = (ushort)(bottomLeft + 1);
                    ushort topLeft = (ushort)(bottomLeft + columns);
                    ushort topRight = (ushort)(topLeft + 1);

                    indices[index++] = bottomLeft;
                    indices[index++] = topLeft;
                    indices[index++] = topRight;
                    indices[index++] = topRight;
                    indices[index++] = bottomRight;
                    indices[index++] = bottomLeft;
                }
            }

            return (vertices, indices);
        }

        private static Vector2[,] ToGrid(Vector2[] points, int controlPointCount)
        {
            var result = new Vector2[controlPointCount, controlPointCount];
            for (int row = 0; row < controlPointCount; row++)
            {
                for (int column = 0; column < controlPointCount; column++)
                {
                    result[row, column] = points[row * controlPointCount + column];
                }
            }
            return result;
        }

        // A structured-grid Catmull-Clark step. Boundary corners remain sharp,
        // boundary curves use the cubic 1/8, 6/8, 1/8 rule, and interior points
        // use the standard (F + 2R + P) / 4 update.
        private static Vector2[,] Subdivide(Vector2[,] source)
        {
            int rows = source.GetLength(0);
            int columns = source.GetLength(1);
            var facePoints = new Vector2[rows - 1, columns - 1];
            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    facePoints[row, column] =
                        (source[row, column] + source[row, column + 1] +
                         source[row + 1, column] + source[row + 1, column + 1]) / 4f;
                }
            }

            var result = new Vector2[rows * 2 - 1, columns * 2 - 1];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    bool boundaryRow = row == 0 || row == rows - 1;
                    bool boundaryColumn = column == 0 || column == columns - 1;
                    Vector2 point = source[row, column];

                    if (boundaryRow && boundaryColumn)
                    {
                        result[row * 2, column * 2] = point;
                    }
                    else if (boundaryRow)
                    {
                        result[row * 2, column * 2] =
                            (source[row, column - 1] + point * 6f +
                             source[row, column + 1]) / 8f;
                    }
                    else if (boundaryColumn)
                    {
                        result[row * 2, column * 2] =
                            (source[row - 1, column] + point * 6f +
                             source[row + 1, column]) / 8f;
                    }
                    else
                    {
                        Vector2 faceAverage =
                            (facePoints[row - 1, column - 1] + facePoints[row - 1, column] +
                             facePoints[row, column - 1] + facePoints[row, column]) / 4f;
                        Vector2 edgeAverage =
                            ((point + source[row - 1, column]) / 2f +
                             (point + source[row + 1, column]) / 2f +
                             (point + source[row, column - 1]) / 2f +
                             (point + source[row, column + 1]) / 2f) / 4f;
                        result[row * 2, column * 2] =
                            (faceAverage + edgeAverage * 2f + point) / 4f;
                    }
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    Vector2 first = source[row, column];
                    Vector2 second = source[row, column + 1];
                    result[row * 2, column * 2 + 1] = row == 0 || row == rows - 1
                        ? (first + second) / 2f
                        : (first + second + facePoints[row - 1, column] +
                           facePoints[row, column]) / 4f;
                }
            }

            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    Vector2 first = source[row, column];
                    Vector2 second = source[row + 1, column];
                    result[row * 2 + 1, column * 2] = column == 0 || column == columns - 1
                        ? (first + second) / 2f
                        : (first + second + facePoints[row, column - 1] +
                           facePoints[row, column]) / 4f;
                }
            }

            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    result[row * 2 + 1, column * 2 + 1] = facePoints[row, column];
                }
            }

            return result;
        }

        private readonly record struct MeshPreset(Vector2[] From, Vector2[] To);

        private static readonly Vector2[] Preset0From =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.6f, 0f), new(0.8f, 0f), new(1f, 0f),
            new(0f, 0.2f), new(-0.0933f, 0.4f), new(0.4f, 0.2f), new(0.6f, 0.2f), new(0.3653f, 0.1335f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.4232f, 0.359f), new(0.3429f, 0.5349f), new(0.6f, 0.4f), new(0.832f, 0.4148f), new(1f, 0.4f),
            new(0f, 0.6f), new(0.2f, 0.6f), new(0.2293f, 0.7775f), new(0.7829f, 0.5595f), new(0.6514f, 0.7302f), new(1f, 0.6f),
            new(0f, 0.8f), new(0.2f, 0.8f), new(0.28f, 0.9195f), new(0.4773f, 0.8f), new(0.8f, 0.8f), new(1f, 0.8f),
            new(0f, 1f), new(0.6514f, 1.1073f), new(0.4f, 1f), new(1f, 1.0317f), new(1f, 1.1302f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset0To =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.6f, 0f), new(0.8f, 0f), new(1f, 0f),
            new(0f, 0.2f), new(-0.0933f, 0.4f), new(0.4f, 0.2f), new(0.6f, 0.2f), new(0.8587f, 0.2234f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.4526f, 0.6053f), new(0.3429f, 0.5349f), new(0.6f, 0.4f), new(0.832f, 0.4148f), new(1f, 0.4f),
            new(0f, 0.6f), new(0.2f, 0.6f), new(0.2293f, 0.7775f), new(0.7829f, 0.5595f), new(0.6514f, 0.7302f), new(1f, 0.6f),
            new(0f, 0.8f), new(0.2f, 0.8f), new(0.28f, 0.9195f), new(0.4773f, 0.8f), new(0.8f, 0.8f), new(1f, 0.8f),
            new(0f, 1f), new(0.6514f, 1.1073f), new(0.4f, 1f), new(1f, 1.0317f), new(1f, 1.1302f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset1From =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.6f, 0f), new(0.8f, 0f), new(1f, 0f),
            new(0f, 0.2f), new(0.3265f, 0.3839f), new(0.4f, 0.2f), new(0.462f, 0.3424f), new(0.683f, 0.2797f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.2f, 0.4f), new(0.4f, 0.4f), new(0.6f, 0.4903f), new(0.6574f, 0.4903f), new(1.1357f, 0.4f),
            new(-0.1173f, 0.4597f), new(0.3771f, 0.4384f), new(0.6415f, 0.5947f), new(0.8254f, 0.6935f), new(0.9334f, 0.5862f), new(1f, 0.6f),
            new(-0.0437f, 0.6533f), new(0.2f, 0.6618f), new(0.683f, 0.7362f), new(0.8139f, 0.833f), new(0.9104f, 0.8085f), new(1f, 0.8f),
            new(0f, 1f), new(0.2f, 1f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset1To =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.6f, 0f), new(0.8f, 0f), new(1f, 0f),
            new(0f, 0.2f), new(0.2437f, 0.4392f), new(0.4f, 0.2f), new(0.462f, 0.3424f), new(0.683f, 0.2797f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.1494f, 0.4787f), new(0.4f, 0.5063f), new(0.6966f, 0.516f), new(0.8139f, 0.4478f), new(1.1357f, 0.4f),
            new(-0.1173f, 0.4597f), new(0.2437f, 0.6085f), new(0.6414f, 0.5756f), new(0.8254f, 0.6935f), new(0.9334f, 0.5862f), new(1f, 0.6f),
            new(-0.0437f, 0.6533f), new(0.2f, 0.6618f), new(0.683f, 0.7362f), new(0.8139f, 0.833f), new(0.9104f, 0.8085f), new(1f, 0.8f),
            new(0f, 1f), new(0.2f, 1f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset2From =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.7465f, -0.0935f), new(0.9702f, -0.0872f), new(1.5935f, -0.0308f),
            new(-0.1675f, 0.2878f), new(0.7185f, 0.3087f), new(0.5952f, 0.0728f), new(0.7823f, 0.0815f), new(0.9318f, 0.301f), new(1.1369f, 0.3756f),
            new(0f, 0.4f), new(0.3295f, 0.4607f), new(0.7823f, 0.3087f), new(0.7465f, 0.365f), new(0.9514f, 0.4305f), new(1.1514f, 0.4424f),
            new(0f, 0.6f), new(0.2f, 0.6f), new(0.3295f, 0.4424f), new(0.5703f, 0.5f), new(0.7887f, 0.4847f), new(1f, 0.6f),
            new(0f, 0.8f), new(0.2414f, 0.7926f), new(0.0418f, 0.7303f), new(0.5952f, 0.4688f), new(0.9433f, 0.6929f), new(1f, 0.8f),
            new(0f, 1f), new(0.2f, 1f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset2To =
        [
            new(0f, 0f), new(0.2f, 0f), new(0.4f, 0f), new(0.7465f, -0.0935f), new(0.9702f, -0.0872f), new(1.5935f, -0.0308f),
            new(-0.1675f, 0.2878f), new(0.5414f, 0.2825f), new(0.5952f, 0.0728f), new(0.7823f, 0.0815f), new(0.9318f, 0.301f), new(1.1369f, 0.3756f),
            new(0f, 0.4f), new(0.2881f, 0.4479f), new(0.7823f, 0.3087f), new(0.8363f, 0.3661f), new(0.9514f, 0.4305f), new(1.1514f, 0.4424f),
            new(0f, 0.6f), new(0.177f, 0.6f), new(0.4f, 0.4775f), new(0.5703f, 0.5f), new(0.7887f, 0.4847f), new(1f, 0.6f),
            new(0f, 0.8f), new(0.2414f, 0.7926f), new(0.1499f, 0.7324f), new(0.5952f, 0.5623f), new(0.9433f, 0.6929f), new(1f, 0.8f),
            new(0f, 1f), new(0.2f, 1f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1f, 1f),
        ];

        private static readonly Vector2[] Preset3From =
        [
            new(-0.2351f, -0.0967f), new(0.2135f, -0.1414f), new(0.9221f, -0.0908f), new(0.9221f, -0.0685f), new(1.3027f, 0.0253f), new(1.2351f, 0.1786f),
            new(-0.3768f, 0.1851f), new(0.2f, 0.2f), new(0.6615f, 0.3146f), new(0.9543f, 0f), new(0.6969f, 0.1911f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.2f, 0.4f), new(0.0776f, 0.2318f), new(0.6f, 0.4f), new(0.6615f, 0.3851f), new(1f, 0.4f),
            new(0f, 0.6f), new(0.1291f, 0.6f), new(0.4f, 0.6f), new(0.4f, 0.4304f), new(0.4264f, 0.5792f), new(1.2029f, 0.8188f),
            new(-0.1192f, 1f), new(0.6f, 0.8f), new(0.4264f, 0.8104f), new(0.6f, 0.8f), new(0.8f, 0.8f), new(1f, 0.8f),
            new(0f, 1f), new(0.0776f, 1.0283f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1.1868f, 1.0283f),
        ];

        private static readonly Vector2[] Preset3To =
        [
            new(-0.2351f, -0.0967f), new(0.2135f, -0.1414f), new(0.9221f, -0.0908f), new(0.9221f, -0.0685f), new(1.3027f, 0.0253f), new(1.2351f, 0.1786f),
            new(-0.3768f, 0.1851f), new(0.1839f, 0.2f), new(0.7034f, 0.2952f), new(0.9543f, 0f), new(0.7775f, 0.3339f), new(1f, 0.2f),
            new(0f, 0.4f), new(0.0357f, 0.5369f), new(0.0776f, 0.2318f), new(0.6f, 0.4f), new(0.6615f, 0.3851f), new(1f, 0.4f),
            new(0f, 0.6f), new(0.2f, 0.6878f), new(0.4f, 0.6f), new(0.5f, 0.5896f), new(0.6454f, 0.6878f), new(1.2029f, 0.8188f),
            new(-0.1192f, 1f), new(0.6193f, 0.9027f), new(0.4264f, 0.8104f), new(0.6f, 0.8f), new(0.8f, 0.8f), new(1f, 0.8f),
            new(0f, 1f), new(0.0776f, 1.0283f), new(0.4f, 1f), new(0.6f, 1f), new(0.8f, 1f), new(1.1868f, 1.0283f),
        ];

        // Keep this table after every control-point array. Static field
        // initializers run in source order, so placing it earlier captures
        // null array references before the preset data has been initialized.
        private static readonly MeshPreset[] Presets =
        [
            new(Preset0From, Preset0To),
            new(Preset1From, Preset1To),
            new(Preset2From, Preset2To),
            new(Preset3From, Preset3To),
        ];

        // Exact wideMesh control maps from MediaCoreUI 4022.400.3.0.0
        // (iOS 16.3, 20D47). All five maps start at the same regular 9x9
        // lattice; MediaCoreUI keeps each copy separately in the binary.
        private static readonly Vector2[] LandscapeIdentity = CreateIdentityGrid(
            LandscapeControlPointCount);

        private static readonly Vector2[] LandscapePreset0To =
        [
            new(-0.2292f, -0.0529f), new(-0.0402f, -0.127f), new(0.1116f, -0.3122f), new(0.0923f, -0.336f), new(1.1205f, -0.164f), new(1.0089f, -0.0635f), new(1.1205f, -0.0529f), new(1.1116f, -0.0899f), new(1.1979f, -0.0741f),
            new(-0.2202f, 0.2685f), new(0.0238f, 0.1435f), new(0.0997f, 0.0933f), new(0.0774f, 0.1091f), new(0.75f, 0.0933f), new(0.7738f, 0.1091f), new(0.7991f, 0.1435f), new(1.2798f, 0.088f), new(1.1801f, 0.1435f),
            new(-0.1667f, 0.3611f), new(0.0432f, 0.25f), new(0.1116f, 0.25f), new(0.0923f, 0.2791f), new(0.5387f, 0.2791f), new(0.5908f, 0.3161f), new(0.625f, 0.3161f), new(0.9896f, 0.2791f), new(1.0893f, 0.2341f),
            new(-0.1176f, 0.4544f), new(0.0625f, 0.3909f), new(0.1503f, 0.4418f), new(0.1637f, 0.4306f), new(0.4836f, 0.3909f), new(0.6161f, 0.4544f), new(0.6458f, 0.4544f), new(0.7411f, 0.3909f), new(1.064f, 0.338f),
            new(-0.0625f, 0.5344f), new(0.0997f, 0.5159f), new(0.2664f, 0.5721f), new(0.2589f, 0.5721f), new(0.4836f, 0.5344f), new(0.7113f, 0.5344f), new(0.7411f, 0.5344f), new(0.7991f, 0.5159f), new(1.0461f, 0.5f),
            new(-0.0402f, 0.6574f), new(0.375f, 0.713f), new(0.3929f, 0.6574f), new(0.375f, 0.625f), new(0.5f, 0.6038f), new(0.7991f, 0.5721f), new(0.808f, 0.625f), new(0.875f, 0.625f), new(1.0461f, 0.6435f),
            new(-0.0298f, 0.7685f), new(0.3586f, 0.9041f), new(0.4063f, 0.8003f), new(0.4568f, 0.75f), new(0.625f, 0.6574f), new(0.8616f, 0.67f), new(0.8408f, 0.713f), new(0.8943f, 0.7288f), new(1.1265f, 0.8214f),
            new(-0.0402f, 0.9041f), new(0.2589f, 1.0152f), new(0.4747f, 0.9676f), new(0.4568f, 0.8882f), new(0.7411f, 0.8538f), new(0.8616f, 0.8538f), new(0.8408f, 0.875f), new(0.9211f, 0.9438f), new(1.1116f, 0.9676f),
            new(-0.0625f, 1.0979f), new(0.0238f, 1.2196f), new(0.3304f, 1.0688f), new(0.375f, 1f), new(0.7887f, 1.0556f), new(0.8408f, 1.0556f), new(0.875f, 1.0688f), new(0.9435f, 1.0688f), new(1.0893f, 1.2196f),
        ];

        private static readonly Vector2[] LandscapePreset1To =
        [
            new(-0.1726f, -0.1984f), new(0.0551f, -0.2593f), new(0.2158f, -0.2593f), new(0.3839f, -0.1984f), new(0.5119f, -0.1984f), new(0.6473f, -0.1243f), new(0.744f, -0.2698f), new(1.0179f, -0.4259f), new(1.2515f, -0.2698f),
            new(0f, 0.0562f), new(0.125f, 0.1971f), new(0.2381f, 0.2679f), new(0.375f, 0.2917f), new(0.5f, 0.2202f), new(0.625f, 0.125f), new(0.8467f, -0.1111f), new(1f, -0.1528f), new(1.1042f, 0.0146f),
            new(-0.0193f, 0.0357f), new(0.125f, 0.1766f), new(0.25f, 0.25f), new(0.375f, 0.3082f), new(0.5f, 0.25f), new(0.625f, 0.1766f), new(0.7887f, 0.0146f), new(0.9598f, -0.0648f), new(1.0551f, -0.0172f),
            new(0f, 0.3353f), new(0.125f, 0.3896f), new(0.2167f, 0.3444f), new(0.375f, 0.3231f), new(0.5119f, 0.3772f), new(0.625f, 0.3353f), new(0.7768f, 0.1025f), new(0.9464f, 0.0562f), new(1.0685f, 0.1647f),
            new(-0.0521f, 0.5192f), new(0.125f, 0.4471f), new(0.2229f, 0.3824f), new(0.375f, 0.3444f), new(0.5119f, 0.3933f), new(0.6577f, 0.4353f), new(0.75f, 0.4677f), new(0.8601f, 0.4353f), new(1.1473f, 0.2345f),
            new(-0.0402f, 0.6442f), new(0.125f, 0.5192f), new(0.2277f, 0.4f), new(0.375f, 0.3664f), new(0.5119f, 0.4074f), new(0.6577f, 0.4677f), new(0.75f, 0.5f), new(0.8527f, 0.4471f), new(1.128f, 0.2345f),
            new(-0.0253f, 0.7718f), new(0.1116f, 0.5675f), new(0.2339f, 0.4353f), new(0.3708f, 0.4219f), new(0.5148f, 0.4353f), new(0.6726f, 0.5f), new(0.7649f, 0.578f), new(0.8601f, 0.5357f), new(1.1622f, 0.3082f),
            new(-0.0253f, 0.9041f), new(0.0982f, 0.7718f), new(0.2381f, 0.6872f), new(0.375f, 0.6442f), new(0.5119f, 0.6442f), new(0.6577f, 0.6872f), new(0.8229f, 0.7348f), new(0.875f, 0.875f), new(1.2411f, 1.1343f),
            new(-0.0521f, 1.1005f), new(0.0982f, 1.0556f), new(0.2277f, 1.0556f), new(0.3557f, 1.0556f), new(0.5f, 1.1587f), new(0.625f, 1.1799f), new(0.7649f, 1.3677f), new(0.8958f, 1.4841f), new(1.0685f, 1.3519f),
        ];

        private static readonly Vector2[] LandscapePreset2To =
        [
            new(-0.064f, -0.1323f), new(0.0893f, -0.1614f), new(0.25f, -0.0608f), new(0.5729f, -0.1614f), new(0.6771f, -0.1614f), new(0.7292f, -0.1614f), new(0.7634f, -0.1217f), new(0.875f, -0.0608f), new(1.0164f, -0.0423f),
            new(-0.0714f, 0.1091f), new(0.2054f, 0.0985f), new(0.2292f, 0.1091f), new(0.375f, 0.125f), new(0.5357f, 0.2077f), new(0.6131f, 0.25f), new(0.6458f, 0.125f), new(0.75f, -0.0284f), new(1.0164f, 0.1091f),
            new(-0.0565f, 0.25f), new(0.1696f, 0.2077f), new(0.1771f, 0.2262f), new(0.2143f, 0.2262f), new(0.375f, 0.1759f), new(0.625f, 0.2937f), new(0.6369f, 0.3161f), new(0.6652f, 0.2262f), new(1.0104f, 0.2262f),
            new(-0.0565f, 0.375f), new(0.0997f, 0.375f), new(0.125f, 0.375f), new(0.1771f, 0.3882f), new(0.3616f, 0.2077f), new(0.6131f, 0.3406f), new(0.6548f, 0.3406f), new(0.7068f, 0.375f), new(1.0313f, 0.3538f),
            new(-0.1429f, 0.6058f), new(0.1414f, 0.5f), new(0.1563f, 0.5f), new(0.1949f, 0.5f), new(0.5193f, 0.2937f), new(0.6964f, 0.4517f), new(0.7158f, 0.4517f), new(0.7902f, 0.4841f), new(1.0461f, 0.4841f),
            new(-0.0714f, 0.6753f), new(0.2054f, 0.625f), new(0.2054f, 0.625f), new(0.2292f, 0.625f), new(0.6131f, 0.375f), new(0.7158f, 0.5298f), new(0.7634f, 0.5456f), new(0.8557f, 0.625f), new(1.0789f, 0.625f),
            new(-0.0565f, 0.8108f), new(0.2143f, 0.75f), new(0.2292f, 0.75f), new(0.25f, 0.7315f), new(0.6652f, 0.6865f), new(0.7634f, 0.6462f), new(0.8095f, 0.7077f), new(0.8914f, 0.75f), new(1.0923f, 0.75f),
            new(-0.0565f, 0.9306f), new(0.2054f, 0.9067f), new(0.2054f, 0.9306f), new(0.2292f, 0.9411f), new(0.625f, 0.875f), new(0.7634f, 0.7718f), new(0.8557f, 0.8108f), new(0.939f, 0.8538f), new(1.0789f, 0.9306f),
            new(0f, 1f), new(-0.0714f, 1.2169f), new(0.125f, 1.4021f), new(0.25f, 1.0794f), new(0.625f, 1.0794f), new(0.7902f, 1.0794f), new(0.875f, 1.0794f), new(0.9509f, 1.0582f), new(1.0104f, 1.0794f),
        ];

        private static readonly Vector2[] LandscapePreset3To =
        [
            new(-0.2292f, -0.3968f), new(0.0699f, -0.3439f), new(0.2217f, -0.1799f), new(0.3512f, -0.1376f), new(0.6533f, -0.2407f), new(0.6845f, -0.164f), new(0.7753f, -0.3148f), new(0.9494f, -0.3677f), new(1.381f, -0.5476f),
            new(-0.1711f, 0.0827f), new(-0.0387f, -0.1263f), new(0.25f, 0.125f), new(0.2887f, 0.125f), new(0.5f, 0.125f), new(0.5565f, 0.125f), new(0.7827f, -0.0787f), new(0.9182f, -0.1799f), new(1.2039f, -0.0628f),
            new(-0.1057f, 0.2209f), new(0.0268f, 0.1918f), new(0.25f, 0.25f), new(0.2679f, 0.2844f), new(0.2887f, 0.2685f), new(0.3958f, 0.2844f), new(0.6771f, 0.125f), new(0.9702f, 0.0542f), new(1.2113f, 0.1091f),
            new(-0.1176f, 0.2983f), new(0.1101f, 0.33f), new(0.25f, 0.42f), new(0.317f, 0.42f), new(0.3571f, 0.42f), new(0.3958f, 0.42f), new(0.6369f, 0.2983f), new(0.9107f, 0.2844f), new(1.2113f, 0.33f),
            new(-0.1533f, 0.375f), new(0.1533f, 0.375f), new(0.2292f, 0.42f), new(0.375f, 0.5f), new(0.4301f, 0.5377f), new(0.4583f, 0.5377f), new(0.6845f, 0.4735f), new(0.811f, 0.4735f), new(1.1369f, 0.463f),
            new(-0.0938f, 0.5728f), new(0.1533f, 0.4054f), new(0.3363f, 0.5f), new(0.4167f, 0.5377f), new(0.5f, 0.625f), new(0.5476f, 0.6991f), new(0.7887f, 0.6118f), new(0.8378f, 0.588f), new(1.1563f, 0.5608f),
            new(-0.0789f, 0.75f), new(0.25f, 0.5608f), new(0.3958f, 0.5608f), new(0.4732f, 0.625f), new(0.5402f, 0.75f), new(0.5967f, 0.7976f), new(0.8839f, 0.7361f), new(0.8839f, 0.7811f), new(1.1563f, 0.6389f),
            new(-0.1057f, 0.9226f), new(0.125f, 0.875f), new(0.2976f, 0.875f), new(0.4464f, 0.8882f), new(0.5908f, 0.9041f), new(0.625f, 0.875f), new(0.9702f, 0.9041f), new(1.0268f, 1f), new(1.1726f, 1.0443f),
            new(-0.0387f, 1.1138f), new(0.0878f, 1.1349f), new(0.2292f, 1.1138f), new(0.4301f, 1.1852f), new(0.625f, 1.2804f), new(0.625f, 1.3254f), new(1.0372f, 1.2328f), new(0.9568f, 1.2328f), new(1.0938f, 1.2328f),
        ];

        private static readonly Vector2[] LandscapePreset4To =
        [
            new(-0.0952f, -0.1561f), new(0.0997f, -0.1561f), new(0.2396f, -0.0847f), new(0.3586f, -0.0608f), new(0.4926f, -0.0608f), new(0.6086f, -0.1561f), new(0.7426f, -0.1772f), new(0.8616f, -0.1561f), new(1.064f, -0.2275f),
            new(-0.0521f, 0.0933f), new(-0.0521f, 0.3062f), new(0.2708f, 0.375f), new(0.375f, 0.3485f), new(0.4926f, 0.3062f), new(0.625f, 0.2335f), new(0.75f, 0.125f), new(0.875f, -0.0337f), new(1.064f, -0.1772f),
            new(-0.125f, 0.3611f), new(0.0789f, 0.5f), new(0.2827f, 0.4147f), new(0.3824f, 0.375f), new(0.4926f, 0.33f), new(0.625f, 0.25f), new(0.75f, 0.1812f), new(0.8988f, 0.0146f), new(1.0804f, -0.123f),
            new(-0.0952f, 0.5119f), new(0.1518f, 0.5344f), new(0.2827f, 0.4683f), new(0.3824f, 0.4147f), new(0.4926f, 0.375f), new(0.625f, 0.2851f), new(0.7589f, 0.2163f), new(0.8988f, 0.2335f), new(1.119f, 0.4147f),
            new(-0.0521f, 0.625f), new(0.2827f, 0.5344f), new(0.2961f, 0.5985f), new(0.3646f, 0.5119f), new(0.4926f, 0.3995f), new(0.6324f, 0.3062f), new(0.7589f, 0.2619f), new(0.9286f, 0.3062f), new(1.1071f, 0.4683f),
            new(-0.1399f, 0.6938f), new(0.2902f, 0.5721f), new(0.2604f, 0.6759f), new(0.3586f, 0.5344f), new(0.5f, 0.4286f), new(0.6414f, 0.33f), new(0.8006f, 0.3062f), new(0.9673f, 0.375f), new(1.119f, 0.5985f),
            new(-0.0521f, 0.7897f), new(0.192f, 0.8294f), new(0.25f, 0.75f), new(0.2708f, 0.6759f), new(0.5f, 0.5119f), new(0.7738f, 0.5589f), new(0.8988f, 0.7315f), new(0.9286f, 0.713f), new(1.119f, 0.75f),
            new(-0.0685f, 0.9438f), new(0.1518f, 0.9438f), new(0.1979f, 0.875f), new(0.25f, 0.957f), new(0.5f, 0.7718f), new(0.7738f, 0.7315f), new(0.936f, 0.9755f), new(1f, 1.0205f), new(1.1726f, 0.957f),
            new(-0.0283f, 1.0873f), new(0.0997f, 1.1561f), new(0.125f, 1.1799f), new(0.2604f, 1.1243f), new(0.4926f, 1.0688f), new(0.8006f, 1.0873f), new(0.9167f, 1.1376f), new(1.0313f, 1.2698f), new(1.1548f, 1.3228f),
        ];

        // Keep this table after the landscape arrays for the same static-field
        // initialization reason as the compact Presets table above.
        private static readonly MeshPreset[] LandscapePresets =
        [
            new(LandscapeIdentity, LandscapePreset0To),
            new(LandscapeIdentity, LandscapePreset1To),
            new(LandscapeIdentity, LandscapePreset2To),
            new(LandscapeIdentity, LandscapePreset3To),
            new(LandscapeIdentity, LandscapePreset4To),
        ];

        private static Vector2[] CreateIdentityGrid(int controlPointCount)
        {
            var points = new Vector2[controlPointCount * controlPointCount];
            float denominator = controlPointCount - 1f;
            for (int row = 0; row < controlPointCount; row++)
            {
                for (int column = 0; column < controlPointCount; column++)
                {
                    points[row * controlPointCount + column] = new Vector2(
                        column / denominator,
                        row / denominator);
                }
            }
            return points;
        }
    }
}

