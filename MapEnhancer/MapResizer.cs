using UI.Common;
using UI.Map;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MapEnhancer
{
	[RequireComponent(typeof(Image))]
	public class MapResizer : PanelResizer, IPointerDownHandler, IPointerUpHandler, IEventSystemHandler, IDragHandler
	{
	private Vector2 originalSize;
	private float aspect;
	private Window window;
	private MapWindow mapWindow;
	private AspectRatioFitter aspectRatioFitter;
	private ResizeNotifier notifier;
	private Vector2 windowMargins;
	private Camera mapCamera;
	private Canvas canvas;
	Vector2 largeWindowPos;
	Vector2 largeWindowSize;
	private bool isLarge;
	private Vector2 defaultSize; // The default/normal size (from WindowSizeMin setting)

	public static MapResizer Create()
		{
			var mapWindow = MapWindow.instance;

			var resizerGo = new GameObject("Resizer Handle");
			resizerGo.SetActive(false);
			var resizer = resizerGo.AddComponent<MapResizer>();
			resizerGo.transform.SetParent(MapWindow.instance._window.transform, false);
			resizerGo.SetActive(mapWindow._window.IsShown);

			return resizer;
		}

		new private void Awake()
		{
			base.Awake();

			window = GetComponentInParent<Window>();
			mapWindow = GetComponentInParent<MapWindow>();
			transform.gameObject.SetActive(window.IsShown);
			mapCamera = MapBuilder.Shared.mapCamera;
			canvas = GetComponentInParent<Canvas>().rootCanvas;

			// Use the window's InitialContentSize which is the vanilla size scaled by UI settings
			// This respects screen resolution and UI scale from game settings
			originalSize = window.InitialContentSize + new Vector2(48f, 48f); // Add typical window frame margins
			var scale = MapEnhancer.Instance.Settings.WindowSizeMin / 800f;
			defaultSize = new Vector2(originalSize.x * scale, originalSize.y * scale);
			minSize = defaultSize; // Keep minSize for compatibility
			aspect = originalSize.x / originalSize.y;
			windowMargins = originalSize - window.InitialContentSize;

			// Reset to default size on startup
			_windowRectTransform.sizeDelta = defaultSize;
			
			// Allow users to resize down to 50% of the default size for more flexibility
			// This gives extra room for users who want a very small map window
			var absoluteMinSize = defaultSize * 0.5f;
			base.minSize = absoluteMinSize;

			var canvasRect = _windowRectTransform.parent.GetComponent<RectTransform>().rect;
			largeWindowSize = canvasRect.size * 0.75f;			notifier = canvas.gameObject.AddComponent<ResizeNotifier>();
			notifier.RectTransformDimensionsChanged += OnRectCanvasTransformChanged;

			AdjustRenderTexture();
			AddAspectRatioFitter();
			CreateDragHandle();
		}

		void OnEnable()
		{
			var windowRectTransform = window._rectTransform;
			var parentRectTransform = _windowRectTransform.parent.GetComponent<RectTransform>();
			if (windowRectTransform.sizeDelta.x > parentRectTransform.sizeDelta.x || windowRectTransform.sizeDelta.y > parentRectTransform.sizeDelta.y)
				windowRectTransform.sizeDelta = parentRectTransform.sizeDelta;
		}
		
	new public void OnPointerDown(PointerEventData data)
	{
		UpdateWindowSize();
		base.OnPointerDown(data);
	}

	new public void OnDrag(PointerEventData data)
	{
		// Call base class to handle the actual resize logic
		base.OnDrag(data);
		
		// Update render texture during drag to prevent blur/pixelation
		AdjustRenderTexture();
	}

	new public void OnPointerUp(PointerEventData data)
	{
		// Call base class to handle pointer up logic
		base.OnPointerUp(data);
		
		if (isLarge)
			largeWindowSize = _windowRectTransform.sizeDelta;
		
		// Final render texture update after drag completes
		AdjustRenderTexture();
	}
		void UpdateWindowSize()
		{
			Rect canvasRect = _windowRectTransform.parent.GetComponent<RectTransform>().rect;
			
			maxSize = new Vector2(canvasRect.max.x - _windowRectTransform.localPosition.x,
								  _windowRectTransform.localPosition.y - canvasRect.min.y);

			if (canvasRect.width / canvasRect.height < aspect)
			{
				maxSize.x = Mathf.Min(maxSize.x, maxSize.y * aspect);
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
			}
			else
			{
				maxSize.y = Mathf.Min(maxSize.y, maxSize.x / aspect);
				aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
			}
		}

		private void AddAspectRatioFitter()
		{
			aspectRatioFitter = transform.parent.gameObject.AddComponent<AspectRatioFitter>();
			aspectRatioFitter.aspectRatio = aspect;
			aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
		}

		private void AdjustRenderTexture()
		{
			RenderTexture rt = mapWindow._renderTexture;
			var height = (int)Mathf.Round(canvas.renderingDisplaySize.y - windowMargins.y);
			var width = (int)Mathf.Round(canvas.renderingDisplaySize.y * aspect - windowMargins.x);
			if (rt.width != width || rt.height != height)
			{
				rt.Release();
				rt.width = width;
				rt.height = height;
				mapCamera.aspect = (float)rt.width / (float)rt.height;
				rt.antiAliasing = (int)(MapEnhancer.Instance?.Settings.MSAA ?? MsaaQuality._4x);
			}
		}

		private void CleanupRenderTexture()
		{
			Rect rect = window.contentRectTransform.rect;
			RenderTexture rt = mapWindow._renderTexture;
			rt.Release();
			rt.width = (int)rect.width;
			rt.height = (int)rect.height;
			rt.antiAliasing = 1;
		}

		private void OnRectCanvasTransformChanged(RectTransform transform)
		{
			AdjustRenderTexture();
		}

		private void CreateDragHandle()
		{
			var image = transform.GetComponent<Image>();
			image.color = new Color(70f/255f, 69f/255f, 55f/255f, 5f/255f);
			image.sprite = Resources.Load<Sprite>("UI/ResizeWidget");

			var rect = GetComponent<RectTransform>();
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 4f, 22f);
			rect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 4f, 22f);
		}

	public void SetMinimumSize(float size)
	{
		if (window == null) return;
		defaultSize = originalSize * (size/800f);
		minSize = defaultSize; // Keep minSize for compatibility
		
		// Update absolute minimum to 50% of new default size
		base.minSize = defaultSize * 0.5f;

		if (!isLarge)
		{
			var windowRectTransform = window._rectTransform;
			windowRectTransform.sizeDelta = defaultSize;
		}
	}		public void Toggle()
		{
			if (window == null)
			{
				MapWindow.Show();
				return;
			}

			var isShown = window!.IsShown;
			if (!isShown)
			{
				window.ShowWindow();
				isLarge = false;
			}
			else
			{
				isLarge = !isLarge;
			}

			if (isLarge)
			{
				_windowRectTransform.anchoredPosition = largeWindowPos;
				_windowRectTransform.sizeDelta = largeWindowSize;
			}
			else
			{
			largeWindowPos = _windowRectTransform.anchoredPosition;
			_windowRectTransform.anchoredPosition = Vector2.zero;
			_windowRectTransform.sizeDelta = defaultSize; // Use defaultSize, not minSize
			}
			if (isShown)
				window.ClampToParentBounds();
		}

		void OnDestroy()
		{
			if (aspectRatioFitter) DestroyImmediate(aspectRatioFitter);
			window._rectTransform.anchoredPosition = Vector2.zero;
			window._rectTransform.sizeDelta = originalSize;

			if (notifier) DestroyImmediate(notifier);
			CleanupRenderTexture();
		}
	}
}
