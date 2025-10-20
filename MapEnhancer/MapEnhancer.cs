using Character;
using Core;
using GalaSoft.MvvmLight.Messaging;
using Game;
using Game.AccessControl;
using Game.Events;
using Game.Messages;
using Game.State;
using HarmonyLib;
using Helpers;
using Map.Runtime;
using MapEnhancer.UMM;
using Model;
using Model.Definition;
using Model.Ops;
using RollingStock;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using TMPro;
using Track;
using Track.Signals;
using UI;
using UI.Builder;
using UI.CarInspector;
using UI.Console.Commands;
using UI.Map;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MapEnhancer;

public class MapEnhancer : MonoBehaviour
{
	public enum MapStates { MAINMENU, MAPLOADED, MAPUNLOADING }
	public static MapStates MapState { get; private set; } = MapStates.MAINMENU;
	internal Loader.MapEnhancerSettings Settings;
	public GameObject Junctions;
	public GameObject JunctionsBranch;
	public GameObject JunctionsMainline;
	private List<Entry> junctionMarkers = new List<Entry>();
	private CullingGroup cullingGroup;
	private BoundingSphere[] cullingSpheres;
	private MapResizer resizer;
	private bool mapFollowMode;
	private UnityEngine.Component? mapCameraTarget;
	public RectTransform mapSettings;
	private Sprite dropdownSprite;

	// Holder stops "prefab" from going active immediately
	private static GameObject _prefabHolder;
	internal static GameObject prefabHolder
	{
		get
		{
			if (_prefabHolder == null)
			{
				_prefabHolder = new GameObject("Prefab Holder");
				_prefabHolder.hideFlags = HideFlags.HideAndDontSave;
				_prefabHolder.SetActive(false);
			}
			return _prefabHolder;
		}
	}

	private static MapIcon _traincarPrefab;
	public static MapIcon? traincarPrefab
	{
		get
		{
			if (_traincarPrefab == null) CreateTraincarPrefab();
			return _traincarPrefab;
		}
	}

	private static MapIcon _flarePrefab;
	public static MapIcon? flarePrefab
	{
		get
		{
			if (_flarePrefab == null) CreateFlarePrefab();
			return _flarePrefab;
		}
	}

	private Coroutine traincarColorUpdater;

	private static HashSet<string> _mainlineSegments;
	private static HashSet<string> _industrialSegments = new HashSet<string>();
	private static HashSet<string> _passengerStopSegments = new HashSet<string>();
	private static bool _isMapFullyLoaded = false;
	
	public static HashSet<string> mainlineSegments
	{
		get
		{
			if (_mainlineSegments == null)
				populateSegmentsAndSwitches();

			return _mainlineSegments!;
		}
	}

	private static HashSet<string> _mainlineSwitches;
	public static HashSet<string> mainlineSwitches
	{
		get
		{
			if (_mainlineSwitches == null)
				populateSegmentsAndSwitches();

			return _mainlineSwitches!;
		}
	}

	private static void populateSegmentsAndSwitches()
	{
		_mainlineSegments = new HashSet<string>();
		_mainlineSwitches = new HashSet<string>();
		var ctcBlocks = FindObjectsOfType<CTCBlock>(true);
		
		Loader.LogDebug($"Found {ctcBlocks.Length} CTC blocks for mainline identification");
		
		foreach (var span in ctcBlocks.SelectMany(block => block.Spans))
		{
			span.UpdateCachedPointsIfNeeded();
			foreach (var seg in span._cachedSegments)
			{
				_mainlineSegments.Add(seg.id);
				_mainlineSwitches.Add(seg.a.id);
				_mainlineSwitches.Add(seg.b.id);
			}
		}
		
		Loader.LogDebug($"Identified {_mainlineSegments.Count} mainline segments and {_mainlineSwitches.Count} mainline switches");
	}

	private static void ReclassifyAllTrackSegments()
	{
		// Skip if using visual-only mode
		if (Instance?.Settings.UseVisualOnlyTrackColors ?? false)
		{
			Loader.LogDebug("Skipping track reclassification - visual-only mode enabled");
			return;
		}
			
		// Re-classify all existing track segments to handle race conditions with other mods
		var segments = FindObjectsOfType<TrackSegment>();
		int mainlineCount = 0;
		int branchCount = 0;
		int industrialCount = 0;
		int preservedCount = 0;
		
		foreach (var segment in segments)
		{
			// Preserve existing industrial classifications
			if (segment.trackClass == TrackClass.Industrial)
			{
				preservedCount++;
				industrialCount++;
				continue;
			}
			
			// Only reclassify mainline vs branch for non-industrial tracks
			if (mainlineSegments.Contains(segment.id))
			{
				segment.trackClass = TrackClass.Mainline;
				mainlineCount++;
			}
			else
			{
				segment.trackClass = TrackClass.Branch;
				branchCount++;
			}
		}
		
		Loader.LogDebug($"Reclassified {segments.Length} track segments: {mainlineCount} mainline, {branchCount} branch, {industrialCount} industrial preserved, {preservedCount} total preserved");
	}

	private static void ReclassifyIndustrialTracks()
	{
		// Skip if using visual-only mode
		if (Instance?.Settings.UseVisualOnlyTrackColors ?? false)
		{
			Loader.LogDebug("Skipping industrial track reclassification - visual-only mode enabled");
			return;
		}
			
		// Ensure all industrial tracks are properly classified after our bulk reclassification
		var industries = FindObjectsOfType<IndustryComponent>();
		int industrialTracksSet = 0;
		
		foreach (var industry in industries)
		{
			if (industry is ProgressionIndustryComponent) continue;
			
			foreach (var tspan in industry.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					seg.trackClass = TrackClass.Industrial;
					industrialTracksSet++;
				}
			}
		}
		
		Loader.LogDebug($"Set {industrialTracksSet} industrial track segments from {industries.Length} industry components");
	}

	public static MapEnhancer Instance
	{
		get { return Loader.Instance; }
	}

	void Start()
	{
		Messenger.Default.Register<MapDidLoadEvent>(this, new Action<MapDidLoadEvent>(this.OnMapDidLoad));
		Messenger.Default.Register<MapWillUnloadEvent>(this, new Action<MapWillUnloadEvent>(this.OnMapWillUnload));

		if (StateManager.Shared.Storage != null)
		{
			OnMapDidLoad(new MapDidLoadEvent());
		}
	}

	void OnDestroy()
	{
		Loader.LogDebug("OnDestroy");

		if (JunctionMarker.matJunctionGreen != null) Destroy(JunctionMarker.matJunctionGreen);
		if (JunctionMarker.matJunctionRed != null) Destroy(JunctionMarker.matJunctionRed);

		if (prefabHolder != null)
		{
			//TODO cleanup sprite/tex
			DestroyImmediate(prefabHolder);
		}

		Messenger.Default.Unregister<MapDidLoadEvent>(this);
		Messenger.Default.Unregister<MapWillUnloadEvent>(this);

		if (MapState == MapStates.MAPLOADED)
		{
			OnMapWillUnload(new MapWillUnloadEvent());
			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();

			if (_traincarPrefab != null) Destroy(_traincarPrefab);
			_traincarPrefab = null;

			DestroyTraincarMarkers();
			DestroyFlareMarkers();
		}
		MapState = MapStates.MAINMENU;
	}

	private void OnMapDidLoad(MapDidLoadEvent evt)
	{
		Loader.LogDebug("OnMapDidLoad");
		if (MapState == MapStates.MAPLOADED) return;
		Loader.LogDebug("OnMapDidLoad2");

		MapState = MapStates.MAPLOADED;
		CleanupIconsAndLabels();
		JunctionMarker.CreatePrefab();

		Junctions = new GameObject("Junctions");
		Junctions.SetActive(MapWindow.instance._window.IsShown);
		JunctionsMainline = new GameObject("Mainline Junctions");
		JunctionsMainline.transform.SetParent(Junctions.transform, false);
		JunctionsBranch = new GameObject("Branch Junctions");
		JunctionsBranch.transform.SetParent(Junctions.transform, false);
		Junctions.SetActive(MapWindow.instance._window.IsShown);

		MapWindow.instance._window.OnShownDidChange += OnMapWindowShown;

		GatherTraincarMarkers();
		traincarColorUpdater = StartCoroutine(TraincarColorUpdater());

		GatherFlareMarkers();

		Messenger.Default.Register<WorldDidMoveEvent>(this, new Action<WorldDidMoveEvent>(this.WorldDidMove));
		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;

		MapBuilder.Shared.mapCamera.GetComponent<UniversalAdditionalCameraData>().requiresDepthOption = CameraOverrideOption.Off;

		// Now that the map is fully loaded, populate mainline segments and classify all existing tracks
		_isMapFullyLoaded = true;
		// Force re-population to ensure we have the most up-to-date CTC blocks from all mods
		_mainlineSegments = null;
		_mainlineSwitches = null;
		// Re-classify all existing track segments now that we know which are mainline
		ReclassifyAllTrackSegments();
		// Ensure industrial tracks are properly set after our reclassification
		ReclassifyIndustrialTracks();

		Rebuild();
		resizer = MapResizer.Create();
		OnSettingsChanged();
		if (MapWindow.instance._window.IsShown)
		{
			MapBuilder.Shared.Rebuild();
			OnMapWindowShown(true);
		}
	}

	private void CleanupIconsAndLabels()
	{
		var mi = Resources.FindObjectsOfTypeAll<MapIcon>();
		var ml = Resources.FindObjectsOfTypeAll<MapLabel>();

		foreach (var m in mi)
		{
			var gr = m.GetComponent<GraphicRaycaster>();
			var cs = m.GetComponent<CanvasScaler>();
			if (gr) DestroyImmediate(gr);
			if (cs) DestroyImmediate(cs);
		}
		foreach (var m in ml)
		{
			var gr = m.GetComponent<GraphicRaycaster>();
			var cs = m.GetComponent<CanvasScaler>();
			if (gr) DestroyImmediate(gr);
			if (cs) DestroyImmediate(cs);
		}
	}

	private void OnMapWillUnload(MapWillUnloadEvent evt)
	{
		Loader.LogDebug("OnMapWillUnload");

		MapState = MapStates.MAPUNLOADING;
		// Reset flag to prevent track classification during map unload/reload
		_isMapFullyLoaded = false;
		
		// Clear tracking sets
		_passengerStopSegments.Clear();
		_industrialSegments.Clear();
		
		Messenger.Default.Unregister<WorldDidMoveEvent>(this);
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}
		cullingGroup = null;

		if (Junctions != null) Destroy(Junctions);
		junctionMarkers.Clear();

		if (traincarColorUpdater != null) StopCoroutine(traincarColorUpdater);

		MapWindow.instance._window.OnShownDidChange -= OnMapWindowShown;

		if (resizer)
			DestroyImmediate(resizer.gameObject);

		MapBuilder.Shared.mapCamera.GetComponent<UniversalAdditionalCameraData>().requiresDepthOption = CameraOverrideOption.On;

		if (mapSettings) Destroy(mapSettings.gameObject);
		if (dropdownSprite != null)
		{
			Destroy(dropdownSprite.texture);
			Destroy(dropdownSprite);
		}
	}

	private void WorldDidMove(WorldDidMoveEvent evt)
	{
		Loader.LogDebug("WorldDidMove");

		var worldPos = WorldTransformer.GameToWorld(new Vector3(0, 0, 0));
		Junctions.transform.position = worldPos;
		UpdateCullingSpheres();
	}

	private void OnMapWindowShown(bool shown)
	{
		Junctions?.SetActive(shown);

		if (shown && mapSettings == null)
		{
			CreateMapSettings();
		}
	}

	private void CreateMapSettings()
	{
		var settingsGo = new GameObject("Map Settings", typeof(RectTransform));
		mapSettings = settingsGo.GetComponent<RectTransform>();
		mapSettings.SetParent(MapWindow.instance._window.transform, false);
		mapSettings.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 27, 30);
		mapSettings.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 4, 145);

		var panel = UIPanel.Create(mapSettings, FindObjectOfType<ProgrammaticWindowCreator>().builderAssets, builder =>
		{
			builder.FieldLabelWidth = 105f;
			builder.AddField("Follow Mode", builder.AddToggle(() => mapFollowMode, (x) => mapFollowMode = x));
			TMP_Dropdown? dropdown = null;
			var teleportLocations = GetTeleportLocations();
			dropdown = builder.AddDropdown(teleportLocations, 0, (index) =>
			{
				SpawnPoint spawnPoint = TeleportCommand.SpawnPointFor(teleportLocations[index].text);
				if (spawnPoint != null && index != 0)
				{
					var mapCamera = MapBuilder.Shared.mapCamera.transform;
					ValueTuple<Vector3, Quaternion> gamePositionRotation = spawnPoint.GamePositionRotation;
					Vector3 item = gamePositionRotation.Item1;
					item.y = mapCamera.position.y;
					mapCamera.position = WorldTransformer.GameToWorld(item);
					dropdown!.SetValueWithoutNotify(0);
				}
			}).GetComponent<TMP_Dropdown>();

			AddLocoSelectorDropdown(builder);
		});
		settingsGo.AddComponent<Image>().color = new Color(0.1098f, 0.1098f, 0.1098f, 1f);

		void AddLocoSelectorDropdown(UIPanelBuilder builder)
		{
			RectTransform rectTransform = builder.CreateRectView("DropDown", 0, 0);

			TMP_Dropdown? dropdown = null;
			var mapCamera = MapBuilder.Shared.mapCamera.transform;
			var locoList = TrainController.Shared.Cars.Where((Car car) => car.IsLocomotive).OrderBy(car => car.SortName).ToList();
			dropdown = builder.AddDropdown(new List<string>() { "Locomotive..." }, 0, index =>
			{
				if (index == 0) return;
				var cars = dropdown!.GetComponent<DropdownClickHandler>().cars;
				var car = cars[index - 1];
				if (car == null) return;

				var position = WorldTransformer.GameToWorld(car.GetCenterPosition(Graph.Shared));
				position.y = mapCamera.position.y;
				mapCamera.position = position;
				dropdown!.SetValueWithoutNotify(0);
				mapCameraTarget = car;
				MapWindow.instance.mapDrag._isDragging = false;
			}).GetComponent<TMP_Dropdown>();
			dropdown.gameObject.AddComponent<DropdownClickHandler>();
			DestroyImmediate(dropdown.template.transform.Find("Viewport/Content/Item/Item Checkmark").gameObject);
			dropdown.template.transform.Find("Viewport/Content/Item/Item Label").GetComponent<RectTransform>().offsetMin = new Vector2(10f, 1f);
			dropdown.template.sizeDelta = new Vector2(0f, 15 * 20f + 8f);
		}

		List<TMP_Dropdown.OptionData> GetTeleportLocations()
		{
			var POIs = GameObject.Find("World/POIs").transform;
			if (!POIs.Find("Connelly"))
			{
				var sp = new GameObject("Connelly", typeof(SpawnPoint)).transform;
				sp.SetParent(POIs, false);
				sp.position = WorldTransformer.GameToWorld(new Vector3(11896.60f, 608.67f, 2875.93f));
				sp.eulerAngles = new Vector3(0, 255f, 0);
			}

			var logCamp = POIs.Find("LogCamp1");
			if (logCamp) logCamp.name = "Walker";

			var _teleportLocations = new List<TMP_Dropdown.OptionData>() { new TMP_Dropdown.OptionData("Location...") };
			var areas = OpsController.Shared.Areas;
			Dictionary<string, Color> locationColorLookup = new Dictionary<string, Color>();
			locationColorLookup = SpawnPoint.All.Where(sp => sp.name.ToLower() != "ds").Select(sp =>
			{
				var area = OpsController.Shared.ClosestAreaForGamePosition(sp.GamePositionRotation.Item1);
				var color = area ? area.tagColor : Color.grey;
				return new { sp.name, color };
			}).ToDictionary(kvp => kvp.name, kvp => kvp.color);

			var tex = new Texture2D(20, 20);
			dropdownSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector3.zero);
			_teleportLocations.AddRange(SpawnPoint.All.Where(sp => sp.name.ToLower() != "ds").Select(sp => new TMP_Dropdown.OptionData(sp.name, dropdownSprite, locationColorLookup[sp.name])).OrderBy(sp =>
			{
				float h, s, v;
				Color.RGBToHSV(sp.color, out h, out s, out v);

				return h;
			}));

			return _teleportLocations;
		}
	}

	private IEnumerator TraincarColorUpdater()
	{
		for (;;)
		{
			foreach (var marker in MapBuilder.Shared._mapIcons.ToArray())
			{
				if (marker == null) continue;

				Car car = marker.transform.parent.GetComponent<Car>();
				if (car == null) continue;

				var image = marker.GetComponentInChildren<Image>(true);
				marker.Text.gameObject.SetActive(!car.IsInBardo);
				image.gameObject.SetActive(!car.IsInBardo);

				if (car.Archetype.IsFreight())
				{
					string text;
					bool flag;
					Vector3 vector;
					OpsCarPosition opsCarPosition;
					OpsController opsController = OpsController.Shared;
					Color color = Color.white;

					if (opsController != null && opsController.TryGetDestinationInfo(car, out text, out flag, out vector, out opsCarPosition))
					{
						Area area = opsController.AreaForCarPosition(opsCarPosition);

						if (area) color = area.tagColor;

						if (!flag)
						{
							var intensity = 1 / color.maxColorComponent;
							color *= intensity;
						}
					}
					image.color = color;
					
					yield return null;
				}
			}
			yield return null;
		}
	}

	public void Rebuild()
	{
		Loader.LogDebug("Rebuild");
		if (cullingGroup != null)
		{
			cullingGroup.Dispose();
		}

		CreateSwitches();

		Camera mapCamera = MapBuilder.Shared.mapCamera;
		cullingGroup = new CullingGroup();
		cullingGroup.targetCamera = mapCamera;
		cullingGroup.SetBoundingSphereCount(0);
		cullingGroup.SetBoundingSpheres(cullingSpheres);
		cullingGroup.onStateChanged = new CullingGroup.StateChanged(CullingGroupStateChanged);
		cullingGroup.SetBoundingDistances(new float[] { float.PositiveInfinity });
		cullingGroup.SetDistanceReferencePoint(mapCamera.transform);
		cullingSpheres = new BoundingSphere[junctionMarkers.Count];
		UpdateCullingSpheres();
		cullingGroup.SetBoundingSpheres(cullingSpheres);
		cullingGroup.SetBoundingSphereCount(cullingSpheres.Length);
	}

	private void CullingGroupStateChanged(CullingGroupEvent sphere)
	{
		int index = sphere.index;

		var sd = junctionMarkers[index].SwitchDescriptor;

		if (sphere.isVisible && !sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(true);
		}
		else if (!sphere.isVisible && sphere.wasVisible)
		{
			junctionMarkers[index].JunctionMarker.SetActive(false);
		}
	}

	private void UpdateCullingSpheres()
	{
		for (int i = 0; i < TrackObjectManager.Instance._descriptors.switches.Count; i++)
		{
			var geo = junctionMarkers[i].SwitchDescriptor.geometry;
			Vector3 vector = WorldTransformer.GameToWorld(geo.switchHome);
			this.cullingSpheres[i] = new BoundingSphere(vector, 1f);
		}
	}

	private void CreateSwitches()
	{
		Loader.LogDebug("CreateSwitches");
		foreach (var jm in junctionMarkers) Destroy(jm.JunctionMarker);
		junctionMarkers.Clear();
		foreach (var kvp in TrackObjectManager.Instance._descriptors.switches)
		{
			var sd = kvp.Value;
			TrackNode node = sd.node;

			var junctionMarker = new GameObject($"JunctionMarker ({node.id})");
			junctionMarker.SetActive(false);
			if (mainlineSwitches.Contains(node.id))
				junctionMarker.transform.SetParent(JunctionsMainline.transform, false);
			else
				junctionMarker.transform.SetParent(JunctionsBranch.transform, false);
			junctionMarkers.Add(new Entry(sd, junctionMarker));
			junctionMarker.transform.localPosition = sd.geometry.switchHome + Vector3.up * 2000f;
			junctionMarker.transform.localRotation = sd.geometry.aPointRail.Points.First().Rotation;
			JunctionMarker jm = sd.geometry.aPointRail.hand == Hand.Right ?
				JunctionMarker.junctionMarkerPrefabL :
				JunctionMarker.junctionMarkerPrefabR;

			jm = GameObject.Instantiate(jm, junctionMarker.transform);
			jm.junction = node;
		}
	}

	public void OnSettingsChanged()
	{
		if (MapState != MapStates.MAPLOADED) return;

		foreach (var junctionMarker in JunctionMarker.junctionMarkerPrefabL.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}
		foreach (var junctionMarker in JunctionMarker.junctionMarkerPrefabR.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}
		foreach (var junctionMarker in Junctions.GetComponentsInChildren<CanvasRenderer>(true))
		{
			junctionMarker.transform.localScale = Vector3.one * Settings.JunctionMarkerScale;
		}

		if (_flarePrefab != null)
			_flarePrefab.GetComponentInChildren<Image>(true).transform.localScale = Vector3.one * Settings.FlareScale;
		foreach (var flare in FlareManager.Shared._instances.Values)
		{
			var icon = flare.GetComponentInChildren<Image>(true);
			if (icon != null)
			{
				icon.transform.localScale = Vector3.one * Settings.FlareScale;
			}
		}

		var mapBuilder = MapBuilder.Shared;
		mapBuilder.segmentLineWidthMin = Settings.TrackLineThickness;
		mapBuilder.segmentLineWidthMax = Settings.MapZoomMax / 5000f * 20;

		var renderTex = mapBuilder.mapCamera.targetTexture;
		if (renderTex && renderTex.antiAliasing != (int)Settings.MSAA)
		{
			renderTex.Release();
			renderTex.antiAliasing = (int)Settings.MSAA;
		}

		if (MapWindow.instance._window.IsShown)
		{
			mapBuilder.mapCamera.orthographicSize =
				Mathf.Clamp(MapBuilder.Shared.mapCamera.orthographicSize, Settings.MapZoomMin, Settings.MapZoomMax);
			mapBuilder.UpdateForZoom();
		}
		resizer.SetMinimumSize(Settings.WindowSizeMin);
	}

	private static void CreateTraincarPrefab()
	{
		var sprite = LoadTexture("traincar.png", "MapTraincarIcon");
		foreach (var mapIcon in Resources.FindObjectsOfTypeAll<MapIcon>().Where(MapIcon => MapIcon.name.StartsWith("Map Icon Locomotive")))
		{
			var image = mapIcon.transform.Find("Image");
			image.localScale = new Vector3(0.8f, 0.8f, 0.8f);
			DestroyImmediate(image.GetComponent<Collider>());
			image.gameObject.AddComponent<BoxCollider>().size = new Vector3(40f, 92f, 1f);
			mapIcon.Text.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
		}
		_traincarPrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject trainCarMarker = _traincarPrefab.gameObject;
		trainCarMarker.hideFlags = HideFlags.HideAndDontSave;
		trainCarMarker.name = "Map Icon Traincar";
		_traincarPrefab.GetComponentInChildren<Image>().sprite = sprite;

		trainCarMarker.AddComponent<CanvasCuller>();
	}

	private static void CreateFlarePrefab()
	{
		var scale = Instance?.Settings.FlareScale ?? 0.6f;
		var sprite = LoadTexture("flare.png", "MapFlareIcon");
		_flarePrefab = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, prefabHolder.transform);
		GameObject flareMarker = _flarePrefab.gameObject;
		flareMarker.hideFlags = HideFlags.HideAndDontSave;
		flareMarker.name = "Map Icon Flare";
		if (_flarePrefab.Text) DestroyImmediate(_flarePrefab.Text.gameObject);
		var image = _flarePrefab.GetComponentInChildren<Image>();
		image.sprite = sprite;
		image.transform.localScale = Vector3.one * scale;
	}

	public static Sprite? LoadTexture(string fileName, string name)
	{
		string iconPath = Path.Combine(Loader.ModEntry.Path, fileName);
		var tex = new Texture2D(128, 128, TextureFormat.DXT5, false);
		tex.name = name;
		tex.wrapMode = TextureWrapMode.Clamp;
		if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(iconPath)))
		{
			Loader.Log("Unable to load traincar icon!");
			return null;
		}
		Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
		sprite.name = name;
		return sprite;
	}

	private void GatherTraincarMarkers()
	{
		foreach (Car car in TrainController.Shared.Cars)
		{
			var marker = car.GetComponentInChildren<MapIcon>(true);

			if (car.Archetype.IsLocomotive())
			{
				void OnClick()
				{
					if (Instance == null) 
					{
						marker.OnClick = delegate { CarPickable.HandleShowInspector(car); };
						CarPickable.HandleShowInspector(car);
						return;
					}

					Instance.mapCameraTarget = car;
					MapWindow.instance.mapDrag._isDragging = false;

					if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = car;
					if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(car);

					CarInspector.Show(car);
				}
				marker.OnClick = OnClick;
			}
			else
			{
				if (!marker)
					AddTraincarMarker(car);
			}
		}
	}

	private void DestroyTraincarMarkers()
	{
		foreach (Car car in TrainController.Shared.Cars)
		{
			if (!car.Archetype.IsLocomotive())
			{
				var marker = car.GetComponentInChildren<MapIcon>();
				if (marker)
					DestroyImmediate(marker.gameObject);
			}
		}
	}

	internal static void AddTraincarMarker(Car car)
	{
		car.MapIcon = Instantiate<MapIcon>(traincarPrefab, car.transform);
		if (car.Archetype == CarArchetype.Tender)
			car.MapIcon.SetText(car.Ident.RoadNumber);
		else
			car.MapIcon.SetText($"<line-height=70%>{car.Ident.ReportingMark}\n{car.Ident.RoadNumber}");
		var image = car.MapIcon.GetComponentInChildren<Image>();
		var scale = image.transform.localScale;
		scale.y = (car.carLength / 11) * scale.y;
		image.transform.localScale = scale;
		var text = car.MapIcon.GetComponentInChildren<TMP_Text>();
		text.horizontalAlignment = HorizontalAlignmentOptions.Left;
		text.enableAutoSizing = false;
		text.fontSizeMin = 19;
		text.fontSize = 19;
		text.autoSizeTextContainer = true;
		text.transform.localPosition = Vector3.zero;


		car.MapIcon.OnClick = delegate
		{
			if (Instance) Instance.mapCameraTarget = car;
			MapWindow.instance.mapDrag._isDragging = false;

			if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = car;
			if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(car);

			CarInspector.Show(car);
		};
		car.UpdateMapIconPosition(car._mover.Position, car._mover.Rotation);
	}

	private void GatherFlareMarkers()
	{
		foreach (GameObject flareGO in FlareManager.Shared._instances.Values)
		{
			var flare = flareGO.GetComponentInChildren<FlarePickable>();
			var marker = flare.GetComponentInChildren<MapIcon>();
			if (!marker)
				AddFlareMarker(flare);
		}
	}

	private void DestroyFlareMarkers()
	{
		foreach (GameObject flareGO in FlareManager.Shared._instances.Values)
		{
				var marker = flareGO.GetComponentInChildren<MapIcon>();
				if (marker)
					DestroyImmediate(marker.gameObject);
		}
	}

	internal static void AddFlareMarker(FlarePickable flare)
	{
		var mapIcon = Instantiate<MapIcon>(flarePrefab, flare.transform.parent);
		var posRot = flare.transform.parent.parent.GetComponent<TrackMarker>().PositionRotation;
		mapIcon.transform.localPosition = mapIcon.transform.localPosition + Vector3.up * 1000f;
		mapIcon.transform.rotation = Quaternion.Euler(90f, posRot.Value.Rotation.eulerAngles.y, 0f);
		mapIcon.OnClick = delegate
		{
			flare.Activate(ObjectPicker.CreateEvent(PickableActivation.Primary));
		};
	}

	void LateUpdate()
	{
		if (MapState != MapStates.MAPLOADED) return;

		var mapCamera = MapBuilder.Shared.mapCamera;

		if (GameInput.shared._gameActionMap.enabled)
		{
			if (Settings.mapToggle.Down())
			{
				resizer.Toggle();
				return;
			}
			else if (Settings.mapFollow.Down())
			{
				mapFollowMode = !mapFollowMode;
			}
			else if (Settings.mapRecenter.Down())
			{
				mapCameraTarget = mapFollowMode ? Camera.main : null;
				MapWindow.instance.mapDrag._isDragging = false;
				Vector3 currentCameraPosition = CameraSelector.shared.CurrentCameraPosition;
				currentCameraPosition.y = 5000f;
				mapCamera.transform.localPosition = currentCameraPosition;
			}
		}

		if (mapFollowMode == true && mapCameraTarget != null)
		{
			if (mapCameraTarget is Car car)
			{
				Vector3 position = WorldTransformer.GameToWorld(Vector3.Lerp(car.LastBodyPosition[0], car.LastBodyPosition[1], 0.5f));

				position.y = mapCamera.transform.position.y;
				mapCamera.transform.position = position;
			}
			else if (mapCameraTarget is Camera cam)
			{
				Vector3 position = Camera.main.transform.position;
				position.y = mapCamera.transform.position.y;
				mapCamera.transform.position = position;
			}
		}

		var mapWindow = MapWindow.instance;
		var mapDrag = MapWindow.instance.mapDrag;
		if (mapDrag._isDragging) mapCameraTarget = null;

		if (!mapDrag._pointerOver || !GameInput.IsMouseOverGameWindow(mapWindow._window)) return;

		if (GameInput.shared.PlaceFlare)
		{
			Vector2 viewportNormalizedPoint = mapDrag.NormalizedMousePosition();
			Ray ray = mapWindow.RayForViewportNormalizedPoint(viewportNormalizedPoint);
			Vector3 vector = MapManager.Instance.FindTerrainPointForXZ(WorldTransformer.WorldToGame(ray.origin));
			Location? location = LocationFromGamePoint(vector, 50f);
			if (location != null)
			{
				StateManager.ApplyLocal(new FlareAddUpdate(Graph.CreateSnapshotTrackLocation(location.Value)));
			}
		}
	}

	public Location? LocationFromGamePoint(Vector3 gamePosition, float radius)
	{
		List<Location> locations = new List<Location>();
		foreach (TrackSegment trackSegment in Graph.Shared.segments.Values)
		{
			Location loc;
			bool result = Graph.Shared.TryGetLocationFromPoint(trackSegment, gamePosition, radius, out loc);
			if (result && loc.IsValid)
			{
				locations.Add((Location)loc);
			}
		}

		if (locations.Count > 0)
			return locations.OrderBy(a => Vector3.Magnitude(a.GetPosition() - gamePosition)).First();

		return null;
	}

	private class Entry
	{
		public Entry(TrackObjectManager.SwitchDescriptor switchDescriptor, GameObject junctionMarker)
		{
			SwitchDescriptor = switchDescriptor;
			JunctionMarker = junctionMarker;
		}

		public readonly TrackObjectManager.SwitchDescriptor SwitchDescriptor;
		public readonly GameObject JunctionMarker;
	}

	[HarmonyPatch(typeof(TrackObjectManager), nameof(TrackObjectManager.Rebuild))]
	private static class TrackObjectManagerRebuildPatch
	{
		private static void Postfix()
		{
			if (MapState != MapStates.MAPLOADED) return;

			Instance?.Rebuild();

			if (MapWindow.instance._window.IsShown) MapWindow.instance.mapBuilder.Rebuild();
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorMainline), MethodType.Getter)]
	private static class TrackColorMainlinePatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Instance?.Settings.TrackColorMainline ?? Loader.MapEnhancerSettings.TrackColorMainlineOrig;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorBranch), MethodType.Getter)]
	private static class TrackColorBranchPatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Instance?.Settings.TrackColorBranch ?? Loader.MapEnhancerSettings.TrackColorBranchOrig;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorIndustrial), MethodType.Getter)]
	private static class TrackColorIndustrialPatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Instance?.Settings.TrackColorIndustrial ?? Loader.MapEnhancerSettings.TrackColorIndustrialOrig;

			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.TrackColorUnavailable), MethodType.Getter)]
	private static class TrackColorUnavailablePatch
	{
		private static bool Prefix(ref Color __result)
		{
			__result = Instance?.Settings.TrackColorUnavailable ?? Loader.MapEnhancerSettings.TrackColorUnavailableOrig;

			return false;
		}
	}

	[HarmonyPatch(typeof(TrackSegment), nameof(TrackSegment.Awake))]
	private static class SegmentTrackClassPatch
	{
		private static void Postfix(TrackSegment __instance)
		{
			// Skip track class modification if using visual-only mode
			if (Instance?.Settings.UseVisualOnlyTrackColors ?? false)
				return;
				
			// Only classify tracks after the map is fully loaded to avoid race conditions with other mods
			if (!_isMapFullyLoaded)
				return;
			
			// Don't override industrial tracks that may have been set by other systems
			if (__instance.trackClass == TrackClass.Industrial)
				return;
				
			if (mainlineSegments.Contains(__instance.id))
				__instance.trackClass = TrackClass.Mainline;
			else
				__instance.trackClass = TrackClass.Branch;
		}
	}

	/*
	[HarmonyPatch(typeof(PassengerStop), nameof(PassengerStop.OnEnable))]
	private static class PaxTrackClassPatch
	{
		private static void Postfix(PassengerStop __instance)
		{
			foreach (var tspan in __instance.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					seg.trackClass = Track.TrackClass.Industrial;
				}
			}
		}
	}
	*/

	[HarmonyPatch(typeof(IndustryComponent), nameof(IndustryComponent.Start))]
	private static class IndustryTrackClassPatch
	{
		private static void Postfix(IndustryComponent __instance)
		{
			if (__instance is ProgressionIndustryComponent) return;
			
			foreach (var tspan in __instance.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					// Always track industrial segments for visual-only mode
					_industrialSegments.Add(seg.id);
					
					// Only modify track class if not using visual-only mode
					if (Instance == null || !Instance.Settings.UseVisualOnlyTrackColors)
					{
						seg.trackClass = Track.TrackClass.Industrial;
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(PassengerStop), nameof(PassengerStop.OnEnable))]
	private static class PassengerStopPatch
	{
		private static void Postfix(PassengerStop __instance)
		{
			// Always track passenger stops (even if tracking feature is disabled)
			// This allows them to override industrial color
			Loader.LogDebug($"PassengerStop: {__instance.DisplayName}");
			foreach (var tspan in __instance.TrackSpans)
			{
				tspan.UpdateCachedPointsIfNeeded();
				foreach (var seg in tspan._cachedSegments)
				{
					_passengerStopSegments.Add(seg.id);
					Loader.LogDebug($"Found PassengerStop segment: {seg.id}");
				}
			}
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.ColorForSegment))]
	private static class ColorForSegmentPatch
	{
		private static void Postfix(ref TrackSegment segment, ref Color __result)
		{
			// Only apply if visual-only mode is enabled
			if (Instance == null || !Instance.Settings.UseVisualOnlyTrackColors) return;

			if (!segment.Available)
			{
				__result = Instance.Settings.TrackColorUnavailable;
				return;
			}

			// Use HashSet lookups for visual-only mode (don't rely on track class property)
			// Default to branch color
			__result = Instance.Settings.TrackColorBranch;
			
			// Check mainline (CTC blocks define mainline)
			if (_mainlineSegments.Contains(segment.id))
			{
				__result = Instance.Settings.TrackColorMainline;
			}
			
			// Check industrial (industry tracks override mainline/branch)
			// BUT: Never show industrial if this is a passenger stop (even if tracking disabled)
			if (_industrialSegments.Contains(segment.id) 
				&& !_mainlineSegments.Contains(segment.id)
				&& !_passengerStopSegments.Contains(segment.id))
			{
				__result = Instance.Settings.TrackColorIndustrial;
			}
			
			// Check passenger stops (shows purple ONLY if tracking enabled)
			if (Instance.Settings.EnablePassengerStopTracking && _passengerStopSegments.Contains(segment.id))
			{
				__result = Instance.Settings.TrackColorPax;
			}
		}
	}

	[HarmonyPatch(typeof(MapBuilder), "Add")]
	private static class MapBuilderAddPatch
	{
		private static void Postfix(MapIcon icon)
		{
			// Check if this is a signal's map icon
			var signal = icon.transform.parent?.GetComponent<CTCSignal>();
			if (signal == null) return;

			// Find the icon's image
			var image = icon.GetComponentInChildren<Image>(true);
			if (image == null) return;

			// Start monitoring this signal icon
			if (!icon.gameObject.GetComponent<SignalIconColorizer>())
			{
				var colorizer = icon.gameObject.AddComponent<SignalIconColorizer>();
				colorizer.Setup(signal, image);
			}
		}
	}

	private class SignalIconColorizer : MonoBehaviour
	{
		private CTCSignal signal;
		private Image icon;
		private SignalAspect lastAspect;

		public void Setup(CTCSignal ctcSignal, Image iconImage)
		{
			signal = ctcSignal;
			icon = iconImage;
			lastAspect = signal.CurrentAspect;
			UpdateColor();
		}

		void Update()
		{
			if (signal != null && signal.CurrentAspect != lastAspect)
			{
				lastAspect = signal.CurrentAspect;
				UpdateColor();
			}
		}

		private void UpdateColor()
		{
			Color signalColor = lastAspect switch
			{
				SignalAspect.Stop => Color.red,
				SignalAspect.Approach => Color.yellow,
				SignalAspect.Clear => Color.green,
				SignalAspect.DivergingApproach => Color.yellow,
				SignalAspect.DivergingClear => Color.green,
				SignalAspect.Restricting => new Color(1f, 0.5f, 0f), // Orange
				_ => Color.white
			};

			signalColor.a = 0.8f;
			icon.color = signalColor;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.UpdateForZoom))]
	private static class MapBuilderZoomPatch
	{
		private static void Postfix(MapBuilder __instance)
		{
			Instance?.JunctionsBranch?.SetActive(__instance.NormalizedScale <= Loader.Settings.MarkerCutoff);
		}
	}

	[HarmonyPatch(typeof(Car), nameof(Car.FinishSetup))]
	private static class CarFinishSetupPatch
	{
		private static void Postfix(Car __instance)
		{
			AddTraincarMarker(__instance);
		}
	}

	[HarmonyPatch(typeof(BaseLocomotive), nameof(Car.FinishSetup))]
	private static class BaseLocomotiveFinishSetupPatch
	{
		private static void Postfix(Car __instance)
		{
			var marker = __instance.GetComponentInChildren<MapIcon>();
			void OnClick()
			{
				if (Instance == null)
				{
					marker.OnClick = delegate { CarPickable.HandleShowInspector(__instance); };
					CarPickable.HandleShowInspector(__instance);
					return;
				}

				Instance.mapCameraTarget = __instance;

				if (GameInput.IsShiftDown) TrainController.Shared.SelectedCar = __instance;
				if (GameInput.IsControlDown) CameraSelector.shared.FollowCar(__instance);

				CarInspector.Show(__instance);
			}
			marker.OnClick = OnClick;
		}
	}

	[HarmonyPatch(typeof(Car), nameof(Car.UpdateMapIconPosition))]
	private static class CarUpdatePositionPatch
	{
		private static bool Prefix(Car __instance, Vector3 position, Quaternion rotation)
		{
			if (__instance.MapIcon == null)
			{
				return false;
			}
			if (__instance.Archetype.IsLocomotive())
				__instance.MapIcon.transform.SetPositionAndRotation(position + Vector3.up * 4000f, Quaternion.Euler(-90f, rotation.eulerAngles.y, 0f));
			else
				__instance.MapIcon.transform.SetPositionAndRotation(position + Vector3.up * 3000f, Quaternion.Euler(-90f, rotation.eulerAngles.y, 0f));
			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Zoom))]
	public static class ChangeMinMaxMapZoom
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codeMatcher = new CodeMatcher(instructions)
				.MatchStartForward(
				new CodeMatch(OpCodes.Ldc_R4, 100f))
				.SetAndAdvance(OpCodes.Ldsfld, AccessTools.Field(typeof(Loader), nameof(Loader.Settings)))
				.InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Loader.MapEnhancerSettings), nameof(Loader.MapEnhancerSettings.MapZoomMin))))
				.MatchStartForward(
				new CodeMatch(OpCodes.Ldc_R4, 10000f))
				.SetAndAdvance(OpCodes.Ldsfld, AccessTools.Field(typeof(Loader), nameof(Loader.Settings)))
				.InsertAndAdvance(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Loader.MapEnhancerSettings), nameof(Loader.MapEnhancerSettings.MapZoomMax))));
			return codeMatcher.InstructionEnumeration();
		}
	}
	
	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.NormalizedScale), MethodType.Getter)]
	private static class ChangeMinMaxMapZoom2
	{
		private static bool Prefix(Camera ___mapCamera, ref float __result)
		{
			__result = Mathf.InverseLerp(Instance?.Settings.MapZoomMin ?? 100f,
				Instance?.Settings.MapZoomMax ?? 5000f, ___mapCamera.orthographicSize);
			return false;
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.IconScale), MethodType.Getter)]
	private static class ChangeMinMaxMapZoom3
	{
		private static bool Prefix(MapBuilder __instance, ref float __result)
		{
			if (Instance == null) return true;

			var min = Mathf.LerpUnclamped(0.2f, 4f, InverseLerpUnclamped(100f, 5000f, Instance.Settings.MapZoomMin));
			var max = Mathf.LerpUnclamped(0.2f, 4f, InverseLerpUnclamped(100f, 5000f, Instance.Settings.MapZoomMax));
			__result = Mathf.Lerp(min, max, __instance.NormalizedScale); ;
			return false;
		}

		public static float InverseLerpUnclamped(float a, float b, float value)
		{
			if (a != b)
			{
				return (value - a) / (b - a);
			}

			return 0f;
		}
	}

	[HarmonyPatch(typeof(MapLabel), nameof(MapLabel.SetZoom))]
	private static class ChangeMinMaxMapZoom4
	{
		private static bool Prefix(ref float s)
		{
			s = s / 4f * 3f;
			return true;
		}
	}

	[HarmonyPatch]
	public static class PreventRebuildFromMovingCamera
	{
		/*
		[HarmonyTranspiler]
		[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.Rebuild))]
		static IEnumerable<CodeInstruction> RebuildTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codeMatcher = new CodeMatcher(instructions)
				.MatchStartForward(
				new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(CameraSelector), "shared")),
				new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(CameraSelector), "CurrentCameraPosition")),
				new CodeMatch(OpCodes.Stloc_1))
				.ThrowIfNotMatch("Could not find CameraSelector.Shared.get_CurrentCameraPosition()")
				.RemoveInstructionsWithOffsets (0, 12);
			return codeMatcher.InstructionEnumeration();
		}
		*/
		[HarmonyTranspiler]
		[HarmonyPatch(typeof(MapWindow), nameof(MapWindow.OnWindowShown))]
		static IEnumerable<CodeInstruction> OnWindowShownTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codeMatcher = new CodeMatcher(instructions)
				.MatchEndForward(
				new CodeMatch(OpCodes.Ldarg_0),
				new CodeMatch(OpCodes.Ldfld),
				new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(MapBuilder), "Rebuild")))
				.ThrowIfNotMatch("Could not find MapWindow.OnWindowShown()")
				.InsertAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PreventRebuildFromMovingCamera), nameof(PreventRebuildFromMovingCamera.RecenterMap))));
			return codeMatcher.InstructionEnumeration();
		}

		public static void RecenterMap()
		{
			Vector3 currentCameraPosition = CameraSelector.shared.CurrentCameraPosition;
			MapBuilder.Shared.mapCamera.transform.localPosition = new Vector3(currentCameraPosition.x, 5000f, currentCameraPosition.z);
		}
	}

	[HarmonyPatch(typeof(MapBuilder), nameof(MapBuilder.UpdateCullingSpheres))]
	public static class IncreaseBoundingSphereForSplinesPatch
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codeMatcher = new CodeMatcher(instructions)
				.MatchStartForward(
				new CodeMatch(OpCodes.Ldloc_3),
				new CodeMatch(OpCodes.Ldc_R4, 1f))
				//new CodeMatch(OpCodes.Newobj))//, ((Func<GameObject, Transform, GameObject>)UnityEngine.Object.Instantiate<GameObject>).Method.GetGenericMethodDefinition()))
				.ThrowIfNotMatch("Could not find new BoundingSphere")
				.Advance(1)
				.Set(OpCodes.Ldc_R4, 100f);
			return codeMatcher.InstructionEnumeration();
		}
	}
	
	[HarmonyPatch(typeof(FlarePickable), nameof(FlarePickable.Configure))]
	private static class FlareAddUpdatePatch
	{
		private static void Postfix(FlarePickable __instance)
		{
			AddFlareMarker(__instance);
		}
	}

	[HarmonyPatch(typeof(FlareManager), nameof(FlareManager.PlaceFlare))]
	public static class PlaceFlareProtectionPatch
	{
		private static bool Prefix(Camera theCamera)
		{
			if (!GameInput.IsMouseOverGameWindow() || theCamera == null)
			{
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(MapIcon), nameof(MapIcon.FixTextRotation))]
	public static class FixTextRotationPatch
	{
		private static bool Prefix(TMP_Text ____text)
		{
			if (____text != null)
			{
				float num = Mathf.DeltaAngle(____text.transform.rotation.eulerAngles.y, 180f);
				if (-90f < num && num < 90f)
				{
					____text.transform.localRotation = Quaternion.Euler(____text.transform.localRotation.eulerAngles + new Vector3(0f, 0f, 180f));
				}
			}
			return false;
		}
	}
}

public static class Extensions
{
	public static RectTransform AddDropdown(this UIPanelBuilder builder, List<TMP_Dropdown.OptionData> options, int currentSelectedIndex, Action<int> onSelect)
	{
		TMP_Dropdown tmp_Dropdown = builder.InstantiateInContainer<TMP_Dropdown>(builder._assets.dropdownControl);
		tmp_Dropdown.ClearOptions();
		tmp_Dropdown.AddOptions(options);
		tmp_Dropdown.value = currentSelectedIndex;
		tmp_Dropdown.onValueChanged.AddListener(delegate (int index)
		{
			onSelect(index);
		});

		tmp_Dropdown.template.sizeDelta = new Vector2(0f, options.Count * 20f + 8f);
		var go = new GameObject("Item Image");
		go.transform.SetParent(tmp_Dropdown.template.transform.Find("Viewport/Content/Item"), false);
		var image = go.AddComponent<Image>();
		var rectTransform = image.rectTransform;
		rectTransform.anchorMin = new Vector2(1, 0);
		rectTransform.anchorMax = new Vector2(1, 1);
		rectTransform.pivot = new Vector2(1, 0.5f);
		rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 15);
		rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 15);
		tmp_Dropdown.itemImage = image;

		UnityEngine.Object.DestroyImmediate(tmp_Dropdown.template.transform.Find("Viewport/Content/Item/Item Checkmark").gameObject);
		tmp_Dropdown.template.transform.Find("Viewport/Content/Item/Item Label").GetComponent<RectTransform>().offsetMin = new Vector2(10f, 1f);

		return tmp_Dropdown.GetComponent<RectTransform>();
	}
}
