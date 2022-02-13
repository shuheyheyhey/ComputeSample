using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Wireframe : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        mf.mesh.SetIndices(mf.mesh.GetIndices(0),MeshTopology.LineStrip,0);
    }
}
