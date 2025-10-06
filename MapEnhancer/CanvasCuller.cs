using Helpers;
using Helpers.Culling;
using System;
using UI.Map;
using UnityEngine;

public class CanvasCuller : MonoBehaviour, CullingManager.ICullingEventHandler
{
	private void Awake()
	{
		canvas = transform.GetComponent<Canvas>();
	}

	private void OnEnable()
	{
		CullingManager.Token cullingToken = _cullingToken;
		if (cullingToken != null)
		{
			cullingToken.Dispose();
		}

		CullingManager cullingManager = FindObjectOfType<CullingManager>();
		if (cullingManager == null)
		{
			GameObject go = new GameObject("CanvasCullingManager");
			cullingManager = go.AddComponent<CullingManager>();
			cullingManager.Configure("canvas", new[] { float.PositiveInfinity });
		}  
		_cullingToken = cullingManager.AddSphere(transform, radius, this);
		_cullingToken.RegisterFixedUpdate(transform);
		var camera = MapBuilder.Shared.mapCamera;
		cullingManager._cullingGroup.targetCamera = camera;
		cullingManager._cullingGroup.SetDistanceReferencePoint(camera.transform);
		return;
	}

	private void OnDisable()
	{
		_cullingToken.Dispose();
		_cullingToken = null;
	}

	public void CullingSphereStateChanged(bool isVisible, int distanceBand)
	{
		CanvasCuller.CullingState cullingState = (isVisible ? CanvasCuller.CullingState.Visible : CanvasCuller.CullingState.NotVisible);
		if (cullingState == _cullingState)
		{
			return;
		}
		_cullingState = cullingState;
		bool flag = _cullingState == CanvasCuller.CullingState.Visible;
		canvas.enabled = flag;
	}

	public void RequestUpdateCullingPosition()
	{
		_cullingToken.UpdatePosition(transform);
	}

	private CullingManager.Token _cullingToken;

	private CanvasCuller.CullingState _cullingState;

	private Canvas canvas;

	public float radius = 30f;

	private enum CullingState
	{
		Unknown,
		Visible,
		NotVisible
	}
}
