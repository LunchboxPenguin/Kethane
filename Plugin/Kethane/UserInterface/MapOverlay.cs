﻿using GeodesicGrid;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kethane.UserInterface
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class MapOverlay : MonoBehaviour
    {
        public static MapOverlay Instance { get; private set; }
        public static string SelectedResource { get; set; }
        public static bool ShowOverlay { get; set; }

        private CelestialBody body;
        private Dictionary<CelestialBody, double> bodyRadii = new Dictionary<CelestialBody, double>();
        private GUISkin skin;
        private Cell? hoverCell;
        private ResourceDefinition resource;
        private Func<Cell, float> heightAt;
        private BoundsMap bounds;
        private OverlayRenderer overlayRenderer;

        private static RenderingManager renderingManager;
        private static GUIStyle centeredStyle = null;
        private static GUIStyle minMaxStyle = null;
        private static GUISkin defaultSkin = null;
        private static Rect controlWindowPos = new Rect(0, 0, 160, 0);
        private static bool revealAll = false;
        private static ApplicationLauncherButton button;

        private static readonly Color32 colorEmpty = Misc.Parse(SettingsManager.GetValue("ColorEmpty"), new Color32(128, 128, 128, 192));
        private static readonly Color32 colorUnknown = Misc.Parse(SettingsManager.GetValue("ColorUnknown"), new Color32(0, 0, 0, 128));
        private static readonly bool debugEnabled = Misc.Parse(SettingsManager.GetValue("Debug"), false);

        static MapOverlay()
        {
            controlWindowPos.x = Misc.Parse(SettingsManager.GetValue("WindowLeft"), 200f);
            controlWindowPos.y = Misc.Parse(SettingsManager.GetValue("WindowTop"), 200f);
            ShowOverlay = Misc.Parse(SettingsManager.GetValue("ShowOverlay"), true);
            SelectedResource = "Kethane";
        }

        public static Cell GetCellUnder(CelestialBody body, Vector3 worldPosition)
        {
            return Cell.Containing(body.transform.InverseTransformPoint(worldPosition), KethaneData.GridLevel);
        }

        public void Awake()
        {
            var scene = HighLogic.LoadedScene;
            if (scene != GameScenes.FLIGHT && scene != GameScenes.TRACKSTATION)
            {
                enabled = false;
            }
        }

        public void Start()
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
            }

            Instance = this;

            overlayRenderer = gameObject.AddComponent<OverlayRenderer>();
            overlayRenderer.SetGridLevel(KethaneData.GridLevel);

            if (button == null)
            {
                var tex = GameDatabase.Instance.GetTexture("Kethane/toolbar", false);
                button = ApplicationLauncher.Instance.AddModApplication(null, null, null, null, null, null, ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION, tex);
            }

            var node = ConfigNode.Load(KSPUtil.ApplicationRootPath + "GameData/Kethane/Grid.cfg");
            if (node == null) { return; }
            foreach (var body in FlightGlobals.Bodies)
            {
                double result;
                if (double.TryParse(node.GetValue(body.name), out result))
                {
                    bodyRadii[body] = result;
                }
            }
        }

        public void OnDestroy()
        {
            SettingsManager.SetValue("ShowOverlay", ShowOverlay);
            SettingsManager.SetValue("WindowLeft", MapOverlay.controlWindowPos.x);
            SettingsManager.SetValue("WindowTop", MapOverlay.controlWindowPos.y);
            SettingsManager.Save();
        }

        public void Update()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.TRACKSTATION)
            {
                Destroy(gameObject);
                return;
            }

            if (!MapView.MapIsEnabled || !ShowOverlay || MapView.MapCamera == null || KethaneData.Current == null)
            {
                overlayRenderer.IsVisible = false;
                return;
            }

            overlayRenderer.IsVisible = true;

            var target = MapView.MapCamera.target;

            var newBody = getTargetBody(target);
            var bodyChanged = (newBody != null) && (newBody != body);
            if (bodyChanged)
            {
                body = newBody;

                heightAt = getHeightRatioMap();
                bounds = new BoundsMap(heightAt, KethaneData.GridLevel);

                overlayRenderer.SetHeightMap(heightAt);

                var radius = bodyRadii.ContainsKey(body) ? bodyRadii[body] : 1.025;
                var parent = ScaledSpace.Instance.scaledSpaceTransforms.FirstOrDefault(t => t.name == body.name);
                overlayRenderer.SetRadiusMultiplier((float)radius);
                overlayRenderer.SetTarget(parent);
            }

            if (bodyChanged || resource == null || resource.Resource != SelectedResource)
            {
                resource = KethaneController.ResourceDefinitions.Where(r => r.Resource == SelectedResource).Single();
                refreshCellColors();
            }

            var ray = MapView.MapCamera.camera.ScreenPointToRay(Input.mousePosition);
            hoverCell = Cell.Raycast(ray, KethaneData.GridLevel, bounds, heightAt, gameObject.transform);
        }

        public void RefreshCellColor(Cell cell, CelestialBody body)
        {
            if (body != this.body) { return; }
            overlayRenderer.SetCellColor(cell, getCellColor(cell, body, KethaneData.Current));
        }

        private void refreshCellColors()
        {
            var data = KethaneData.Current;
            var colors = new CellMap<Color32>(KethaneData.GridLevel, c => getCellColor(c, body, data));
            overlayRenderer.SetCellColors(colors);
        }

        private Color32 getCellColor(Cell cell, CelestialBody body, KethaneData data)
        {
            var bodyResources = data[resource.Resource][body];
            var deposit = bodyResources.Resources.GetQuantity(cell);
            var scanned = bodyResources.IsCellScanned(cell);
            var color = (revealAll ? deposit != null : scanned) ? getDepositColor(resource, bodyResources, deposit) : colorUnknown;
            return color;
        }

        private static Color32 getDepositColor(ResourceDefinition definition, BodyResourceData bodyResources, double? deposit)
        {
            Color32 color;
            if (deposit != null)
            {
                var ratio = (float)(deposit.Value / bodyResources.Resources.MaxQuantity);
                color = (Color32)(definition.ColorFull * ratio + definition.ColorEmpty * (1 - ratio));
            }
            else
            {
                color = colorEmpty;
            }
            return color;
        }

        public void OnGUI()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.TRACKSTATION) { return; }

            if (renderingManager == null)
            {
                renderingManager = (RenderingManager)GameObject.FindObjectOfType(typeof(RenderingManager));
            }

            if (renderingManager != null)
            {
                var obj = renderingManager.uiElementsToDisable.FirstOrDefault();
                if (obj != null && !obj.activeSelf) { return; }
            }

            if (!MapView.MapIsEnabled || MapView.MapCamera == null) { return; }

            if (skin == null)
            {
                GUI.skin = null;
                skin = (GUISkin)GameObject.Instantiate(GUI.skin);

                var window = skin.window;
                window.padding = new RectOffset(6, 6, 20, 6);
                window.fontSize = 10;
                skin.window = window;

                var label = skin.label;
                label.margin = new RectOffset(1, 1, 1, 1);
                label.padding = new RectOffset(1, 1, 1, 1);
                label.fontSize = 10;
                skin.label = label;
            }

            GUI.skin = skin;

            if (hoverCell != null && ShowOverlay)
            {
                var mouse = Event.current.mousePosition;
                var position = new Rect(mouse.x + 16, mouse.y + 4, 160, 32);
                GUILayout.Window(12359, position, mouseWindow, "Resource Info");
            }

            if (defaultSkin == null)
            {
                GUI.skin = null;
                defaultSkin = (GUISkin)GameObject.Instantiate(GUI.skin);
            }

            if (centeredStyle == null)
            {
                centeredStyle = new GUIStyle(GUI.skin.GetStyle("Label"));
                centeredStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (minMaxStyle == null)
            {
                minMaxStyle = new GUIStyle(GUI.skin.button);
                minMaxStyle.contentOffset = new Vector2(-1, 0);
            }

            if (button == null || button.State != RUIToggleButton.ButtonState.TRUE) { return; }

            GUI.skin = defaultSkin;
            var oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = XKCDColors.Green;

            controlWindowPos = GUILayout.Window(12358, controlWindowPos, controlWindow, "Kethane Scan Map");

            GUI.backgroundColor = oldBackground;
        }

        private void mouseWindow(int windowId)
        {
            var cell = hoverCell.Value;
            var pos = cell.Position;
            var lat = (float)(Math.Atan2(pos.y, Math.Sqrt(pos.x * pos.x + pos.z * pos.z)) * 180 / Math.PI);
            var lon = (float)(Math.Atan2(pos.z, pos.x) * 180 / Math.PI);

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Center:");
            GUILayout.FlexibleSpace();
            GUILayout.Label(String.Format("{0:F1} {1}, {2:F1} {3}", Math.Abs(lat), lat < 0 ? "S" : "N", Math.Abs(lon), lon < 0 ? "W" : "E"));
            GUILayout.EndHorizontal();

            foreach (var definition in KethaneController.ResourceDefinitions)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label(String.Format("{0}:", definition.Resource));
                GUILayout.FlexibleSpace();
                if (revealAll || KethaneData.Current[definition.Resource][body].IsCellScanned(cell))
                {
                    var deposit = KethaneData.Current[definition.Resource][body].Resources.GetQuantity(cell);
                    GUILayout.Label(deposit != null ? String.Format("{0:N1}", deposit.Value) : "(none)");
                }
                else
                {
                    GUILayout.Label("(no data)");
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void controlWindow(int windowID)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();

            var defs = KethaneController.ResourceDefinitions.Select(d => d.Resource).ToList();

            GUI.enabled = defs.Count > 1;
            if (GUILayout.Button("◀", GUILayout.ExpandWidth(false)))
            {
                SelectedResource = defs.LastOrDefault(s => s.CompareTo(SelectedResource) < 0) ?? defs.Last();
            }
            GUI.enabled = true;

            GUILayout.Label(SelectedResource, centeredStyle, GUILayout.ExpandWidth(true));

            GUI.enabled = defs.Count > 1;
            if (GUILayout.Button("▶", GUILayout.ExpandWidth(false)))
            {
                SelectedResource = defs.FirstOrDefault(s => s.CompareTo(SelectedResource) > 0) ?? defs.First();
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            ShowOverlay = GUILayout.Toggle(ShowOverlay, "Show Grid Overlay");

            if (debugEnabled)
            {
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel != null && vessel.mainBody != body) { vessel = null; }

                GUILayout.BeginVertical(GUI.skin.box);

                if (revealAll != GUILayout.Toggle(revealAll, "Reveal Unscanned Cells"))
                {
                    revealAll = !revealAll;
                    refreshCellColors();
                }

                if (GUILayout.Button("Reset " + (body ? body.name : "[null]") + " Data"))
                {
                    KethaneData.Current[resource.Resource].ResetBodyData(body);
                    refreshCellColors();
                }

                if (GUILayout.Button("Reset Generator Config"))
                {
                    KethaneData.Current.ResetGeneratorConfig(resource);
                    refreshCellColors();
                }

                if (GUILayout.Button("Export Data (" + (revealAll ? "All" : "Scanned") + ")"))
                {
                    export();
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, controlWindowPos.width, controlWindowPos.height));
        }

        private static void export()
        {
            var sb = new StringBuilder();

            var cells = new CellMap<string>(KethaneData.GridLevel);
            foreach (var cell in Cell.AtLevel(KethaneData.GridLevel))
            {
                var pos = cell.Position;
                var lat = (float)(Math.Atan2(pos.y, Math.Sqrt(pos.x * pos.x + pos.z * pos.z)) * 180 / Math.PI);
                var lon = (float)(Math.Atan2(pos.z, pos.x) * 180 / Math.PI);
                cells[cell] = String.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},", cell.Index, lat, lon, pos.x, pos.y, pos.z);
            }

            sb.AppendLine("body,resource,cellId,lat,lon,x,y,z,scanned,quantity");

            foreach (var body in FlightGlobals.Bodies)
            {
                foreach (var resource in KethaneController.ResourceDefinitions)
                {
                    foreach (var cell in Cell.AtLevel(KethaneData.GridLevel))
                    {
                        var scanned = KethaneData.Current[resource.Resource][body].IsCellScanned(cell);
                        var deposit = KethaneData.Current[resource.Resource][body].Resources.GetQuantity(cell);

                        sb.Append(String.Format("{0},{1},", body.name, resource.Resource));
                        sb.Append(cells[cell]);
                        sb.Append(scanned ? "true" : "false");
                        if ((revealAll || scanned) && deposit != null)
                        {
                            sb.Append(String.Format(CultureInfo.InvariantCulture, ",{0}", deposit.Value));
                        }
                        else
                        {
                            sb.Append(",");
                        }
                        sb.AppendLine();
                    }
                }
            }

            KSP.IO.File.WriteAllText<KethaneController>(sb.ToString(), "kethane_export.csv");
        }

        private static CelestialBody getTargetBody(MapObject target)
        {
            if (target.type == MapObject.MapObjectType.CELESTIALBODY)
            {
                return target.celestialBody;
            }
            else if (target.type == MapObject.MapObjectType.MANEUVERNODE)
            {
                return target.maneuverNode.patch.referenceBody;
            }
            else if (target.type == MapObject.MapObjectType.VESSEL)
            {
                return target.vessel.mainBody;
            }

            return null;
        }

        private Func<Cell, float> getHeightRatioMap()
        {
            Func<Cell, float> heightRatioAt;

            try
            {
                var bodyTerrain = TerrainData.ForBody(body);
                heightRatioAt = c => Math.Max(1, bodyTerrain.GetHeightRatio(c));
            }
            catch (ArgumentException)
            {
                heightRatioAt = c => 1;
            }

            return heightRatioAt;
        }
    }
}
