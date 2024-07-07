using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicCompute : MonoBehaviour
{
    public int sphereAmount = 10;
    public ComputeShader shader;
    public Material material;
    public Mesh mesh;

    ComputeBuffer resultBuffer;
    int kernel;
    uint threadGroupSizeX;
    uint threadGroupSizeY;
    Vector3[] output;

    private void Start()
    {
        kernel = shader.FindKernel("CSMain");
        shader.GetKernelThreadGroupSizes(kernel, out threadGroupSizeX, out threadGroupSizeY, out _);
        resultBuffer = new ComputeBuffer(sphereAmount * sphereAmount, sizeof(float) * 3);
        output = new Vector3[sphereAmount * sphereAmount];
        shader.SetInt("dimension", sphereAmount);
        shader.SetBuffer(kernel, "resultBuffer", resultBuffer);
        shader.Dispatch(kernel, Mathf.CeilToInt((float)sphereAmount / threadGroupSizeX),
            Mathf.CeilToInt((float)sphereAmount / threadGroupSizeY), 1);
        //resultBuffer.GetData(output);
        material.SetBuffer("posBuffer", resultBuffer);

        //for (int i = 0; i < sphereAmount * sphereAmount; i++)
        //{
        //    GameObject cube = Instantiate(GameObject.CreatePrimitive(PrimitiveType.Cube), output[i], Quaternion.identity, this.transform);
        //    //Debug.Log(output[i]);
        //}
    }

    private void Update()
    {
        Graphics.DrawMeshInstancedProcedural(mesh,0,material,new Bounds(Vector3.zero, new Vector3(sphereAmount * sphereAmount, 
            1f, sphereAmount * sphereAmount)),resultBuffer.count);
    }

    private void OnDisable()
    {
        resultBuffer.Release();
        resultBuffer = null;
    }
}
