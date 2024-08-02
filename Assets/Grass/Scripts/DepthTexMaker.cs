using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DepthTexMaker : MonoBehaviour
{
    RenderTexture hzDepth;
    public GrassRenderer grassRenderer;
    public Shader mipmapMaker;
    Material hzMat;

    int ID_DepthTexture;
    int ID_InvSize;

    void Start()
    {
        Camera cam = Camera.main;
        hzMat = new Material(mipmapMaker);
        cam.depthTextureMode |= DepthTextureMode.Depth;

        hzDepth = new RenderTexture(1024, 1024,0, RenderTextureFormat.RHalf);
        hzDepth.autoGenerateMips = false;
        hzDepth.useMipMap = true;
        hzDepth.filterMode = FilterMode.Point;
        hzDepth.Create();

        grassRenderer.SetDepthTexture(hzDepth);
    }

    // Update is called once per frame
    void Update()
    {
        int w =hzDepth.width;
        int h =hzDepth.height;
        int level = 0;

        RenderTexture lastRt = null;
        if (ID_DepthTexture == 0)
        {
            ID_DepthTexture = Shader.PropertyToID("_DepthTexture");
            ID_InvSize = Shader.PropertyToID("_InvSize");
        }
        RenderTexture tempRT;
        while (h > 8 && w > 8)
        {

            hzMat.SetVector(ID_InvSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));

            tempRT = RenderTexture.GetTemporary(w, h, 0, hzDepth.format);
            tempRT.filterMode = FilterMode.Point;
            if (lastRt == null)
            {
                //  hzbMat.SetTexture(ID_DepthTexture, Shader.GetGlobalTexture("_CameraDepthTexture"));
                Graphics.Blit(Shader.GetGlobalTexture("_CameraDepthTexture"), tempRT);
            }
            else
            {
                hzMat.SetTexture(ID_DepthTexture, lastRt);
                Graphics.Blit(null, tempRT, hzMat);
                RenderTexture.ReleaseTemporary(lastRt);
            }
            Graphics.CopyTexture(tempRT, 0, 0, hzDepth, 0, level);
            lastRt = tempRT;

            w /= 2;
            h /= 2;
            level++;


        }
        RenderTexture.ReleaseTemporary(lastRt);
    }

    private void OnDisable()
    {
        hzDepth.Release();
        Destroy(hzDepth);
    }


}
