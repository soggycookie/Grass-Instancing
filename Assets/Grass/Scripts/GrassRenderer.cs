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

    public Camera cam;
    Fog fog;

    const float planeDimension = 10f;

    [Header("Terrain")]
    public Transform parent;
    public float uniformPlaneScale = 1.0f;
    private float scaledDimension;

    [Space(5)]
    [Header("Grass settings")]
    public int densityPerUnit = 1;
    public Mesh grassMesh;
    private int numOfInstances, numPerDimension;
    public Material groundMat;
    public float distanceCutoff;

    [Header("Shader")]
    public Material grassMaterial;
    public ComputeShader grassInitializerCS, cullGrassCS;
    
    //Buffer
    private ComputeBuffer grassDataBuffer, culledPositionsBuffer;
    private ComputeBuffer voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer;
    private int numThreadGroups, numVoteThreadGroups, numGroupScanThreadGroups;

    //for DrawMeshInstancedIndirect
    private ComputeBuffer argsBuffer;
    private uint[] args;
    private Bounds bound;
    public Boolean isFogOn;

    private void Awake()
    {
        //initialize variables
        scaledDimension = planeDimension * uniformPlaneScale;
        numPerDimension = Mathf.FloorToInt(scaledDimension) * densityPerUnit;
        numOfInstances = numPerDimension * numPerDimension;

        numThreadGroups = Mathf.CeilToInt(numOfInstances / 128.0f);
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
        numVoteThreadGroups = Mathf.CeilToInt(numOfInstances / 128.0f);
        numGroupScanThreadGroups = Mathf.CeilToInt(numOfInstances / 1024.0f);

        voteBuffer = new ComputeBuffer(numOfInstances, 4);
        scanBuffer = new ComputeBuffer(numOfInstances, 4);
        groupSumArrayBuffer = new ComputeBuffer(numThreadGroups, 4);
        scannedGroupSumBuffer = new ComputeBuffer(numThreadGroups, 4);

        //intialize grass compute shader
        grassDataBuffer = new ComputeBuffer(numOfInstances, SizeOf(typeof(GrassData)));
        culledPositionsBuffer = new ComputeBuffer(numOfInstances, SizeOf(typeof(GrassData)));
        grassInitializerCS.SetVector("_Center", Vector3.zero);
        grassInitializerCS.SetFloat("_Dimension", scaledDimension);
        grassInitializerCS.SetInt("_InstanceDimension", numPerDimension);
        grassInitializerCS.SetInt("_Density", densityPerUnit);
        grassInitializerCS.SetBuffer(0, "_GrassDataBuffer", grassDataBuffer);

        //[numthreads(8,8,1)]
        int threadGroup = Mathf.CeilToInt((float)numPerDimension / 8f);
        grassInitializerCS.Dispatch(0, threadGroup, threadGroup, 1);

        //set up buffer for DrawMeshInstancedIndirect
        argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)grassMesh.GetIndexCount(0);
        args[1] = (uint) 0;
        args[2] = (uint)grassMesh.GetIndexStart(0);
        args[3] = (uint)grassMesh.GetBaseVertex(0);
        argsBuffer.SetData(args);

        CreateTerrain(Vector3.zero);


        fog = cam.GetComponent<Fog>();

    }

    void CullGrass(Matrix4x4 VP)
    {
        //Reset Args
        argsBuffer.SetData(args);


        // Vote
        cullGrassCS.SetMatrix("MATRIX_VP", VP);
        cullGrassCS.SetBuffer(0, "_GrassDataBuffer", grassDataBuffer);
        cullGrassCS.SetBuffer(0, "_VoteBuffer", voteBuffer);
        cullGrassCS.SetVector("_CameraPosition", Camera.main.transform.position);
        cullGrassCS.SetFloat("_Distance", distanceCutoff);
        cullGrassCS.Dispatch(0, numVoteThreadGroups, 1, 1);

        // Scan Instanes
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
        cullGrassCS.SetBuffer(3, "_GrassDataBuffer", grassDataBuffer);
        cullGrassCS.SetBuffer(3, "_VoteBuffer", voteBuffer);
        cullGrassCS.SetBuffer(3, "_ScanBuffer", scanBuffer);
        cullGrassCS.SetBuffer(3, "_ArgsBuffer", argsBuffer);
        cullGrassCS.SetBuffer(3, "_CulledGrassOutputBuffer", culledPositionsBuffer);
        cullGrassCS.SetBuffer(3, "_GroupSumArray", scannedGroupSumBuffer);
        cullGrassCS.Dispatch(3, numThreadGroups, 1, 1);

        grassMaterial.SetBuffer("grassDataBuffer", culledPositionsBuffer);

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

    private void Start()
    {

    }

    private void Update()
    {
        Matrix4x4 P = cam.projectionMatrix;
        Matrix4x4 V = cam.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;

        CullGrass(VP);

        Graphics.DrawMeshInstancedIndirect(grassMesh, 0, grassMaterial, bound, argsBuffer);
        //Graphics.DrawMeshInstancedProcedural(grassMesh,0,grassMaterial,bound,grassPosBuffer.count);

        fog.enabled = isFogOn;
        if (isFogOn)
        {
            grassMaterial.EnableKeyword("IS_FOG");
        }
        else
        {
            grassMaterial.DisableKeyword("IS_FOG");
        }
    }

    private void OnValidate()
    {
        if (uniformPlaneScale < 1.0f)
        {
            uniformPlaneScale = 1.0f;
        }
    }

    private void OnDisable()
    {
        grassDataBuffer.Release();
        grassDataBuffer = null;
        argsBuffer.Release();
        argsBuffer = null;

        culledPositionsBuffer.Release();
        culledPositionsBuffer = null;
        voteBuffer.Release();
        voteBuffer = null;
        scanBuffer.Release();
        scanBuffer = null;
        groupSumArrayBuffer.Release();
        groupSumArrayBuffer = null;
        scannedGroupSumBuffer.Release();
        scannedGroupSumBuffer = null;
    }
}
