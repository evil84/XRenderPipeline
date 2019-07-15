using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Matrix4x4 = UnityEngine.Matrix4x4;
using RenderPipeline = UnityEngine.Rendering.RenderPipeline;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

public struct AAbb
{
	public Vector3 min;
	public Vector3 max;
}

public struct LightSource
{
	//public Vector4 posRadius;
	//public Vector4 color;

	public Vector3 position;
	public Vector3 direction;
	public Vector4 color;
	public float spotAngle;
	public float range;
	public uint type;
}

public class MyRenderPipeline : RenderPipeline
{
	private CommandBuffer _cameraBuffer;
	
	private static readonly int clusterWidth = 32;
	private static readonly int clusterHeight = 32;
	private static readonly int clusterZCount = 16;
	private static readonly int maxLightCount = 2048;

	private int _clusterXYZCount = 0;
	private ComputeShader _clusterRenderingCS;
	private ComputeShader _clusterLightCullingCS;
	private ComputeShader _debugLightingCS;
	private ComputeBuffer _clusterBuffer;
	private ComputeBuffer _LightBuffer;

	private int _clusterComputeKernel;
	private int _lightCullingKernel;
	private int _debugLightingKernel;
	private int _lightCount;
	private int _ClusterXCount;
	private int _ClusterYCount;
	
	private ComputeBuffer g_pointLightIndexCounter;
	private ComputeBuffer g_lightIndexList;
	private ComputeBuffer g_lightGrid;

	private XPipelineAsset _pipelineAsset;

	
	private int depthTargetTextureId;
	private int colorTargetTextureId;

	private RenderTexture _debugTexture;
	
	public MyRenderPipeline(XPipelineAsset asset)
	{
		_pipelineAsset = asset;
		//_clusterRenderingCS = _pipelineAsset.clusterRenderingCS;
		_clusterRenderingCS = Resources.Load<ComputeShader>("ClusterRendering");
		_clusterLightCullingCS = Resources.Load<ComputeShader>("ClusterLightCulling");
		_debugLightingCS = Resources.Load<ComputeShader>("DebugLighting");
		_clusterComputeKernel = _clusterRenderingCS.FindKernel("ClusterCompute");
		_lightCullingKernel = _clusterLightCullingCS.FindKernel("ClusterLightCulling");
		_debugLightingKernel = _debugLightingCS.FindKernel("DebugLightting");
		depthTargetTextureId = Shader.PropertyToID("depthTexture");
		colorTargetTextureId = Shader.PropertyToID("sceneTexture");
	}
	
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (Camera camera in cameras)
        {
            Render(context, camera);
        }
    }
    
    private void Render(ScriptableRenderContext context, Camera camera)
    {
	    
	    ScriptableCullingParameters cullingParameters;
	    if (!camera.TryGetCullingParameters(false, out cullingParameters))
		    return;
	    
	    GraphicsSettings.lightsUseLinearIntensity = true;

	    _cameraBuffer = CommandBufferPool.Get(camera.name);
	    
	    context.SetupCameraProperties(camera);
		
	    
	    RenderTextureDescriptor descriptor = CreateRenderTextureDescriptor(camera, 1);
	    var depthDescriptor = descriptor;
	    depthDescriptor.colorFormat = RenderTextureFormat.Depth;
	    depthDescriptor.depthBufferBits = 32;
	    depthDescriptor.bindMS = false;
	    _cameraBuffer.GetTemporaryRT(depthTargetTextureId, depthDescriptor, FilterMode.Point);

	    var colorDescriptor = descriptor;
	    descriptor.colorFormat = RenderTextureFormat.ARGB32;
	    descriptor.bindMS = false;
	    _cameraBuffer.GetTemporaryRT(colorTargetTextureId, colorDescriptor, FilterMode.Bilinear);
	    _cameraBuffer.SetRenderTarget(new RenderTargetIdentifier(colorTargetTextureId), new RenderTargetIdentifier(depthTargetTextureId));
	    
	    CameraClearFlags clearFlags = camera.clearFlags;
	    _cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor, 1.0f);
	    context.ExecuteCommandBuffer(_cameraBuffer);
	    _cameraBuffer.Clear();

#if UNITY_EDITOR
	    ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif
	    
		var cullingResult = context.Cull(ref cullingParameters);
		
		SetupCameraClusters(camera);
		UpdateLightBuffer(cullingResult);
		LightCulling(camera);
		context.ExecuteCommandBuffer(_cameraBuffer);
		_cameraBuffer.Clear();
		//DrawDepthOnlyPass(camera, context, cullingResult);
		
		_ClusterXCount = Mathf.CeilToInt((float) Screen.width / clusterWidth);
	    _ClusterYCount = Mathf.CeilToInt((float) Screen.height / clusterHeight);
	    
	    _cameraBuffer.SetGlobalBuffer("g_lights", _LightBuffer);
	    _cameraBuffer.SetGlobalBuffer("g_lightIndexList", g_lightIndexList);
	    _cameraBuffer.SetGlobalBuffer("g_lightGrid", g_lightGrid);
	    _cameraBuffer.SetGlobalVector("clusterSize", new Vector2(clusterWidth, (float)clusterHeight));
	    _cameraBuffer.SetGlobalVector("cb_clusterCount", new Vector3(_ClusterXCount, _ClusterYCount, clusterZCount));
	    _cameraBuffer.SetGlobalVector("cb_clusterSize", new Vector3(clusterWidth, clusterHeight, Mathf.CeilToInt((camera.farClipPlane - camera.nearClipPlane) / clusterZCount)));
	    _cameraBuffer.SetGlobalVector("cb_screenSize", new Vector4 ( Screen.width, Screen.height, 1.0f / Screen.width, 1.0f / Screen.height ));
	    
	    
	    context.ExecuteCommandBuffer(_cameraBuffer);
	    _cameraBuffer.Clear();
	    //cameraBuffer.EndSample("Render Camera");
	    
	    var drawSettings = new DrawingSettings(new ShaderTagId("ForwardLit"), new SortingSettings(camera)
	    {
		    criteria = SortingCriteria.QuantizedFrontToBack,
	    });

	    //drawSettings.enableInstancing = true;
	    //drawSettings.perObjectData = PerObjectData.LightIndices;
	    
	    var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
	    context.DrawRenderers(cullingResult, ref drawSettings, ref filterSettings);
	    
	    context.DrawSkybox(camera);
	    filterSettings.renderQueueRange = RenderQueueRange.transparent;
	    context.DrawRenderers(cullingResult, ref drawSettings, ref filterSettings);

	    if (camera.cameraType == CameraType.SceneView)
	    {
		    context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
	    }
	    
	    //DebugLighting(camera);
	    context.ExecuteCommandBuffer(_cameraBuffer);
	    _cameraBuffer.Clear();
	    
	    //_cameraBuffer.Blit(_debugTexture, BuiltinRenderTextureType.CameraTarget);
	    _cameraBuffer.Blit(new RenderTargetIdentifier(colorTargetTextureId), BuiltinRenderTextureType.CameraTarget);
	    context.ExecuteCommandBuffer(_cameraBuffer);
	    _cameraBuffer.Clear();
	    context.Submit();
	    
    }

    private void SetupCameraClusters(Camera camera)
    {
	    float near = camera.nearClipPlane;
		float far = camera.farClipPlane;
		
		int clusterXCount = Mathf.CeilToInt((float) Screen.width / clusterWidth);
		int clusterYCount = Mathf.CeilToInt((float) Screen.height / clusterHeight);
		_clusterXYZCount = clusterXCount * clusterYCount * clusterZCount;
		
		_clusterBuffer?.Release();

		_clusterBuffer = new ComputeBuffer(_clusterXYZCount, 72);


		Matrix4x4 projectionMatrix;
		if (camera.cameraType == CameraType.SceneView)
			projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
		else
			projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
		
		var projectionMatrixInverse = projectionMatrix.inverse;
		
		_cameraBuffer.SetComputeIntParams(_clusterRenderingCS, "clusterCount", new int[]{ clusterXCount, clusterYCount, clusterZCount });
		_cameraBuffer.SetComputeIntParams(_clusterRenderingCS, "clusterSize", new int[] { clusterWidth, clusterHeight });
		_cameraBuffer.SetComputeFloatParams(_clusterRenderingCS, "nearFarPlane", new float[] { near, far });
		_cameraBuffer.SetComputeFloatParams(_clusterRenderingCS, "screenSize", new float[] { Screen.width, Screen.height, 1.0f / Screen.width, 1.0f / Screen.height });
		_cameraBuffer.SetComputeMatrixParam(_clusterRenderingCS, "inverseProjectionMatrix", projectionMatrixInverse);

		int threadGroupCountX = Mathf.CeilToInt((float) clusterXCount / 16);
		int threadGroupCountY = Mathf.CeilToInt((float) clusterYCount / 16);
		int threadCountCountZ = Mathf.CeilToInt((float) clusterZCount / 4);
		
		_cameraBuffer.SetComputeBufferParam(_clusterRenderingCS, _clusterComputeKernel, "g_clusters", _clusterBuffer);
		_cameraBuffer.DispatchCompute(_clusterRenderingCS, _clusterComputeKernel, threadGroupCountX, threadGroupCountY, threadCountCountZ);
    }

    private void UpdateLightBuffer(CullingResults cullingResults)
    {
	    _LightBuffer?.Release();
	    _LightBuffer = new ComputeBuffer(maxLightCount, System.Runtime.InteropServices.Marshal.SizeOf<LightSource>()); 
	    
	    List<LightSource> lightPosRadius = new List<LightSource>();
	    
	    for (int i = 0; i < cullingResults.visibleLights.Length; ++i)
	    {
		    var light = cullingResults.visibleLights[i];
		    if (light.light.enabled == true)
		    {
			    LightSource l = new LightSource();
			    l.position = new Vector3(light.light.transform.position.x, light.light.transform.position.y,
				    light.light.transform.position.z);
			    if (light.lightType == LightType.Directional || light.lightType == LightType.Spot)
			    {
				    l.direction = light.light.transform.forward;
				    l.direction = light.light.transform.localToWorldMatrix.GetColumn(2);
			    }
			    l.color = light.finalColor;
			    if (light.lightType == LightType.Spot)
				    l.spotAngle = light.spotAngle;
			    l.range = light.range;
			    l.type = (uint) light.lightType;
			    lightPosRadius.Add(l);
		    }
	    }

	    _LightBuffer.SetData(lightPosRadius);
	    _lightCount = lightPosRadius.Count;
    }
	
    
    private void LightCulling(Camera camera)
    {
	    g_pointLightIndexCounter?.Release();
	    g_lightIndexList?.Release();
	    g_lightGrid?.Release();
		
	    g_pointLightIndexCounter = new ComputeBuffer(1, sizeof(uint));
	    g_pointLightIndexCounter.SetData(new uint[]{ 0 });
	    g_lightIndexList = new ComputeBuffer(1024 * _clusterXYZCount, sizeof(uint));
	    g_lightGrid = new ComputeBuffer(_clusterXYZCount, sizeof(uint) * 2);
	    
	    _cameraBuffer.SetComputeBufferParam(_clusterLightCullingCS, _lightCullingKernel, "g_lights", _LightBuffer);
	    _cameraBuffer.SetComputeBufferParam(_clusterLightCullingCS, _lightCullingKernel, "g_clusters", _clusterBuffer);
	    _cameraBuffer.SetComputeBufferParam(_clusterLightCullingCS, _lightCullingKernel, "g_pointLightIndexCounter", g_pointLightIndexCounter);
	    _cameraBuffer.SetComputeBufferParam(_clusterLightCullingCS, _lightCullingKernel, "g_lightIndexList", g_lightIndexList);
	    _cameraBuffer.SetComputeBufferParam(_clusterLightCullingCS, _lightCullingKernel, "g_lightGrid", g_lightGrid);
	    _cameraBuffer.SetComputeMatrixParam(_clusterLightCullingCS, "_CameraViewMatrix", camera.transform.worldToLocalMatrix);
	    _cameraBuffer.SetComputeIntParam(_clusterLightCullingCS, "g_lightCount", _lightCount);
	    _cameraBuffer.SetComputeIntParams(_clusterRenderingCS, "clusterCount", new int[]{ _ClusterXCount, _ClusterYCount, clusterZCount });
	    _cameraBuffer.DispatchCompute(_clusterLightCullingCS, _lightCullingKernel, _clusterXYZCount, 1, 1);
    }

    private void DrawDepthOnlyPass(Camera camera, ScriptableRenderContext context, CullingResults cullingResult)
    {
	    FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
	    filteringSettings.layerMask = camera.cullingMask;
	    var drawSettings = new DrawingSettings(new ShaderTagId("DepthOnly"), new SortingSettings(camera)
	    {
		    criteria = SortingCriteria.QuantizedFrontToBack,
	    });
	    
	    drawSettings.perObjectData = PerObjectData.None;
	    context.DrawRenderers(cullingResult, ref drawSettings, ref filteringSettings);
    }

    private void DebugLighting(Camera camera)
    {
	    if (_debugTexture == null)
	    {
		    _debugTexture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat);
		    _debugTexture.enableRandomWrite = true;
		    _debugTexture.Create();
	    }
	    
	    int clusterXCount = Mathf.CeilToInt((float) Screen.width / clusterWidth);
	    int clusterYCount = Mathf.CeilToInt((float) Screen.height / clusterHeight);
	    
	    _cameraBuffer.SetComputeTextureParam(_debugLightingCS, _debugLightingKernel, "debugTexture", _debugTexture);
	    _cameraBuffer.SetComputeTextureParam(_debugLightingCS, _debugLightingKernel, "depthTexture", new RenderTargetIdentifier(depthTargetTextureId));
	    _cameraBuffer.SetComputeTextureParam(_debugLightingCS, _debugLightingKernel, "sceneTexture", new RenderTargetIdentifier(colorTargetTextureId));
	    _cameraBuffer.SetComputeBufferParam(_debugLightingCS, _debugLightingKernel, "lightGridBuffer", g_lightGrid);
	    _cameraBuffer.SetComputeFloatParams(_debugLightingCS, "screenSize", new float[] { Screen.width, Screen.height, 1.0f / Screen.width, 1.0f / Screen.height });
	    _cameraBuffer.SetComputeIntParams(_clusterRenderingCS, "clusterCount", new int[]{ clusterXCount, clusterYCount, clusterZCount });
	    Matrix4x4 projectionMatrix;
	    if (camera.cameraType == CameraType.SceneView)
		    projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
	    else
		    projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
		
	    var projectionMatrixInverse = projectionMatrix.inverse;
	    _cameraBuffer.SetComputeMatrixParam(_debugLightingCS, "inverseProjectionMatrix", projectionMatrixInverse);
	    _cameraBuffer.DispatchCompute(_debugLightingCS, _debugLightingKernel, Mathf.CeilToInt((float)Screen.width / 32), Mathf.CeilToInt((float)Screen.height / 32), 1);
    }
    
    
    private RenderTextureDescriptor CreateRenderTextureDescriptor(Camera camera, int msaaSamples)
    {
	    RenderTextureDescriptor desc;
	    RenderTextureFormat renderTextureFormatDefault = RenderTextureFormat.Default;
	    
	    desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight);
	    desc.width = (int)((float)desc.width);
	    desc.height = (int)((float)desc.height);
	    

	    // TODO: when preserve framebuffer alpha is enabled we can't use RGB111110Float format.
	    bool useRGB111110 = Application.isMobilePlatform &&
	                        SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float);
	    RenderTextureFormat hdrFormat = (useRGB111110) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.DefaultHDR;
	    desc.colorFormat = renderTextureFormatDefault;
	    desc.depthBufferBits = 32;
	    desc.enableRandomWrite = false;
	    desc.sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);
	    desc.msaaSamples = msaaSamples;
	    desc.bindMS = false;
	    desc.useDynamicScale = camera.allowDynamicResolution;
	    return desc;
    }
    
    static RenderTextureDescriptor CreateRenderTextureDescriptor(Camera camera, bool isHdrEnabled, int msaaSamples)
    {
	    RenderTextureDescriptor desc;
	    RenderTextureFormat renderTextureFormatDefault = RenderTextureFormat.Default;
	    
	    desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight);
	    desc.width = (int)((float)desc.width);
	    desc.height = (int)((float)desc.height);
    

	    // TODO: when preserve framebuffer alpha is enabled we can't use RGB111110Float format.
	    bool useRGB111110 = Application.isMobilePlatform &&
	                        SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float);
	    RenderTextureFormat hdrFormat = (useRGB111110) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.DefaultHDR;
	    desc.colorFormat = isHdrEnabled ? hdrFormat : renderTextureFormatDefault;
	    desc.depthBufferBits = 32;
	    desc.enableRandomWrite = false;
	    desc.sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);
	    desc.msaaSamples = msaaSamples;
	    desc.bindMS = false;
	    desc.useDynamicScale = camera.allowDynamicResolution;
	    return desc;
    }

    private void debugClusterRendering()
    {
	    Material debugMat = Resources.Load<Material>("debugClusterMat");
	    debugMat.SetBuffer("ClusterAABBs", _clusterBuffer);
	    //debugMat.SetBuffer("ClusterGrid", g_lightGrid);
	    debugMat.SetMatrix("_CameraWorldMatrix", Camera.main.transform.localToWorldMatrix);
	    _cameraBuffer.DrawProcedural(Matrix4x4.identity, debugMat, 0, MeshTopology.Points, _clusterXYZCount);
    }
}