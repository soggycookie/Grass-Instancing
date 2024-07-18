using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
        public ComputeBuffer argsBufferLOD;
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

    [Header("Shader")]
    public Material grassMaterial;
    public ComputeShader grassInitializerCS, cullGrassCS, windNoiseCS;

    //Buffer
    private ComputeBuffer voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer;
    private int numThreadGroups, numVoteThreadGroups, numGroupScanThreadGroups;

    [Space(5)]
    [Header("Grass technical settings")]
    public int densityPerDimension = 1;
    public int numInstancePerChunkDimension;
    public Mesh grassMeshLOD, grassMesh;
    private int numInstancePerChunk, numPerDimension;
    public Material groundMat;
    [Range(0, 1000)]
    public float distanceCutoff;
    [Range(0, 1000)]
    public float distanceLOD;
    public bool isFogOn;
    public bool changeMatWhilePlay;

    [Space(5)]
    [Header("Grass Settings")]
    public Color tipColor;
    public Color rootColor;
    public bool highGrassTipColorOn;
    public Color highGrassTipColor;
    [Range(0, 10)]
    public float highGrassTipFactor;

    [Space(5)]
    [Range(0, 2)]
    public float grassCurve;

    [Space(5)]
    [Range(0, 5)]
    public float swaySpeed;
    [Tooltip("This should take a extreme small scale value if the area is small")]
    [Range(0.00001f, 2)]
    public float windTextureScale = 0.03f;
    [Range(0, 5)]
    public float windSpeed = 1;
    [Range(0, 5)]
    public float windAmplitude;
    [Tooltip("To read only")]
    public RenderTexture windMap;

    [Space(5)]
    public bool ambientBaseOnDensity;
    [Range(0, 1)]
    public float ambientOcclusion;

    [Space(5)]
    public float scaleHeight;
    public float scaleWidth;
    public Vector2 scaleXBaseOnHeightMap;

    [Space(5)]
    public Texture grassHeightTexture;
    [Range(0, 10)]
    public float heightStrength;

    [Space(5)]
    [Header("Fog Settings")]
    public Color fogColor;
    [Range(0, 10)]
    public float fogOffset;
    [Range(0, 1)]
    public float fogDensity;





    //for DrawMeshInstancedIndirect
    private uint[] argsLOD;
    private uint[] args;
    private Bounds bound;


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
        argsLOD = new uint[5] { 0, 0, 0, 0, 0 };
        argsLOD[0] = (uint)grassMeshLOD.GetIndexCount(0);
        argsLOD[1] = (uint)0;
        argsLOD[2] = (uint)grassMeshLOD.GetIndexStart(0);
        argsLOD[3] = (uint)grassMeshLOD.GetBaseVertex(0);

        args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint)0;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        CreateTerrain(Vector3.zero);


        fog = cam.GetComponent<Fog>();

        WindMapInit();
        InitializeGrassChunks();

    }

    private void WindMapInit()
    {
        windMap = new RenderTexture(216, 216, 0, RenderTextureFormat.RGFloat)
        {
            enableRandomWrite = true

        };

        windMap.Create();

        windNoiseCS.SetTexture(0, "_WindNoise", windMap);
        windNoiseCS.SetFloat("_Time", 0);
        UpdateWindMap();
    }

    private void UpdateWindMap()
    {
        windNoiseCS.SetFloat("_Time", (float)EditorApplication.timeSinceStartup * 0.2f);
        windNoiseCS.Dispatch(0, 64, 64, 1);
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
                boundCenter += new Vector3((i + 1) * chunkSize - chunkSize / 2, 0, (j + 1) * chunkSize - chunkSize / 2);

                Bounds chunkBound = new Bounds(boundCenter, new Vector3(chunkSize, 20.0f, chunkSize));

                GrassChunk chunk = new GrassChunk();
                chunk.bound = chunkBound;
                chunk.argsBufferLOD = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                chunk.argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                chunk.argsBufferLOD.SetData(argsLOD);
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

    void CullGrass(bool lod, GrassChunk chunk, Matrix4x4 VP)
    {
        //Reset Args
        if (lod)
        {
            chunk.argsBufferLOD.SetData(argsLOD);
        }
        else
        {
            chunk.argsBuffer.SetData(args);
        }

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
        cullGrassCS.SetBuffer(3, "_ArgsBuffer", lod ? chunk.argsBufferLOD : chunk.argsBuffer);
        cullGrassCS.SetBuffer(3, "_CulledGrassOutputBuffer", chunk.culledPositionBuffer);
        cullGrassCS.SetBuffer(3, "_GroupSumArray", scannedGroupSumBuffer);
        cullGrassCS.Dispatch(3, numThreadGroups, 1, 1);

        chunk.material.SetBuffer("grassDataBuffer", chunk.culledPositionBuffer);

        chunk.material.SetTexture("_WindNoise", windMap);
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

    private void UpdateMaterialWhilePlay(GrassChunk chunk)
    {
        if (!changeMatWhilePlay)
        {
            return;
        }

        Material mat = chunk.material;

        mat.SetColor("_TipColor", tipColor);
        mat.SetColor("_RootColor", rootColor);
        mat.SetColor("_HighGrassTipColor", highGrassTipColor);
        mat.SetInt("_IsTipColorOn", highGrassTipColorOn ? 1 : 0);
        mat.SetFloat("_HighGrassTipFactor", highGrassTipFactor);
        mat.SetFloat("_Droop", grassCurve);
        mat.SetFloat("_SwaySpeed", swaySpeed);
        mat.SetFloat("_WindAmplitude", windAmplitude);
        windNoiseCS.SetFloat("_WindSpeed", windSpeed);
        windNoiseCS.SetFloat("_Scale", windTextureScale);
        mat.SetInt("_DensityAmbient", ambientBaseOnDensity ? 1 : 0);
        mat.SetFloat("_AmbientOcclusion", ambientOcclusion);
        mat.SetFloat("_ScaleYAxis", scaleHeight);
        mat.SetFloat("_ScaleXAxis", scaleWidth);
        mat.SetVector("_ScaleXBaseOnY", scaleXBaseOnHeightMap);
        mat.SetTexture("_GrassHeightMap", grassHeightTexture);
        mat.SetFloat("_HeightStrength", heightStrength);
        mat.SetColor("_FogColor", fogColor);
        mat.SetFloat("_FogOffset", fogOffset);
        mat.SetFloat("_FogDensity", fogDensity);

    }

    private void Update()
    {
        Matrix4x4 P = cam.projectionMatrix;
        Matrix4x4 V = cam.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;
        bool lod;
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

            lod = Vector3.Distance(cam.transform.position, chunks[i].bound.center) > distanceLOD;

            UpdateMaterialWhilePlay(chunks[i]);
            UpdateWindMap();
            CullGrass(lod, chunks[i], VP);
            Graphics.DrawMeshInstancedIndirect(lod ? grassMeshLOD : grassMesh, 0, chunks[i].material, chunks[i].bound, lod ? chunks[i].argsBufferLOD : chunks[i].argsBuffer);


        }


    }

    private void ReleaseChunkBuffer(GrassChunk chunk)
    {
        chunk.positionBuffer.Release();
        chunk.positionBuffer = null;
        chunk.culledPositionBuffer.Release();
        chunk.culledPositionBuffer = null;
        chunk.argsBufferLOD.Release();
        chunk.argsBufferLOD = null;
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

        windMap.Release();
        windMap = null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        for (int i = 0; i < chunks.Length; i++)
        {
            Gizmos.DrawWireCube(chunks[i].bound.center, chunks[i].bound.size);
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
}
