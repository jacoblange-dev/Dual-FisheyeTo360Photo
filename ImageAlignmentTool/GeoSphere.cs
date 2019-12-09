using System;
using System.Collections.Generic;
using OpenTK;

namespace ImageAlignmentTool
{
    // based on GeoSphere found in the SharpDX toolkit
    internal class GeoSphere
    {
        public int VertCount => _vertices.Count;
        public int TexCoordCount => _uvs.Count;
        public int IndexCount => _indices.Count;

        public Vector3[] GetVerts()
        {
            return _vertices.ToArray();
        }

        public Vector2[] GetUvs()
        {
            return _uvs.ToArray();
        }

        public int[] GetIndices()
        {
            return _indices.ToArray();
        }

        private static readonly Vector3[] OctahedronVertices =
        {
            new Vector3(0, 1, 0),
            new Vector3(0, 0, -1),
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 1),
            new Vector3(-1, 0, 0),
            new Vector3(0, -1, 0)
        };

        private static readonly int[] OctahedronIndices =
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 4,
            0, 4, 1,
            5, 1, 4,
            5, 4, 3,
            5, 3, 2,
            5, 2, 1
        };

        private List<Vector3> _vertexPositions;
        private List<int> _indices;
        private List<Vector3> _vertices;
        private List<Vector2> _uvs;
        private unsafe int* _indicesPtr;

        private readonly Dictionary<UndirectedEdge, int> _subdividedEdges = new Dictionary<UndirectedEdge, int>();

        public GeoSphere(float pRadius, int pSubdivisions = 5)
        {
            GenerateMesh(pRadius, pSubdivisions);
        }

        private unsafe void GenerateMesh(float pRadius, int pSubdivisions)
        {
            _vertexPositions = new List<Vector3>(OctahedronVertices);
            _indices = new List<int>(OctahedronIndices);

            for (var i = 0; i < pSubdivisions; ++i)
            {
                var newIndices = new List<int>();
                _subdividedEdges.Clear();

                var triCount = _indices.Count / 3;
                for (var j = 0; j < triCount; ++j)
                {
                    var ind0 = _indices[j * 3 + 0];
                    var ind1 = _indices[j * 3 + 1];
                    var ind2 = _indices[j * 3 + 2];

                    Vector3 v01, v12, v20;
                    int ind01, ind12, ind20;
                    //       v0
                    //       *
                    //      / \
                    //     / a \
                    // v20*-----*v01
                    //   / \ c / \
                    //  / b \ / d \
                    // *-----*-----*
                    // v2    m12     v1
                    DivideEdge(ind0, ind1, out v01, out ind01);
                    DivideEdge(ind1, ind2, out v12, out ind12);
                    DivideEdge(ind0, ind2, out v20, out ind20);

                    // a
                    newIndices.Add(ind0);
                    newIndices.Add(ind01);
                    newIndices.Add(ind20);

                    // b
                    newIndices.Add(ind20);
                    newIndices.Add(ind12);
                    newIndices.Add(ind2);

                    // c
                    newIndices.Add(ind20);
                    newIndices.Add(ind01);
                    newIndices.Add(ind12);

                    // d
                    newIndices.Add(ind01);
                    newIndices.Add(ind1);
                    newIndices.Add(ind12);
                }
                _indices.Clear();
                _indices.AddRange(newIndices);
            }

            // uv coordinate calculations
            _vertices = new List<Vector3>(_vertexPositions.Count);
            _uvs = new List<Vector2>(_vertexPositions.Count);
            foreach (var vertex in _vertexPositions)
            {
                var normal = Vector3.Normalize(vertex);
                var position = normal * pRadius;
                _vertices.Add(position);

                var longitude = (float)Math.Atan2(normal.X, -normal.Z);
                var latitude = (float)Math.Asin(normal.Y);

                var u = longitude / MathHelper.TwoPi + 0.5f;
                var v = latitude / MathHelper.Pi + 0.5f;
                _uvs.Add(new Vector2(u, 1 - v));
            }

            // fix up seam near prime meridian
            const float epsilon = 1.192092896e-7f;
            var preCount = _vertices.Count;
            var indicesArray = GetIndices();
            fixed (void* pIndices = indicesArray)
            {
                _indicesPtr = (int*)pIndices;

                for (var i = 0; i < preCount; ++i)
                {
                    var isOnPrimeMeridian = WithinEpsilon(_vertices[i].X, 0, epsilon)
                                            && WithinEpsilon(_uvs[i].X, 1, epsilon);

                    if (!isOnPrimeMeridian)
                        continue;

                    var newIndex = VertCount;
                    var vertex = _vertices[i];
                    var uv = _uvs[i];
                    uv.X = 0f;
                    _vertices.Add(vertex);
                    _uvs.Add(uv);

                    for (var j = 0; j < IndexCount; j += 3)
                    {
                        var ind0 = &_indicesPtr[j + 0];
                        var ind1 = &_indicesPtr[j + 1];
                        var ind2 = &_indicesPtr[j + 2];

                        if (*ind0 == i)
                        { /* do nothing */ }
                        else if (*ind1 == i)
                        {
                            // swap 0 1
                            var temp = *ind0;
                            *ind0 = *ind1;
                            *ind1 = temp;

                            // swap 1 2
                            temp = *ind1;
                            *ind1 = *ind2;
                            *ind2 = temp;
                        }
                        else if (*ind2 == i)
                        {
                            // swap 0 2
                            var temp = *ind0;
                            *ind0 = *ind2;
                            *ind2 = temp;

                            // swap 1 2
                            temp = *ind1;
                            *ind1 = *ind2;
                            *ind2 = temp;
                        }
                        else
                            continue;

                        if (Math.Abs(_uvs[*ind0].X - _uvs[*ind1].X) > 0.5f ||
                            Math.Abs(_uvs[*ind0].X - _uvs[*ind2].X) > 0.5f)
                            _indicesPtr[j] = newIndex;
                    }
                }
                //FixPole(0); // this might be possible, tried some things that didn't quite work
                //FixPole(5);

                var count = IndexCount;
                _indices.Clear();
                for (var i = 0; i < count; ++i)
                    _indices.Add(_indicesPtr[i]);

                _indicesPtr = (int*)0;
            }
        }

        private void DivideEdge(int pInd0, int pInd1, out Vector3 pNewVertex, out int pNewIndex)
        {
            var edge = new UndirectedEdge(pInd0, pInd1);

            if (_subdividedEdges.TryGetValue(edge, out pNewIndex))
                pNewVertex = _vertexPositions[pNewIndex];
            else
            {
                pNewVertex = (_vertexPositions[pInd0] + _vertexPositions[pInd1]) * 0.5f;
                pNewIndex = _vertexPositions.Count;
                _vertexPositions.Add(pNewVertex);
                _subdividedEdges[edge] = pNewIndex;
            }
        }

        private static bool WithinEpsilon(float pA, float pB, float pEpsilon)
        {
            var num = pA - pB;
            return (-pEpsilon <= num) && (num <= pEpsilon);
        }

        private struct UndirectedEdge : IEquatable<UndirectedEdge>
        {
            /// <summary>
            /// Assumes item1 > item2.  (a, b) != (b, a)
            /// </summary>
            public UndirectedEdge(int pItem1, int pItem2)
            {
                _item1 = Math.Max(pItem1, pItem2);
                _item2 = Math.Min(pItem1, pItem2);
            }

            private readonly int _item1;
            private readonly int _item2;

            public bool Equals(UndirectedEdge pOther)
            {
                return _item1 == pOther._item1 && _item2 == pOther._item2;
            }

            public override bool Equals(object pObject)
            {
                if (ReferenceEquals(null, pObject)) return false;
                return pObject is UndirectedEdge && Equals((UndirectedEdge)pObject);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_item1.GetHashCode() * 397) ^ _item2.GetHashCode();
                }
            }

            public static bool operator ==(UndirectedEdge pLeft, UndirectedEdge pRight)
            {
                return pLeft.Equals(pRight);
            }

            public static bool operator !=(UndirectedEdge pLeft, UndirectedEdge pRight)
            {
                return !pLeft.Equals(pRight);
            }
        }
    }
}
