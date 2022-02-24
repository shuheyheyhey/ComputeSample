using System;
using System.Collections;
using System.Collections.Generic;
using Lasp;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.Rendering;

public class NoiseCompute : MonoBehaviour
{
    [SerializeField] private Mesh sourceMesh = default;
    [SerializeField] private ComputeShader noiseComputeShader = default;
    [SerializeField] private ComputeShader triToVertComputeShader = default;
    [SerializeField] private Material material = default;
    public Camera camera;
    public GameObject gameObject;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SourceVertex
    {
        public Vector3 position;
        public Vector2 uv;
    }

    private bool initialized = false;

    // A compute buffer to hold vertex data of the source mesh
    private GraphicsBuffer sourceVertBuffer;

    // A compute buffer to hold index data of the source mesh
    private GraphicsBuffer sourceTriBuffer;

    // A compute buffer to hold vertex data of the generated mesh
    private GraphicsBuffer drawBuffer;

    // A compute buffer to hold indirect draw arguments
    private GraphicsBuffer argsBuffer;

    // The id of the kernel in the noise compute shader
    private int idNoiseComputeKernel;

    // The id of the kernel in the tri to vert count compute shader
    private int idTriToVertKernel;

    // The x dispatch size for the pyramid compute shader
    private int dispatchSize;

    // The local bounds of the generated mesh
    private Bounds localBounds;

    // The size of one entry into the various compute buffers
    // ComputeBuffer のメモリテーブルにセットする際、適切にサイズを計算する必要がある
    private const int SOURCE_VERT_STRIDE = sizeof(float) * (3 + 2);
    private const int SOURCE_TRI_STRIDE = sizeof(int);
    private const int DRAW_STRIDE = sizeof(float) * (3 + (3 + 2) * 3);
    private const int ARGS_STRIDE = sizeof(int) * 4;

    private void OnEnable()
    {
        // If initialized, call on disable to clean things up
        if (initialized)
        {
            OnDisable();
        }
        initialized = true;
        
        this.SetupKernelIndex();
        
        // Initialize Buffer
        this.SetupBuffer();
    }

    private void OnDisable()
    {
        // Dispose of buffers
        if (initialized)
        {
            sourceVertBuffer.Release();
            sourceTriBuffer.Release();
            drawBuffer.Release();
            argsBuffer.Release();
        }

        initialized = false;
    }

    private void SetupBuffer()
    {
        // 指定された Mesh から各情報を抜き出す
        Vector3[] positions = this.sourceMesh.vertices;
        Vector2[] uvs = this.sourceMesh.uv;
        int[] tris = this.sourceMesh.triangles;

        // ComputeShader への入力データの用意
        SourceVertex[] vertices = new SourceVertex[positions.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new SourceVertex()
            {
                position = positions[i],
                uv = Vector2.zero,//uvs[i],
            };
        }

        // 三角形の数
        int numTriangles = tris.Length / 3;

        // ComputeBuffer 入力データの準備
        this.sourceVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, SOURCE_VERT_STRIDE);
        this.sourceVertBuffer.SetData(vertices);
        this.sourceTriBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tris.Length, SOURCE_TRI_STRIDE);
        this.sourceTriBuffer.SetData(tris);
        
        // We split each triangle into three new ones
        this.drawBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, numTriangles * 7, DRAW_STRIDE);
        this.drawBuffer.SetCounterValue(0); // Set the count to zero
        
        this.argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, ARGS_STRIDE);
        // The data in the args buffer corresponds to:
        // 0: vertex count per draw instance. We will only use one instance
        // 1: instance count. One
        // 2: start vertex location if using a Graphics Buffer
        // 3: and start instance location if using a Graphics Buffer
        this.argsBuffer.SetData(new int[] {0, 1, 0, 0});
        
        this.noiseComputeShader.SetBuffer(this.idNoiseComputeKernel, "_SourceVertices", this.sourceVertBuffer);
        this.noiseComputeShader.SetBuffer(this.idNoiseComputeKernel, "_SourceTriangles", this.sourceTriBuffer);
        this.noiseComputeShader.SetBuffer(this.idNoiseComputeKernel, "_DrawTriangles", this.drawBuffer);
        this.noiseComputeShader.SetInt("_NumSourceTriangles", numTriangles);

        this.triToVertComputeShader.SetBuffer(this.idTriToVertKernel, "_IndirectArgsBuffer", this.argsBuffer);
        
        this.material.SetBuffer("_DrawTriangles", this.drawBuffer);
        
        this.noiseComputeShader.GetKernelThreadGroupSizes(this.idNoiseComputeKernel, out uint threadGroupSize, out _, out _);
        this.dispatchSize = Mathf.CeilToInt((float)numTriangles / threadGroupSize);

        // Get the bounds of the source mesh and then expand by the pyramid height
        this.localBounds = this.sourceMesh.bounds;
    }

    private void SetupKernelIndex()
    {
        this.idNoiseComputeKernel = this.noiseComputeShader.FindKernel("Main");
        this.idTriToVertKernel = this.triToVertComputeShader.FindKernel("Main");
    }

    public Bounds TransformBounds(Bounds boundsOS) {
        var center = transform.TransformPoint(boundsOS.center);

        // transform the local extents' axes
        var extents = boundsOS.extents;
        var axisX = transform.TransformVector(extents.x, 0, 0);
        var axisY = transform.TransformVector(0, extents.y, 0);
        var axisZ = transform.TransformVector(0, 0, extents.z);

        // sum their absolute value to get the world extents
        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds { center = center, extents = extents };
    }

    private void Update()
    {
        float sin = Mathf.Abs(Mathf.Sin(Time.deltaTime/10)) * 360f;
        Vector3 euler = new Vector3(0f, 0f, sin);
        //this.transform.Rotate(euler);
        //this.gameObject.transform.Rotate(euler);
        this.camera.transform.LookAt(Vector3.zero);
    }

    // LateUpdate is called after all Update calls
    private void LateUpdate() {

        // Clear the draw buffer of last frame's data
        if (drawBuffer == null)
        {
            return;
        }

        drawBuffer.SetCounterValue(0);

        AudioLevelTracker levelTracker = this.GetComponent<AudioLevelTracker>();
        this.noiseComputeShader.SetFloat("_AudioLevel", levelTracker.normalizedLevel);
        
        this.noiseComputeShader.SetFloat("_Time", Time.deltaTime);

        // Transform the bounds to world space
        Bounds bounds = TransformBounds(localBounds);

        // Update the shader with frame specific data
        this.noiseComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
        //this.noiseComputeShader.SetFloat("_PyramidHeight", pyramidHeight * Mathf.Sin(animationFrequency * Time.timeSinceLevelLoad));

        // Dispatch the pyramid shader. It will run on the GPU
        this.noiseComputeShader.Dispatch(this.idNoiseComputeKernel, dispatchSize, 1, 1);

        // Copy the count (stack size) of the draw buffer to the args buffer, at byte position zero
        // This sets the vertex count for our draw procediral indirect call
        GraphicsBuffer.CopyCount(this.drawBuffer, this.argsBuffer, 0);

        // This the compute shader outputs triangles, but the graphics shader needs the number of vertices,
        // we need to multiply the vertex count by three. We'll do this on the GPU with a compute shader 
        // so we don't have to transfer data back to the CPU
        triToVertComputeShader.Dispatch(idTriToVertKernel, 1, 1, 1);

        // DrawProceduralIndirect queues a draw call up for our generated mesh
        // It will receive a shadow casting pass, like normal
        Graphics.DrawProceduralIndirect(material, bounds, MeshTopology.Lines, argsBuffer, 0, 
            null, null, ShadowCastingMode.On, false, gameObject.layer);
    }

}
