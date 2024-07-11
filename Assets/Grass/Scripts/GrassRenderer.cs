using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static System.Runtime.InteropServices.Marshal;

public class GrassRenderer : MonoBehaviour
{
    private struct GrassData
    {
        public Vector3 position;
        public Vector2 worldUV;
    }
    private struct GrassChunk
    {
        public Bounds bound;
        public ComputeBuffer argsBuffer;
        public ComputeBuffer positionBuffer;
        public ComputeBuffer culledPositionBuffer;
        public Material material;
    }

    public Camera cam;
    Fog fog;

    const float planeDimension = 10f;

    [Header("Terrain")]
    public Transform parent;
    public float uniformPlaneScale = 1.0f;
    public int chunkDimension = 2;
    private float scaledDimension;
    GrassChunk[] chunks;

    [Space(5)]
    [Header("Grass settings")]
    public int densityPerDimension = 1, numInstancePerChunkDimension;
    public Mesh grassMesh;
    private int numInstancePerChunk, numPerDimension;
    public Material groundMat;
    public float distanceCutoff;

    [Header("Shader")]
    public Material grassMaterial;
    public ComputeShader grassInitializerCS, cullGrassCS;

    //Buffer
    private ComputeBuffer voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer;
    private int numThreadGroups, numVoteThreadGroups, numGroupScanThreadGroups;

    //for DrawMeshInstancedIndirect
    private uint[] args;
    private Bounds bound;
    public Boolean isFogOn;

    private void OnEnable()
    {
        //initialize variables
        scaledDimension = planeDimension * uniformPlaneScale;
        numPerDimension = numInstancePerChunkDimension * densityPerDimension;
        numInstancePerChunk = numPerDimension * numPerDimension;

        numThreadGroups = Mathf.CeilToInt(numInstancePerChunk / 128.0f);
        if (numThreadGroups > 128)
        {
            int powerOfTwo = 128;
            while (powerOfTwo < numThreadGroups)
                powerOfTwo *= 2;

            numThreadGroups = powerOfTwo;
        }
        else
        {
            while (128 % numThreadGroups != 0)
                numThreadGroups++;
        }
        numVoteThreadGroups = Mathf.CeilToInt(numInstancePerChunk / 128.0f);
        numGroupScanThreadGroups = Mathf.CeilToInt(numThreadGroups / 1024.0f);
        //numGroupScanThreadGroups = Mathf.CeilToInt(numInstancePerChunk / 1024.0f);

        voteBuffer = new ComputeBuffer(numInstancePerChunk, 4);
        scanBuffer = new ComputeBuffer(numInstancePerChunk, 4);
        groupSumArrayBuffer = new ComputeBuffer(numThreadGroups, 4);
        scannedGroupSumBuffer = new ComputeBuffer(numThreadGroups, 4);

        //set up buffer for DrawMeshInstancedIndirect
        args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint) 0;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);

        CreateTerrain(Vector3.zero);


        fog = cam.GetComponent<Fog>();

        InitializeGrassChunks();

    }

    private void InitializeGrassChunks()
    {
        chunks = new GrassChunk[chunkDimension * chunkDimension];

        for (int i = 0; i < chunkDimension; i++)
        {
            for (int j = 0; j < chunkDimension; j++)
            {
                float chunkSize = scaledDimension / (float)chunkDimension;
                float halfFieldSize = scaledDimension / 2;

                Vector3 boundCenter;
                boundCenter = new Vector3(-halfFieldSize, 0, -halfFieldSize);
                boundCenter += new Vector3((i + 1) * chunkSize  - chunkSize/2 , 0, (j + 1) * chunkSize - chunkSize / 2);

                Bounds chunkBound = new Bounds(boundCenter, new Vector3(chunkSize, 20.0f, chunkSize));

                GrassChunk chunk = new GrassChunk();
                chunk.bound = chunkBound;
                chunk.argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                chunk.argsBuffer.SetData(args);
                chunk.positionBuffer = new ComputeBuffer(numInstancePerChunk, SizeOf(typeof(GrassData)));
                chunk.culledPositionBuffer = new ComputeBuffer(numInstancePerChunk, SizeOf(typeof(GrassData)));

                grassInitializerCS.SetInt("_YOffset", j);
                grassInitializerCS.SetInt("_XOffset", i);
                grassInitializerCS.SetVector("_Center", boundCenter);
                grassInitializerCS.SetFloat("_Dimension", chunkSize);
                grassInitializerCS.SetInt("_InstanceDimension", numPerDimension);
                grassInitializerCS.SetInt("_Density", densityPerDimension);
                grassInitializerCS.SetInt("_ChunkInstances", numInstancePerChunk);
                grassInitializerCS.SetInt("_ChunkDimension", chunkDimension);
                grassInitializerCS.SetBuffer(0, "_GrassDataBuffer", chunk.positionBuffer);

                //[numthreads(8,8,1)]
                int threadGroup = Mathf.CeilToInt((float)numPerDimension / 32f);
                grassInitializerCS.Dispatch(0, threadGroup, threadGroup, 1);

                Material mat = new Material(grassMaterial);
                mat.SetInt("_NumInstanceDimension", numPerDimension);
                mat.SetFloat("_ChunkSize", chunkSize);
                chunk.material = mat;

                chunks[j + i * chunkDimension] = chunk;
            }
        }
    }

    void CullGrass(GrassChunk chunk, Matrix4x4 VP)
    {
        //Reset Args
        chunk.argsBuffer.SetData(args);


        // Vote
        cullGrassCS.SetFloat("_ScaleY", chunk.material.GetFloat("_ScaleYAxis"));
        cullGrassCS.SetFloat("_XAxisRotation", cam.transform.rotation.eulerAngles.x);
        cullGrassCS.SetMatrix("MATRIX_VP", VP);
        cullGrassCS.SetBuffer(0, "_GrassDataBuffer", chunk.positionBuffer);
        cullGrassCS.SetBuffer(0, "_VoteBuffer", voteBuffer);
        cullGrassCS.SetVector("_CameraPosition", cam.transform.position);
        cullGrassCS.SetFloat("_Distance", distanceCutoff);
        cullGrassCS.SetInt("_NumInstance", numInstancePerChunk);
        cullGrassCS.Dispatch(0, numVoteThreadGroups, 1, 1);

        // Scan Instances
        cullGrassCS.SetBuffer(1, "_VoteBuffer", voteBuffer);
        cullGrassCS.SetBuffer(1, "_ScanBuffer", scanBuffer);
        cullGrassCS.SetBuffer(1, "_GroupSumArray", groupSumArrayBuffer);
        cullGrassCS.Dispatch(1, numThreadGroups, 1, 1);

        // Scan Groups
        cullGrassCS.SetInt("_NumOfGroups", numThreadGroups);
        cullGrassCS.SetBuffer(2, "_GroupSumArrayIn", groupSumArrayBuffer);
        cullGrassCS.SetBuffer(2, "_GroupSumArrayOut", scannedGroupSumBuffer);
        cullGrassCS.Dispatch(2, numGroupScanThreadGroups, 1, 1);

        // Compact
        cullGrassCS.SetBuffer(3, "_GrassDataBuffer", chunk.positionBuffer);
        cullGrassCS.SetBuffer(3, "_VoteBuffer", voteBuffer);
        cullGrassCS.SetBuffer(3, "_ScanBuffer", scanBuffer);
        cullGrassCS.SetBuffer(3, "_ArgsBuffer", chunk.argsBuffer);
        cullGrassCS.SetBuffer(3, "_CulledGrassOutputBuffer", chunk.culledPositionBuffer);
        cullGrassCS.SetBuffer(3, "_GroupSumArray", scannedGroupSumBuffer);
        cullGrassCS.Dispatch(3, numThreadGroups, 1, 1);

        chunk.material.SetBuffer("grassDataBuffer", chunk.culledPositionBuffer);

    }

    void CreateTerrain(Vector3 center)
    {
        GameObject terrain = GameObject.CreatePrimitive(PrimitiveType.Plane);
        terrain.transform.position = center;
        terrain.transform.parent = parent;
        terrain.transform.localScale = Vector3.one * uniformPlaneScale;

        Renderer renderer = terrain.GetComponent<Renderer>();
        renderer.material = groundMat;

        //get bound
        bound = renderer.bounds;
    }

    private void Update()
    {
        Matrix4x4 P = cam.projectionMatrix;
        Matrix4x4 V = cam.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;

        fog.enabled = isFogOn;
        for (int i = 0; i < chunks.Length; i++)
        {
            if (isFogOn)
            {
                chunks[i].material.EnableKeyword("IS_FOG");
            }
            else
            {
                chunks[i].material.DisableKeyword("IS_FOG");
            }

            CullGrass(chunks[i], VP);

            Graphics.DrawMeshInstancedIndirect(grassMesh, 0, chunks[i].material, chunks[i].bound, chunks[i].argsBuffer);
        
        
        }

        
    }

    private void OnValidate()
    {
        if (uniformPlaneScale < 1.0f)
        {
            uniformPlaneScale = 1.0f;
        }

        if (numInstancePerChunkDimension <= 0)
        {
            numInstancePerChunkDimension = 1;
        }
    }

    private void ReleaseChunkBuffer(GrassChunk chunk)
    {
        chunk.positionBuffer.Release();
        chunk.positionBuffer = null;
        chunk.culledPositionBuffer.Release();
        chunk.culledPositionBuffer = null;
        chunk.argsBuffer.Release();
        chunk.argsBuffer = null;
    }

    private void OnDisable()
    {
        voteBuffer.Release();
        voteBuffer = null;
        scanBuffer.Release();
        scanBuffer = null;
        groupSumArrayBuffer.Release();
        groupSumArrayBuffer = null;
        scannedGroupSumBuffer.Release();
        scannedGroupSumBuffer = null;

        for (int i = 0; i < chunks.Length; i++)
        {
            ReleaseChunkBuffer(chunks[i]);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        for (int i = 0; i < chunks.Length; i++)
        {
            Gizmos.DrawWireCube(chunks[i].bound.center, chunks[i].bound.size);
        }
    }
}
