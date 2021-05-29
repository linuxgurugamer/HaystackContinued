using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.Localization;

using ClickThroughFix;

namespace HaystackReContinued
{
    /// <summary>
    /// Class to house vessel types along with icons and sort order for the plugin
    /// </summary>
    public class HSVesselType
    {
        public string name; // Type name defined by KSP devs
        public byte sort; // Sort order, lowest first

        // Icon texture, loaded from icons directory. File must be named 'button_vessel_TYPE.png'
        public Texture2D icon;

        public bool visible; // Is this type shown in list

        public HSVesselType(string name, byte sort, Texture2D icon, bool visible)
        {
            this.name = name;
            this.sort = sort;
            this.icon = icon;
            this.visible = visible;
        }
    };

    public abstract class HaystackContinued : MonoBehaviour
    {
        public static HaystackContinued fetch = null;
        // window vars
        private int windowId;
        internal bool WinVisible = false;
        internal Rect winRect;
        private bool isGUISetup;


        // controllers and managers
        private VesselListController vesselListController;

        //controls
        private ResizeHandle resizeHandle;
        private DefaultScrollerView defaultScrollerView;
        private GroupedScrollerView groupedScrollerView;
        private BottomButtons bottomButtons;
        private ExpandedVesselInfo expandedVesselInfo;
        private CloseHandle closeHandle;

        public void Awake()
        {
            HSUtils.DebugLog("HaystackContinued#Awake");
            fetch = this;
            this.bottomButtons = new BottomButtons();
            this.bottomButtons.LoadSettings();

            this.vesselListController = new VesselListController(this, this.bottomButtons);
            this.defaultScrollerView = new DefaultScrollerView(this, this.vesselListController);
            this.groupedScrollerView = new GroupedScrollerView(this, this.vesselListController);
            this.expandedVesselInfo = new ExpandedVesselInfo(this, this.bottomButtons, this.defaultScrollerView,
                this.groupedScrollerView);
            this.resizeHandle = new ResizeHandle();
            this.closeHandle = new CloseHandle();

            windowId = Resources.rnd.Next(1000, 2000000);
        }


        private void onDataLoadedHandler()
        {
            this.vesselListController.RefreshFilteredList();
        }

        public void OnEnable()
        {
            HSUtils.DebugLog("HaystackContinued#OnEnable");

            GameEvents.onPlanetariumTargetChanged.Add(this.onMapTargetChange);

            this.WinRect = HaystackResourceLoader.Instance.Settings.WindowPositions[this.SettingsName];
            WinVisible = HaystackResourceLoader.Instance.Settings.WindowVisibilities[this.SettingsName];
            this.bottomButtons.LoadSettings();

            HaystackResourceLoader.Instance.DisplayButtonOnClick += this.displayButtonClicked;

            InvokeRepeating("IRFetchVesselList", 5.0F, 5.0F);
            InvokeRepeating("RefreshDataSaveSettings", 0, 30.0F);

            //HaystackResourceLoader.Instance.FixApplicationLauncherButtonDisplay(this.WinVisible);
        }

        public void OnDisable()
        {
            HSUtils.DebugLog("HaystackContinued#OnDisable");
            CancelInvoke();

            GameEvents.onPlanetariumTargetChanged.Remove(this.onMapTargetChange);

            HaystackResourceLoader.Instance.DisplayButtonOnClick -= this.displayButtonClicked;

            HaystackResourceLoader.Instance.Settings.WindowPositions[this.SettingsName] = this.WinRect;
            HaystackResourceLoader.Instance.Settings.WindowVisibilities[this.SettingsName] = this.WinVisible;
            this.bottomButtons.SaveSettings();

            HaystackResourceLoader.Instance.Settings.Save();
        }

        private void displayButtonClicked(EventArgs e)
        {
            this.WinVisible = !this.WinVisible;
        }

        /*  Many mods assume that KSP will be operating with working directory set to its root directory,
            containing KSP.x86_64 and GameData.  Doesn't hurt to set it though.

            Furthermore, if this is done early enough in the executing KSP instance, it will apply to all
            code (e.g. mods) executing thereafter.

            Finally, if this change, applied here in HaystackContinued, is moved into a mod that is a)
            almost ubiquitous and b) very likely to run early (e.g. ModuleManager?), it will have beneficial
            effects on a far wider scale.  (KerbalKonstructs is another mod that is known to fail based on
            this faulty assumption.)
        */       
        public void setCurrentWorkingDirectory()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(exeDir);
        }

        public void Start()
        {
            setCurrentWorkingDirectory();
            // not an anonymous functions because we need to remove them in #OnDestroy
            GameEvents.onHideUI.Add(onHideUI);
            GameEvents.onShowUI.Add(onShowUI);
            GameEvents.onVesselChange.Add(onVesselChange);
            GameEvents.onVesselWasModified.Add(onVesselWasModified);
            GameEvents.onVesselRename.Add(onVesselRenamed);

            this.vesselListController.FetchVesselList();
            DataManager.Instance.OnDataLoaded += this.onDataLoadedHandler;
        }

        private void onVesselRenamed(GameEvents.HostedFromToAction<Vessel, string> data)
        {
            this.vesselListController.FetchVesselList();
        }

        private void onVesselWasModified(Vessel data)
        {
            this.vesselListController.FetchVesselList();
        }

        private void onVesselChange(Vessel data)
        {
            this.vesselListController.FetchVesselList();
        }

        //called when the game tells us that the UI is going to be shown again
        private void onShowUI()
        {
            this.UIHide = false;
        }

        //called when the game tells us that the UI is to be hidden; used for screenshots generally
        private void onHideUI()
        {
            this.UIHide = true;
        }

        protected bool UIHide { get; set; }

        public void OnDestory()
        {
            HSUtils.DebugLog("HaystackContinued#OnDestroy");

            GameEvents.onHideUI.Remove(this.onHideUI);
            GameEvents.onShowUI.Remove(this.onShowUI);
            GameEvents.onVesselChange.Remove(this.onVesselChange);
            GameEvents.onVesselWasModified.Remove(this.onVesselWasModified);
            GameEvents.onVesselRename.Remove(this.onVesselRenamed);

            DataManager.Instance.OnDataLoaded -= this.onDataLoadedHandler;
            this.expandedVesselInfo.Dispose();
        }

        private void onMapTargetChange(MapObject mapObject)
        {
            if (!HSUtils.IsTrackingCenterActive)
            {
                return;
            }

            if (mapObject == null)
            {
                return;
            }

            switch (mapObject.type)
            {
                case MapObject.ObjectType.Vessel:
                    this.defaultScrollerView.SelectedVessel = mapObject.vessel;
                    this.groupedScrollerView.SelectedVessel = mapObject.vessel;
                    break;
                case MapObject.ObjectType.CelestialBody:
                    this.defaultScrollerView.SelectedBody = mapObject.celestialBody;
                    break;
                default:
                    this.defaultScrollerView.SelectedBody = null;
                    this.defaultScrollerView.SelectedVessel = null;
                    break;
            }
        }


        public void IRFetchVesselList()
        {
            if (!this.IsGuiDisplay) return;

            this.vesselListController.RefreshFilteredList();
        }


        /// <summary>
        /// Function called every 30 seconds
        /// </summary>
        public void RefreshDataSaveSettings()
        {
            if (!this.IsGuiDisplay) return;

            HaystackResourceLoader.Instance.Settings.WindowPositions[this.SettingsName] = this.WinRect;
            this.bottomButtons.SaveSettings();
        }

        static bool altSkin = false;
        /// <summary>
        /// Repaint GUI
        /// </summary>
        public void OnGUI()
        {
            if (this.IsGuiDisplay)
            {
                if (!this.isGUISetup)
                {
                    altSkin = HighLogic.CurrentGame.Parameters.CustomParams<HS>().useAltSkin;


                    Resources.LoadStyles();

                    //TODO: eliminate
                    this.groupedScrollerView.GUISetup(this.bottomButtons);
                    this.defaultScrollerView.GUISetup(this.bottomButtons);
                    this.bottomButtons.GUISetup(this.groupedScrollerView, this.defaultScrollerView);

                    this.bottomButtons.OnSwitchVessel += vessel => this.StartCoroutine(SwitchToVessel(vessel));

                    this.vesselListController.FetchVesselList();

                    this.isGUISetup = true;
                }
                if (altSkin != HighLogic.CurrentGame.Parameters.CustomParams<HS>().useAltSkin)
                {
                    altSkin = HighLogic.CurrentGame.Parameters.CustomParams<HS>().useAltSkin;
                    Resources.LoadStyles(true);
                }
                this.drawGUI();
            }
        }

        private static IEnumerator SwitchToVessel(Vessel vessel)
        {
            yield return new WaitForFixedUpdate();

            if (HSUtils.IsTrackingCenterActive)
            {
                HSUtils.TrackingSwitchToVessel(vessel);
            }
            else if (HSUtils.IsSpaceCenterActive)
            {
                HSUtils.SwitchAndFly(vessel);
            }
            else
            {
                FlightGlobals.SetActiveVessel(vessel);
            }
        }

        public Rect WinRect
        {
            get { return this.winRect; }
            set { this.winRect = value; }
        }

        private void drawGUI()
        {
            if (!HighLogic.CurrentGame.Parameters.CustomParams<HS>().useAltSkin)
                GUI.skin = HighLogic.Skin;
            if (expandedVesselInfo.popupDialog)
                return;
            this.winRect = this.winRect.ClampToScreen();


            this.winRect = ClickThruBlocker.GUILayoutWindow(windowId, this.winRect, this.mainWindowConstructor,
                string.Format("Haystack ReContinued {0}", Settings.version), Resources.winStyle, GUILayout.MinWidth(120),
                GUILayout.MinHeight(300));

            this.expandedVesselInfo.DrawExpandedWindow();

            // do this here since if it's done within the window you only recieve events that are inside of the window
            this.resizeHandle.DoResize(ref this.winRect);
        }

        /// <summary>
        /// Checks if the GUI should be drawn in the current scene
        /// </summary>
        protected virtual bool IsGuiDisplay
        {
            get { return false; }
        }

        internal bool IsVisible()
        {
            return IsGuiDisplay;
        }
        private bool isVesselHidden(Vessel vessel)
        {
            return this.HiddenVessels.Contains(vessel.id);
        }

        private void markVesselHidden(Vessel vessel, bool mark)
        {
            HSUtils.DebugLog("HaystackContinued#markVesselHidden: {0} {1}", vessel.name, mark);
            if (mark)
            {
                DataManager.Instance.HiddenVessels.AddVessel(vessel);
            }
            else
            {
                DataManager.Instance.HiddenVessels.RemoveVessel(vessel);
            }
        }

        protected abstract string SettingsName { get; }

        public HashSet<Guid> HiddenVessels
        {
            get { return DataManager.Instance.HiddenVessels.VesselList; }
        }

        private void mainWindowConstructor(int windowID)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();

            #region vessel types - horizontal

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Vessels
            var typeCounts = this.vesselListController.VesselTypeCounts;
            for (int i = 0; i < Resources.vesselTypesList.Count(); i++)
            {
                var typeString = Resources.vesselTypesList[i].name;

                if (typeCounts.ContainsKey(typeString))
                    typeString += string.Format(" ({0})", typeCounts[typeString]);

                var previous = Resources.vesselTypesList[i].visible;

                Resources.vesselTypesList[i].visible = GUILayout.Toggle(Resources.vesselTypesList[i].visible,
                    new GUIContent(Resources.vesselTypesList[i].icon, typeString), Resources.buttonVesselTypeStyle);

                if (previous != Resources.vesselTypesList[i].visible)
                {
                    this.vesselListController.RefreshFilteredList();
                }

                if (typeString.Equals(Resources.BODIES))
                {
                    defaultScrollerView.ShowCelestialBodies = Resources.vesselTypesList[i].visible;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            #endregion vessel types

            //Search area

            GUILayout.BeginHorizontal();

            GUILayout.Label("Search:", Resources.textSearchStyle);
            this.vesselListController.SearchTerm = GUILayout.TextField(this.vesselListController.SearchTerm, GUILayout.MinWidth(50.0F), GUILayout.ExpandWidth(true));

            // clear search text
            if (GUILayout.Button("x", Resources.buttonSearchClearStyle))
            {
                this.vesselListController.SearchTerm = "";
            }

            GUILayout.EndHorizontal();

            if (this.bottomButtons.GroupByOrbitingBody)
            {
                this.groupedScrollerView.Draw();
            }
            else
            {
                this.defaultScrollerView.Draw();
            }

            this.bottomButtons.Draw();

            // handle tooltips here so it is on top
            if (GUI.tooltip != "")
            {
                var mousePosition = Event.current.mousePosition;
                var content = new GUIContent(GUI.tooltip);
                var size = Resources.tooltipBoxStyle.CalcSize(content);
                GUI.Box(new Rect(mousePosition.x - 30, mousePosition.y - 30, size.x + 12, 25).ClampToPosIn(this.WinRect), content, Resources.tooltipBoxStyle);
            }

            GUILayout.EndVertical();

            this.expandedVesselInfo.DrawExpandButton();

            GUILayout.EndHorizontal();

            this.resizeHandle.Draw(ref this.winRect);
            this.closeHandle.Draw(ref this.winRect);

            // If user input detected, force refresh
            if (GUI.changed)
            {
                // might want to make this a bit more granular but it seems ok atm
                this.vesselListController.RefreshFilteredList();
            }

            GUI.DragWindow();
        }

        private class VesselListController
        {
            private HaystackContinued haystack;

            // number of vessles per type
            private Dictionary<string, int> vesselTypeCounts = new Dictionary<string, int>();

            private List<Vessel> vesselList = new List<Vessel>();
            private List<Vessel> filteredVesselList = new List<Vessel>();
            private List<CelestialBody> filteredBodyList = new List<CelestialBody>();

            private Comparers.CombinedComparer<Vessel> vesselComparer;

            // Search text
            private string searchTerm = "";

            private readonly Dictionary<CelestialBody, List<Vessel>> groupedBodyVessel =
                new Dictionary<CelestialBody, List<Vessel>>();

            private bool listIsAscending;
            private Vessel activeVessel;

            internal VesselListController(HaystackContinued haystack, BottomButtons bottomButtons)
            {
                this.haystack = haystack;
                bottomButtons.OnNearbyChanged += onNearbyChanged;
                bottomButtons.OnHiddenVesselsChanged += onHiddenVesselChanged;
                bottomButtons.OnSortOrderChanged += onSortOrderChanged;

                this.listIsAscending = bottomButtons.IsAscendingSortOrder;

                this.vesselComparer = Comparers.CombinedComparer<Vessel>.FromOne(new Comparers.VesselNameComparer());
            }

            private void onSortOrderChanged(BottomButtons view)
            {
                this.listIsAscending = view.IsAscendingSortOrder;

                this.RefreshFilteredList();
            }

            private void onHiddenVesselChanged(BottomButtons view)
            {
                this.RefreshFilteredList();
            }

            private void onNearbyChanged(BottomButtons view)
            {
                if (!HSUtils.IsInFlight)
                {
                    return;
                }

                this.updateActiveVessel();

                if (view.IsNearbyOnly)
                {
                    this.vesselComparer = this.vesselComparer.Add(new Comparers.VesselNearbyComparer(this.activeVessel));
                }
                else
                {
                    this.vesselComparer = this.vesselComparer.Remove<Comparers.VesselNearbyComparer>();
                }

                this.RefreshFilteredList();
            }

            public Dictionary<string, int> VesselTypeCounts
            {
                get { return this.vesselTypeCounts; }
            }

            public List<Vessel> DisplayVessels
            {
                get { return this.filteredVesselList; }
            }

            public Dictionary<CelestialBody, List<Vessel>> GroupedByBodyVessels
            {
                get { return this.groupedBodyVessel; }
            }

            public List<CelestialBody> DisplayBodyies
            {
                get { return this.filteredBodyList; }
            }

            public string SearchTerm
            {
                get { return this.searchTerm; }
                set
                {
                    var previous = this.searchTerm;
                    this.searchTerm = value;

                    if (string.IsNullOrEmpty(this.searchTerm))
                    {
                        this.vesselComparer = this.vesselComparer.Remove<Comparers.FilteredVesselComparer>();
                    }
                    else
                    {
                        this.vesselComparer =
                            this.vesselComparer.Add(new Comparers.FilteredVesselComparer(this.searchTerm));
                    }

                    if (previous != this.searchTerm)
                    {
                        this.RefreshFilteredList();
                    }
                }
            }

            /// <summary>
            /// Refresh list of vessels
            /// </summary>
            public void FetchVesselList()
            {
                this.vesselList = FlightGlobals.Vessels;

                if (this.vesselList == null)
                {
                    HSUtils.DebugLog("vessel list is null");
                    this.vesselList = new List<Vessel>();
                }

                this.updateActiveVessel();

                // count vessel types
                this.VesselTypeCounts.Clear();
                foreach (var vessel in vesselList)
                {
                    var typeString = vessel.vesselType.ToString();

                    if (this.VesselTypeCounts.ContainsKey(typeString))
                        this.VesselTypeCounts[typeString]++;
                    else
                        this.VesselTypeCounts.Add(typeString, 1);
                }

                this.performFilters();
            }

            private void updateActiveVessel()
            {
                if (!HSUtils.IsInFlight)
                {
                    this.activeVessel = null;
                    return;
                }

                var current = FlightGlobals.ActiveVessel;

                if (current != this.activeVessel)
                {
                    this.activeVessel = current;
                    this.updateNearbyComparer();
                }
            }

            private void updateNearbyComparer()
            {
                var comparer = this.vesselComparer.Comparers.FirstOrDefault(c => c is Comparers.VesselNearbyComparer);

                if (comparer == null)
                {
                    return;
                }

                this.vesselComparer = this.vesselComparer.Remove<Comparers.VesselNearbyComparer>();
                this.vesselComparer.Add(new Comparers.VesselNearbyComparer(this.activeVessel));
            }

            public void RefreshFilteredList()
            {
                this.updateActiveVessel();
                this.performFilters();
                this.performSort(this.filteredVesselList);
            }

            private void performFilters()
            {
                this.filteredVesselList = new List<Vessel>(this.vesselList);
                this.filteredBodyList = new List<CelestialBody>(Resources.CelestialBodies);

                if (this.vesselList != null)
                {
                    this.removeFilteredVesslesFromList(this.filteredVesselList);

                    //now hidden vessels
                    if (!this.haystack.bottomButtons.IsHiddenVesselsToggled)
                    {
                        this.removeHiddenVesselsFromList(this.filteredVesselList);
                    }

                    if (this.haystack.bottomButtons.IsNearbyOnly)
                    {
                        this.filterForNearbyOnly(this.filteredVesselList);
                    }

                    // And then filter by the search string
                    this.performSearchOnVesselList(this.filteredVesselList);

                    if (!string.IsNullOrEmpty(this.SearchTerm))
                    {
                        this.filteredBodyList.RemoveAll(
                            cb => -1 == cb.bodyName.IndexOf(this.SearchTerm, StringComparison.OrdinalIgnoreCase)
                            );
                    }
                }

                if (this.haystack.bottomButtons.GroupByOrbitingBody)
                {
                    this.GroupedByBodyVessels.Clear();

                    foreach (var vessel in this.filteredVesselList)
                    {
                        var body = vessel.orbit.referenceBody;

                        List<Vessel> list;
                        if (!this.GroupedByBodyVessels.TryGetValue(body, out list))
                        {
                            list = new List<Vessel>();
                            this.GroupedByBodyVessels.Add(body, list);
                        }

                        list.Add(vessel);
                    }

                    // sort groups
                    foreach (var kv in this.groupedBodyVessel)
                    {
                        this.performSort(kv.Value);
                    }
                }
            }

            private void removeFilteredVesslesFromList(List<Vessel> list)
            {
                if (Resources.vesselTypesList != null)
                {
                    var invisibleTypes = Resources.vesselTypesList.FindAll(type => type.visible == false).Select(type => type.name);

                    list.RemoveAll(vessel => invisibleTypes.Contains(vessel.vesselType.ToString()));
                }
            }

            private void removeHiddenVesselsFromList(List<Vessel> list)
            {
                list.RemoveAll(v => this.haystack.HiddenVessels.Contains(v.id));
            }

            private void filterForNearbyOnly(List<Vessel> list)
            {
                if (!HSUtils.IsInFlight)
                {
                    return;
                }
                var localBody = FlightGlobals.ActiveVessel.orbit.referenceBody;

                list.RemoveAll(v => v.orbit.referenceBody != localBody);
            }

            private void performSearchOnVesselList(List<Vessel> list)
            {
                if (string.IsNullOrEmpty(this.SearchTerm))
                {
                    return;
                }
                //string vname = v.vesselName;
                //Localizer.TryGetStringByTag(v.vesselName, out vname);

                list.RemoveAll(

                v => v == null || v.vesselName == null || -1 == v.vesselName.IndexOf(this.SearchTerm, StringComparison.OrdinalIgnoreCase)
                        );
            }

            private void performSort(List<Vessel> list)
            {
                list.Sort(this.vesselComparer);
                if (!this.listIsAscending)
                {
                    list.Reverse();
                }
            }
        }

        private class GroupedScrollerView
        {
            private Vector2 scrollPos = Vector2.zero;
            private Vessel selectedVessel;
            private CelestialBody selectedBody;
            private readonly VesselInfoView vesselInfoView;
            private VesselListController vesselListController;


            internal GroupedScrollerView(HaystackContinued haystackContinued, VesselListController vesselListController)
            {
                this.vesselInfoView = new VesselInfoView(haystackContinued);
                this.vesselListController = vesselListController;
            }

            public Vessel SelectedVessel
            {
                get { return this.selectedVessel; }
                set { this.selectedVessel = value; }
            }

            internal void Draw()
            {
                var displayVessels = this.vesselListController.DisplayVessels;
                if (displayVessels == null || displayVessels.IsEmpty())
                {
                    GUILayout.Label("No matched vessels found");

                    GUILayout.FlexibleSpace();
                    return;
                }

                this.scrollPos = GUILayout.BeginScrollView(scrollPos);

                Vessel preSelectedVessel = null;

                var clicked = false;

                GUILayout.BeginVertical();

                foreach (var kv in this.vesselListController.GroupedByBodyVessels)
                {
                    var body = kv.Key;
                    var vessels = kv.Value;

                    var selected = body == selectedBody;

                    selected = GUILayout.Toggle(selected, new GUIContent(body.name), Resources.buttonTextOnly);

                    if (selected)
                    {
                        this.selectedBody = body;
                    }
                    else
                    {
                        if (this.selectedBody == body)
                        {
                            this.selectedBody = null;
                        }
                        continue;
                    }

                    var activeVessel = HSUtils.IsInFlight ? FlightGlobals.ActiveVessel : null;
                    foreach (var vessel in vessels)
                    {
                        //this typically happens when debris is going out of physics range and is deleted by the game
                        if (vessel == null)
                        {
                            continue;
                        }

                        this.vesselInfoView.Draw(vessel, vessel == this.selectedVessel, activeVessel);

                        if (!this.vesselInfoView.Clicked)
                        {
                            continue;
                        }

                        preSelectedVessel = vessel;
                        clicked = true;
                    }
                }

                GUILayout.EndVertical();
                GUILayout.EndScrollView();

                var checkInScroll = GUILayoutUtility.GetLastRect();
                if (!clicked || !checkInScroll.Contains(Event.current.mousePosition))
                {
                    return;
                }

                if (preSelectedVessel != null && preSelectedVessel == this.selectedVessel)
                {
                    this.fireOnSelectedItemClicked(this);
                    return;
                }

                if (preSelectedVessel != null && preSelectedVessel != this.selectedVessel)
                {
                    this.selectedVessel = preSelectedVessel;
                    this.fireOnSelectionChanged(this);
                }

                this.vesselInfoView.Reset();
                this.changeCameraTarget();
            }

            private void changeCameraTarget()
            {
                if (this.selectedVessel == null)
                {
                    return;
                }

                if (HSUtils.IsTrackingCenterActive)
                {
                    HSUtils.RequestCameraFocus(this.selectedVessel);
                }
                else
                {
                    HSUtils.FocusMapObject(this.selectedVessel);
                }
            }

            internal void GUISetup(BottomButtons bottomButtons)
            {
                bottomButtons.OnGroupByChanged += view => this.reset();
            }

            private void reset()
            {
                this.scrollPos = Vector2.zero;
                this.selectedVessel = null;
                this.selectedBody = null;
                this.vesselInfoView.Reset();
            }

            internal delegate void OnSelectionChangedHandler(GroupedScrollerView view);
            internal event OnSelectionChangedHandler OnSelectionChanged;
            protected virtual void fireOnSelectionChanged(GroupedScrollerView view)
            {
                OnSelectionChangedHandler handler = this.OnSelectionChanged;
                if (handler != null) handler(view);
            }

            internal delegate void OnSelectedItemClickedHandler(GroupedScrollerView view);

            internal event OnSelectedItemClickedHandler OnSelectedItemClicked;

            protected virtual void fireOnSelectedItemClicked(GroupedScrollerView view)
            {
                var handler = this.OnSelectedItemClicked;
                if (handler != null) handler(view);
            }
        }

        private class DefaultScrollerView
        {
            private Vector2 scrollPos = Vector2.zero;
            private Vessel selectedVessel;
            private CelestialBody selectedBody;

            private readonly VesselInfoView vesselInfoView;
            private readonly VesselListController vesselListController;

            public bool ShowCelestialBodies { get; set; }

            internal DefaultScrollerView(HaystackContinued haystackContinued, VesselListController vesselListController)
            {
                this.vesselInfoView = new VesselInfoView(haystackContinued);
                this.vesselListController = vesselListController;
            }

            internal Vessel SelectedVessel
            {
                get { return this.selectedVessel; }
                set
                {
                    this.selectedVessel = value;
                    this.selectedBody = null;
                }
            }

            internal CelestialBody SelectedBody
            {
                get { return this.selectedBody; }
                set
                {
                    this.selectedBody = value;
                    this.selectedVessel = null;
                }
            }

            private void reset()
            {
                this.scrollPos = Vector2.zero;
                this.selectedVessel = null;
                this.selectedBody = null;
                this.vesselInfoView.Reset();
            }

            internal void Draw()
            {
                var displayVessels = this.vesselListController.DisplayVessels;
                if ((displayVessels == null || displayVessels.IsEmpty()) && this.ShowCelestialBodies != true)
                {
                    GUILayout.Label("No match found");
                    GUILayout.FlexibleSpace();
                    return;
                }

                var clicked = false;
                Vessel preSelectedVessel = null;
                CelestialBody preSelecedBody = null;

                this.scrollPos = GUILayout.BeginScrollView(this.scrollPos);

                GUILayout.BeginVertical();

                var activeVessel = HSUtils.IsInFlight ? FlightGlobals.ActiveVessel : null;

                foreach (var vessel in displayVessels)
                {
                    //this typically happens when debris is going out of physics range and is deleted by the game
                    if (vessel == null)
                    {
                        continue;
                    }

                    this.vesselInfoView.Draw(vessel, vessel == this.selectedVessel, activeVessel);

                    if (!this.vesselInfoView.Clicked)
                    {
                        continue;
                    }

                    preSelectedVessel = vessel;
                    clicked = true;
                }

                // celestial bodies
                if (this.ShowCelestialBodies)
                {
                    var displayBodies = this.vesselListController.DisplayBodyies;
                    foreach (var body in displayBodies)
                    {
                        GUILayout.BeginVertical(body == this.SelectedBody
                            ? Resources.vesselInfoSelected
                            : Resources.vesselInfoDefault);

                        GUILayout.Label(body.name, Resources.textListHeaderStyle);
                        GUILayout.EndVertical();

                        Rect check = GUILayoutUtility.GetLastRect();

                        if (Event.current != null && Event.current.type == EventType.MouseDown &&
                            Input.GetMouseButtonDown(0) && check.Contains(Event.current.mousePosition))
                        {
                            if (this.SelectedBody == body)
                            {
                                this.fireOnSelectedItemClicked(this);
                                continue;
                            }

                            preSelecedBody = body;
                            clicked = true;
                        }
                    }
                }

                GUILayout.EndVertical();
                GUILayout.EndScrollView();

                var checkInScrollClick = GUILayoutUtility.GetLastRect();

                //clicks to items in scroll view can happen outside of the scroll view
                if (!clicked || Event.current == null || !checkInScrollClick.Contains(Event.current.mousePosition))
                {
                    return;
                }

                if (preSelectedVessel != null && preSelectedVessel == this.SelectedVessel)
                {
                    this.fireOnSelectedItemClicked(this);
                    return;
                }

                if (preSelecedBody != null && preSelecedBody != this.SelectedBody)
                {
                    this.SelectedBody = preSelecedBody;
                    this.fireOnSelectionChanged(this);
                }
                else if (preSelectedVessel != null && preSelectedVessel != this.SelectedVessel)
                {
                    this.SelectedVessel = preSelectedVessel;
                    this.fireOnSelectionChanged(this);
                }


                this.vesselInfoView.Reset();
                this.changeCameraTarget();
            }

            private void changeCameraTarget()
            {
                // don't do anything if we are in the space center since there is no map view to change.
                if (HSUtils.IsSpaceCenterActive)
                {
                    return;
                }

                if (this.SelectedVessel != null)
                {

                    if (HSUtils.IsTrackingCenterActive)
                    {
                        HSUtils.RequestCameraFocus(this.SelectedVessel);
                    }
                    else
                    {
                        HSUtils.FocusMapObject(this.selectedVessel);
                    }
                }
                if (this.SelectedBody != null)
                {
                    HSUtils.FocusMapObject(this.SelectedBody);
                }
            }

            internal delegate void OnSelectionChangedHandler(DefaultScrollerView scrollerView);
            internal event OnSelectionChangedHandler OnSelectionChanged;
            protected virtual void fireOnSelectionChanged(DefaultScrollerView scrollerview)
            {
                OnSelectionChangedHandler handler = this.OnSelectionChanged;
                if (handler != null) handler(scrollerview);
            }

            internal delegate void OnSelectedItemClickedHandler(DefaultScrollerView scrollerView);
            internal event OnSelectedItemClickedHandler OnSelectedItemClicked;
            protected virtual void fireOnSelectedItemClicked(DefaultScrollerView scrollerView)
            {
                var handler = this.OnSelectedItemClicked;
                if (handler != null) handler(scrollerView);
            }

            internal void GUISetup(BottomButtons bottomButtons)
            {
                bottomButtons.OnGroupByChanged += view => this.reset();
            }
        }

        private class CloseHandle
        {
            private const float xBoxSize = 18;
            private const float xBoxMargin = 2;
            GUIStyle buttonStyle;
            bool initted = false;

            internal void Draw(ref Rect winRect)
            {
                if (!initted)
                {
                    buttonStyle = new GUIStyle(GUI.skin.box);
                    buttonStyle.padding = new RectOffset(5, 5, 3, 3);
                    buttonStyle.margin = new RectOffset(2, 2, 2, 2);
                    buttonStyle.stretchWidth = false;
                    buttonStyle.stretchHeight = false;
                    buttonStyle.normal.textColor = buttonStyle.focused.textColor = Color.red;

                    buttonStyle.fontSize = 14;


                    initted = true;
                }

                var resizer = new Rect(xBoxMargin, xBoxMargin, xBoxSize, xBoxSize);
                GUI.Box(resizer, "X", buttonStyle);

                if (!Event.current.isMouse)
                {
                    return;
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    resizer.Contains(Event.current.mousePosition))
                {
                    HaystackResourceLoader.instance.appLauncherButton_OnTrue();
                }
            }
        }

        private class ResizeHandle
        {
            private bool resizing;
            private Vector2 lastPosition = new Vector2(0, 0);
            private const float resizeBoxSize = 18;
            private const float resizeBoxMargin = 2;

            internal void Draw(ref Rect winRect)
            {

                var resizer = new Rect(winRect.width - resizeBoxSize - resizeBoxMargin, resizeBoxMargin, resizeBoxSize, resizeBoxSize);
                GUI.Box(resizer, "//", Resources.resizeBoxStyle);

                if (!Event.current.isMouse)
                {
                    return;
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    resizer.Contains(Event.current.mousePosition))
                {
                    this.resizing = true;
                    this.lastPosition.x = Input.mousePosition.x;
                    this.lastPosition.y = Input.mousePosition.y;

                    Event.current.Use();
                }
            }

            internal void DoResize(ref Rect winRect)
            {
                if (!this.resizing)
                {
                    return;
                }

                if (Input.GetMouseButton(0))
                {
                    var deltaX = Input.mousePosition.x - this.lastPosition.x;
                    var deltaY = Input.mousePosition.y - this.lastPosition.y;

                    //Event.current.delta does not make resizing very smooth.

                    this.lastPosition.x = Input.mousePosition.x;
                    this.lastPosition.y = Input.mousePosition.y;

                    winRect.xMax += deltaX;
                    winRect.yMin -= deltaY;

                    if (Event.current.isMouse)
                    {
                        Event.current.Use();
                    }
                }

                if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
                {
                    this.resizing = false;

                    Event.current.Use();
                }
            }
        } // ResizeHandle


        private class BottomButtons
        {
            private GroupedScrollerView groupedScrollerView;
            private DefaultScrollerView defaultScrollerView;

            internal bool GroupByOrbitingBody { get; private set; }
            internal bool IsHiddenVesselsToggled { get; private set; }
            internal bool IsNearbyOnly { get; private set; }

            private bool isAscendingSortOrder = true;

            internal bool IsAscendingSortOrder
            {
                get { return this.isAscendingSortOrder; }
                private set { this.isAscendingSortOrder = value; }
            }

            private static readonly GUIContent groupByOrbitContent = new GUIContent(Resources.btnOrbitIcon, "Group by orbiting body");
            private static readonly GUIContent hiddenVesselsButtonContent = new GUIContent(Resources.btnHiddenIcon, "Manage hidden vessels");
            private static readonly GUIContent nearbyButtonContent = new GUIContent("NB", "Nearby vessels only");
            private static readonly GUIContent ascendingButtonContent = new GUIContent(Resources.btnAscendingIcon, "Ascending sort order");
            private static readonly GUIContent descendingButtonContent = new GUIContent(Resources.btnDescendingIcon, "Descending sort order");

            internal void LoadSettings()
            {
                this.IsAscendingSortOrder = HaystackResourceLoader.Instance.Settings.BottomButtons["ascending"];
                this.IsNearbyOnly = HaystackResourceLoader.Instance.Settings.BottomButtons["nearby"];
                this.GroupByOrbitingBody = HaystackResourceLoader.Instance.Settings.BottomButtons["groupby"];
            }

            internal void SaveSettings()
            {
                HaystackResourceLoader.Instance.Settings.BottomButtons["ascending"] = this.IsAscendingSortOrder;
                HaystackResourceLoader.Instance.Settings.BottomButtons["nearby"] = this.IsNearbyOnly;
                HaystackResourceLoader.Instance.Settings.BottomButtons["groupby"] = this.GroupByOrbitingBody;
            }

            private void groupByButton()
            {
                //group by toggle
                var previous = this.GroupByOrbitingBody;
                this.GroupByOrbitingBody = GUILayout.Toggle(this.GroupByOrbitingBody,
                    groupByOrbitContent,
                    GUI.skin.button, GUILayout.Width(32f), GUILayout.Height(32f));

                if (previous != this.GroupByOrbitingBody)
                {
                    this.fireOnGroupByChanged(this);
                }
            }

            private void nearbyButton()
            {
                var previous = this.IsNearbyOnly;
                this.IsNearbyOnly = GUILayout.Toggle(this.IsNearbyOnly,
                    nearbyButtonContent, GUI.skin.button, GUILayout.Width(32f),
                    GUILayout.Height(32f));

                if (previous != this.IsNearbyOnly)
                {
                    this.fireOnNearbyChanged(this);
                }
            }

            private void hiddenVesselsButton()
            {
                var previous = this.IsHiddenVesselsToggled;
                this.IsHiddenVesselsToggled = GUILayout.Toggle(this.IsHiddenVesselsToggled,
                    hiddenVesselsButtonContent, GUI.skin.button,
                    GUILayout.Width(32f), GUILayout.Height(32f));

                if (previous != this.IsHiddenVesselsToggled)
                {
                    this.fireOnHiddenVesselsChanged(this);
                }
            }

            private void sortOrderButtons()
            {
                var previous = this.IsAscendingSortOrder;

                var ascPrev = this.IsAscendingSortOrder;
                var descPrev = !this.IsAscendingSortOrder;

                var ascendingButton = GUILayout.Toggle(ascPrev,
                    ascendingButtonContent, GUI.skin.button, GUILayout.Width(32f),
                    GUILayout.Height(32f));

                var descendingButton = GUILayout.Toggle(descPrev, descendingButtonContent, GUI.skin.button,
                    GUILayout.Width(32f), GUILayout.Height(32f));


                var next = previous;
                if (previous && ascPrev == ascendingButton && descendingButton != descPrev)
                {
                    next = !previous;
                }
                if (!previous && descPrev == descendingButton && ascendingButton != ascPrev)
                {
                    next = !previous;
                }

                this.IsAscendingSortOrder = next;

                if (previous != next)
                {
                    this.fireOnSortOrderChanged(this);
                }
            }

            private void targetButton()
            {
                // Disable buttons for current vessel or nothing selected
                if (this.isTargetButtonDisabled())
                {
                    GUI.enabled = false;
                }

                // target button
                if (GUILayout.Button(Resources.btnTarg, Resources.buttonTargStyle))
                {
                    ITargetable selected;

                    if (this.GroupByOrbitingBody)
                    {
                        selected = this.groupedScrollerView.SelectedVessel;
                    }
                    else
                    {
                        selected = (ITargetable)this.defaultScrollerView.SelectedVessel ??
                                   this.defaultScrollerView.SelectedBody;
                    }

                    if (selected != null)
                    {
                        FlightGlobals.fetch.SetVesselTarget(selected);
                    }
                }

                GUI.enabled = true;
            }

            private void flyButton()
            {
                // Disable fly button if we selected a body, have no selection, or selected the current vessel
                if (this.isFlyButtonDisabled())
                {
                    GUI.enabled = false;
                }

                // fly button
                if (GUILayout.Button(Resources.btnGoHover, Resources.buttonGoStyle))
                {
                    this.fireOnSwitchVessel(this.getSelectedVessel());
                }

                GUI.enabled = true;
            }

            internal void Draw()
            {
                GUILayout.BeginHorizontal();

                this.groupByButton();
                this.nearbyButton();
                this.hiddenVesselsButton();

                GUILayout.FlexibleSpace();

                this.sortOrderButtons();

                GUILayout.FlexibleSpace();

                this.targetButton();
                this.flyButton();

                GUILayout.EndHorizontal();
            }

            private bool isTargetButtonDisabled()
            {
                if (!HSUtils.IsInFlight)
                {
                    return true;
                }

                if (this.GroupByOrbitingBody)
                {
                    return this.groupedScrollerView.SelectedVessel == null ||
                           this.groupedScrollerView.SelectedVessel == FlightGlobals.ActiveVessel;
                }


                // cannot target current orbiting body
                if (this.defaultScrollerView.SelectedBody != null && FlightGlobals.ActiveVessel.orbit.referenceBody != this.defaultScrollerView.SelectedBody)
                {
                    return false;
                }

                return this.defaultScrollerView.SelectedVessel == null ||
                       FlightGlobals.ActiveVessel == this.defaultScrollerView.SelectedVessel;
            }

            private bool isFlyButtonDisabled()
            {
                var vessel = this.GroupByOrbitingBody
                    ? this.groupedScrollerView.SelectedVessel
                    : this.defaultScrollerView.SelectedVessel;

                return vessel == null || FlightGlobals.ActiveVessel == vessel;
            }

            private Vessel getSelectedVessel()
            {
                return this.GroupByOrbitingBody
                    ? this.groupedScrollerView.SelectedVessel
                    : this.defaultScrollerView.SelectedVessel;
            }

            internal delegate void OnGroupByChangedHandler(BottomButtons view);

            internal event OnGroupByChangedHandler OnGroupByChanged;

            protected virtual void fireOnGroupByChanged(BottomButtons view)
            {
                var handler = this.OnGroupByChanged;
                if (handler != null) handler(view);
            }

            internal delegate void OnHiddenVesselsChangedHandler(BottomButtons view);

            internal event OnHiddenVesselsChangedHandler OnHiddenVesselsChanged;

            private void fireOnHiddenVesselsChanged(BottomButtons bottomButtons)
            {
                var handler = this.OnHiddenVesselsChanged;
                if (handler != null) handler(bottomButtons);
            }


            internal delegate void OnSwitchVesselHandler(Vessel vessel);

            internal event OnSwitchVesselHandler OnSwitchVessel;

            protected virtual void fireOnSwitchVessel(Vessel vessel)
            {
                var handler = this.OnSwitchVessel;
                if (handler != null) handler(vessel);
            }

            internal delegate void OnNearbyChangedHandler(BottomButtons view);

            internal event OnNearbyChangedHandler OnNearbyChanged;

            protected virtual void fireOnNearbyChanged(BottomButtons view)
            {
                var handler = this.OnNearbyChanged;
                if (handler != null) handler(view);
            }

            internal delegate void OnSortOrderChangedHandler(BottomButtons view);

            internal event OnSortOrderChangedHandler OnSortOrderChanged;

            protected virtual void fireOnSortOrderChanged(BottomButtons view)
            {
                var handler = this.OnSortOrderChanged;
                if (handler != null) handler(view);
            }


            internal void GUISetup(GroupedScrollerView groupedScrollerView, DefaultScrollerView defaultScrollerView)
            {
                this.groupedScrollerView = groupedScrollerView;
                this.defaultScrollerView = defaultScrollerView;
            }
        }

        internal class VesselInfoView
        {
            internal bool Clicked { get; set; }
            private Vessel expandedVessel = null;
            private bool selected;
            private DockingPortListView dockingPortListView;

            private HaystackContinued haystackContinued;
            private BottomButtons bottomButtons;

            internal VesselInfoView(HaystackContinued haystackContinued)
            {
                this.haystackContinued = haystackContinued;
                this.bottomButtons = this.haystackContinued.bottomButtons;
                this.dockingPortListView = new DockingPortListView(haystackContinued);
            }

            internal void Reset()
            {
                this.Clicked = false;
                this.expandedVessel = null;
                this.selected = false;
                this.dockingPortListView.CurrentVessel = null;
            }

            internal void Draw(Vessel vessel, bool selected, Vessel activeVessel)
            {
                this.Clicked = false;
                this.selected = selected;

                if (this.bottomButtons.IsHiddenVesselsToggled &&
                    !global::HaystackReContinued.HiddenVessels.ExcludedTypes.Contains(vessel.vesselType))
                {
                    GUILayout.BeginHorizontal();

                    var hidden = this.haystackContinued.isVesselHidden(vessel);

                    var tooltip = hidden ? "Show vessel" : "Hide vessel";

                    var change = GUILayout.Toggle(hidden, new GUIContent(Resources.btnHiddenIcon, tooltip),
                        GUI.skin.button, GUILayout.Height(24f), GUILayout.Height(24f));
                    if (hidden != change)
                    {
                        this.haystackContinued.markVesselHidden(vessel, change);
                    }
                }

                GUILayout.BeginVertical(selected ? Resources.vesselInfoSelected : Resources.vesselInfoDefault);

                GUILayout.BeginHorizontal();
                string vname;
                if (!Localizer.TryGetStringByTag(vessel.vesselName, out vname))
                    vname = vessel.vesselName;
                GUILayout.Label(vname, Resources.textListHeaderStyle);
                GUILayout.FlexibleSpace();
                this.drawDistance(vessel, activeVessel);
                GUILayout.EndHorizontal();

                this.drawVesselInfoText(vessel, activeVessel);

                GUILayout.EndVertical();

                var check = GUILayoutUtility.GetLastRect();

                if (this.bottomButtons.IsHiddenVesselsToggled &&
                    !global::HaystackReContinued.HiddenVessels.ExcludedTypes.Contains(vessel.vesselType))
                {
                    GUILayout.EndHorizontal();
                }


                if (Event.current != null && Event.current.type == EventType.MouseDown &&
                    Input.GetMouseButtonDown(0) &&
                    check.Contains(Event.current.mousePosition))
                {
                    HSUtils.Log("HaystackContinued, SelectedVessel set to : " + vessel.vesselName);
                    this.Clicked = true;
                    API.SelectedVessel = vessel;
                }
            }

            private void drawDistance(Vessel vessel, Vessel activeVessel)
            {
                string distance = "";

                if (HSUtils.IsInFlight && vessel != activeVessel && vessel != null && activeVessel != null)
                {
                    var calcDistance = Vector3.Distance(activeVessel.transform.position, vessel.transform.position);
                    distance = HSUtils.ToSI(calcDistance) + "m";
                }

                GUILayout.Label(distance, Resources.textSituationStyle);
            }


            private void drawVesselInfoText(Vessel vessel, Vessel activeVessel)
            {
                string status = "";
                int cnt = 0;
                if (activeVessel == vessel)
                {
                    status = ". Currently active";
                    cnt = vessel.Parts.Count;
                }
                else if (vessel.loaded)
                {
                    status = ". Loaded";
                    cnt = vessel.Parts.Count;
                    //cnt = vessel.protoVessel.protoPartSnapshots.Count;
                }
                else cnt = vessel.protoVessel.protoPartSnapshots.Count;

                string situation = "";
                if (cnt == 1)
                    situation = string.Format("{0}. {1}{2}",
                       vessel.vesselType,
                       Vessel.GetSituationString(vessel),
                       status
                       );
                else
                    situation = string.Format("{0}. {1}{2}. Parts: {3}",
                       vessel.vesselType,
                       Vessel.GetSituationString(vessel),
                       status, cnt
                       );

                GUILayout.BeginHorizontal();

                GUILayout.Label(situation, Resources.textSituationStyle);
                if (this.selected)
                {
                    GUILayout.FlexibleSpace();

                    drawDockingExpandButton(vessel);
                }
                GUILayout.EndHorizontal();


                this.dockingPortListView.Draw(vessel);
            }

            private void drawDockingExpandButton(Vessel vessel)
            {
                //can't show docking ports in the tracking center or space center
                if (HSUtils.IsTrackingCenterActive || HSUtils.IsSpaceCenterActive)
                {
                    return;
                }

                var enabled = vessel == this.expandedVessel;
                var icon = enabled ? Resources.btnDownArrow : Resources.btnUpArrow;

                var result = GUILayout.Toggle(enabled, new GUIContent(icon, "Show Docking Ports"),
                    Resources.buttonExpandStyle);

                if (result != enabled)
                {
                    this.expandedVessel = result ? vessel : null;
                    this.dockingPortListView.CurrentVessel = this.expandedVessel;
                }
            }
        }

        internal class DockingPortListView
        {
            private Vessel currentVessel;
            private readonly List<PortInfo> portList = new List<PortInfo>();
            private readonly HaystackContinued haystackContinued;
            private bool runUpdate;

            private static readonly Type moduleDockingNodeNamedType;
            private static readonly FieldInfo modulePortName;
            private static bool namedDockingPortSupport;

            static DockingPortListView()
            {
                try
                {
                    Type result = null;
                    AssemblyLoader.loadedAssemblies.TypeOperation(t =>
                    {
                        if (t.FullName == "NavyFish.ModuleDockingNodeNamed")
                        {
                            result = t;
                        }
                    });

                    moduleDockingNodeNamedType = result;

                    modulePortName = moduleDockingNodeNamedType.GetField("portName",
                        BindingFlags.Instance | BindingFlags.Public);
                }
                catch (Exception e)
                {
                    moduleDockingNodeNamedType = null;
                    modulePortName = null;
                    HSUtils.DebugLog("exception getting docking port alignment indicator type");
                    HSUtils.DebugLog("{0}", e.Message);
                }

                if (moduleDockingNodeNamedType != null && modulePortName != null)
                {
                    namedDockingPortSupport = true;

                    HSUtils.Log("Docking Port Alignment Indicator mod detected: using named docking node support.");
                    HSUtils.DebugLog("{0} {1}", moduleDockingNodeNamedType.FullName,
                        moduleDockingNodeNamedType.AssemblyQualifiedName);
                }
                else
                {
                    HSUtils.DebugLog("Docking Port Alignment Indicator mod was not detected");
                }
            }

            internal DockingPortListView(HaystackContinued haystackContinued)
            {
                this.haystackContinued = haystackContinued;
            }

            internal Vessel CurrentVessel
            {
                get { return this.currentVessel; }
                set
                {
                    this.currentVessel = value;
                    this.handleVesselChange();
                }
            }

            private void handleVesselChange()
            {
                if (this.currentVessel == null)
                {
                    this.portList.Clear();
                    this.runUpdate = false;
                    return;
                }

                this.runUpdate = true;
                this.haystackContinued.StartCoroutine(this.updatePortListCoroutine());
            }

            public IEnumerator updatePortListCoroutine()
            {
                while (this.runUpdate)
                {
                    yield return new WaitForEndOfFrame();

                    this.populatePortList();

                    yield return new WaitForSeconds(30f);
                }
            }

            public void populatePortList()
            {
                this.portList.Clear();

                var targetables = this.currentVessel.FindPartModulesImplementing<ITargetable>();
                foreach (var targetable in targetables)
                {
                    var port = targetable as ModuleDockingNode;
                    if (port == null)
                    {
                        continue;
                    }

                    if (port.state.StartsWith("Docked", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // don't display docking ports that have all their attach nodes used.
                    var usedNodeCount = port.part.attachNodes.Count(node => node.attachedPart != null);
                    if (usedNodeCount == port.part.attachNodes.Count)
                    {
                        continue;
                    }

                    var info = new PortInfo
                    {
                        Name = getPortName(port),
                        PortNode = port,
                    };

                    this.portList.Add(info);
                }

                portList.Sort((a, b) => a.Name.CompareTo(b.Name));
            }

            private string getPortName(ModuleDockingNode port)
            {
                HSUtils.DebugLog("DockingPortListView#getPortName: start");

                if (!namedDockingPortSupport)
                {
                    return port.part.partInfo.title.Trim();
                }

                PartModule found = null;
                for (int i = 0; i < port.part.Modules.Count; i++)
                {
                    var module = port.part.Modules[i];
                    if (module.GetType() == moduleDockingNodeNamedType)
                    {
                        found = module;
                        break;
                    }
                }

                if (found == null)
                {
                    HSUtils.DebugLog(
                        "DockingPortListView#getPortName: named docking port support enabled but could not find the part module");
                    return port.part.partInfo.title;
                }

                var portName = (string)modulePortName.GetValue(found);

                HSUtils.DebugLog("DockingPortListView#getPortName: found name: {0}", portName);

                return portName;
            }

            internal void Draw(Vessel vessel)
            {
                if (this.CurrentVessel == null || vessel != this.CurrentVessel)
                {
                    return;
                }

                if (!this.CurrentVessel.loaded)
                {
                    GUILayout.BeginVertical();
                    GUILayout.Label("The vessel is out of range: cannot list docking ports",
                        Resources.textSituationStyle);
                    GUILayout.EndVertical();
                    return;
                }

                if (this.portList.IsEmpty())
                {
                    GUILayout.BeginVertical();
                    GUILayout.Label("This vessel does not have any docking ports", Resources.textSituationStyle);
                    GUILayout.EndVertical();
                    return;
                }

                GUILayout.BeginVertical();

                GUILayout.Label("Docking Ports", Resources.textDockingPortHeaderStyle);

                foreach (var i in portList)
                {
                    GUILayout.Box((Texture)null, Resources.hrSepLineStyle, GUILayout.ExpandWidth(true),
                        GUILayout.ExpandHeight(false));

                    GUILayout.BeginHorizontal();

                    GUILayout.Label(i.Name, Resources.textDockingPortStyle, GUILayout.ExpandHeight(false));
                    GUILayout.FlexibleSpace();

                    if (FlightGlobals.ActiveVessel != this.currentVessel)
                    {
                        var distance = this.getDistanceText(i.PortNode);
                        GUILayout.Label(distance, Resources.textDockingPortDistanceStyle, GUILayout.ExpandHeight(true));
                        GUILayout.Space(10f);
                        if (GUILayout.Button(Resources.btnTargetAlpha, Resources.buttonDockingPortTarget,
                            GUILayout.Width(18f),
                            GUILayout.Height(18f)))
                        {
                            setDockingPortTarget(i.PortNode);
                        }

                        GUILayout.Space(10f);
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
            }

            private void setDockingPortTarget(ModuleDockingNode portNode)
            {
                var vessel = portNode.GetVessel();

                //can't set target if the vessel is not loaded or is the active vessel
                if (!vessel.loaded || vessel.isActiveVessel)
                {
                    return;
                }

                FlightGlobals.fetch.SetVesselTarget(portNode);
            }

            private string getDistanceText(ModuleDockingNode port)
            {
                var activeVessel = FlightGlobals.ActiveVessel;
                var distance = Vector3.Distance(activeVessel.transform.position, port.GetTransform().position);

                return string.Format("{0}m", HSUtils.ToSI(distance));
            }

            private struct PortInfo
            {
                internal string Name;
                internal ModuleDockingNode PortNode;
            }
        }

        private class ExpandedVesselInfo : IDisposable
        {
            private HaystackContinued haystack;
            private DefaultScrollerView defaultScrollerView;
            private GroupedScrollerView groupedScrollerView;
            private BottomButtons bottomButtons;

            private bool _isExpanded;
            private VesselData vesselData = new VesselData();
            private BodyData bodyData = new BodyData();

            private readonly int windowId = Resources.rnd.Next(1000, 2000000);
            private Vector2 scrollPosition = new Vector2(0, 0);
            private Rect windowRect;

            private GUIContent renameContent = new GUIContent("R", "Rename vessel");
            private GUIContent terminateContent = new GUIContent(Resources.btnTerminateNormalBackground, "Terminate vessel");
            //btnTerminateFilePath

            internal ExpandedVesselInfo(HaystackContinued haystack, BottomButtons bottomButtons, DefaultScrollerView defaultScrollerView,
                GroupedScrollerView groupedScrollerView)
            {
                this.haystack = haystack;
                this.defaultScrollerView = defaultScrollerView;
                this.groupedScrollerView = groupedScrollerView;

                this.defaultScrollerView.OnSelectionChanged += view => this.updateData();
                this.defaultScrollerView.OnSelectedItemClicked += view => this.IsExpanded = !this.IsExpanded;
                this.groupedScrollerView.OnSelectionChanged += view => this.updateData();
                this.groupedScrollerView.OnSelectedItemClicked += view => this.IsExpanded = !this.IsExpanded;

                this.bottomButtons = bottomButtons;
                this.windowRect = new Rect(this.haystack.WinRect.xMax, this.haystack.WinRect.y, 0,
                       this.haystack.winRect.height);
            }

            private bool IsVessel
            {
                get
                {
                    if (bottomButtons.GroupByOrbitingBody)
                    {
                        if (groupedScrollerView.SelectedVessel != null)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (defaultScrollerView.SelectedVessel != null)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            private Vessel currentVessel
            {
                get
                {
                    return this.bottomButtons.GroupByOrbitingBody ?
                        this.groupedScrollerView.SelectedVessel : this.defaultScrollerView.SelectedVessel;
                }
            }

            private bool IsBody
            {
                get { return (!bottomButtons.GroupByOrbitingBody && defaultScrollerView.SelectedBody != null); }
            }

            private CelestialBody currentBody
            {
                get
                {
                    if (!bottomButtons.GroupByOrbitingBody)
                    {
                        return defaultScrollerView.SelectedBody;
                    }
                    return null;
                }
            }

            private bool IsExpanded
            {
                get { return this._isExpanded; }
                set
                {
                    if (value)
                    {
                        this.updateData();
                        this.haystack.StartCoroutine(this.updateDataCoroutine());
                    }
                    else
                    {
                        this.scrollPosition = new Vector2(0, 0);
                    }
                    this._isExpanded = value;
                }
            }

            private void updateData()
            {
                this.windowRect.width = 0;
                if (IsVessel)
                {
                    updateVesselData();
                }
                if (IsBody)
                {
                    this.updateBodyData();
                }
            }
            private void updateBodyData()
            {
                var body = this.currentBody;
                var same = body.bodyName.GetHashCode() == this.bodyData.Id;

                var newBodyData = new BodyData
                {
                    Id = body.bodyName.GetHashCode(),
                    OrbitData = body.orbit != null ? OrbitData.FromOrbit(body.orbit) : new OrbitData(),
                    PhysicalData = same ? this.bodyData.PhysicalData : getPhysicalData(body),
                    AtmData = same ? this.bodyData.AtmData : getAtmData(body),
                    SciData = same ? this.bodyData.SciData : getSciData(body),
                    Satellites = same ? this.bodyData.Satellites : getSatellites(body),
                };

                this.bodyData = newBodyData;
            }

            private List<DisplayItem> getSatellites(CelestialBody body)
            {
                return (from satellite in body.orbitingBodies
                        select DisplayItem.Create(satellite.bodyName, "")).ToList();
            }

            private List<DisplayItem> getSciData(CelestialBody body)
            {
                var items = new List<DisplayItem>();
                var sci = body.scienceValues;

                var spaceHigh = DisplayItem.Create("Space High Alt: ", sci.spaceAltitudeThreshold.ToString("N0") + "m");
                items.Add(spaceHigh);
                if (body.atmosphere)
                {
                    var flyingHigh = DisplayItem.Create("Flying High Alt: ",
                        sci.flyingAltitudeThreshold.ToString("N0") + "m");
                    items.Add(flyingHigh);
                }

                return items;
            }

            private List<DisplayItem> getAtmData(CelestialBody body)
            {
                var items = new List<DisplayItem>();

                if (!body.atmosphere)
                {
                    return items;
                }

                var maxHeight = DisplayItem.Create("Atmopshere Ends: ", body.atmosphereDepth.ToString("N0") + "m");
                items.Add(maxHeight);
                var oxygen = DisplayItem.Create("Oxygen?: ", body.atmosphereContainsOxygen ? "Yes" : "No");
                items.Add(oxygen);

                var kPAASL = body.GetPressure(0d);
                var atmASL = kPAASL * PhysicsGlobals.KpaToAtmospheres;
                var aslDisplay = string.Format("{0}kPa ({1}atm)", kPAASL.ToString("F2"), atmASL.ToString("F2"));
                var aslPressure = DisplayItem.Create("Atm. ASL: ", aslDisplay);
                items.Add(aslPressure);

                var surfaceTemp = DisplayItem.Create("Surface Temp: ", body.GetTemperature(0.0).ToString("0.##") + "K");
                items.Add(surfaceTemp);

                return items;
            }

            private List<DisplayItem> getPhysicalData(CelestialBody body)
            {
                var radius = DisplayItem.Create("Radius: ", (body.Radius / 1000d).ToString("N0") + "km");
                var mass = DisplayItem.Create("Mass: ", body.Mass.ToString("0.###E+0") + "kg");
                var gm = DisplayItem.Create("GM: ", body.gravParameter.ToString("0.###E+0"));
                var gravity = DisplayItem.Create("Surface Gravity: ", body.GeeASL.ToString("0.####") + "g");

                var escape = 2d * body.gravParameter / body.Radius;
                escape = Math.Sqrt(escape);

                var escapeVelocity = DisplayItem.Create("Escape Velocity: ", escape.ToString("0.0") + "m/s");

                double alt = body.atmosphere ? body.atmosphereDepth + 20000 : 15000;
                var orbitVelocity = Math.Sqrt(body.gravParameter / (body.Radius + alt));

                var standardOrbitVelocity = DisplayItem.Create("Std Orbit Velocity: ",
                    orbitVelocity.ToString("0.0") + "m/s @ " + Converters.Distance(alt));

                var rotationalPeriod = DisplayItem.Create("Rotational Period: ", Converters.Duration(body.rotationPeriod));
                var tidalLocked = DisplayItem.Create("Tidally Locked: ", body.tidallyLocked ? "Yes" : "No");
                var soiSize = DisplayItem.Create("SOI Size: ", (body.sphereOfInfluence / 1000d).ToString("N0") + "km");

                return new List<DisplayItem>
                {
                    radius,
                    mass,
                    gm,
                    gravity,
                    standardOrbitVelocity,
                    escapeVelocity,
                    rotationalPeriod,
                    tidalLocked,
                    soiSize
                };
            }

            private void updateVesselData()
            {
                var vessel = this.currentVessel;

                bool hasNextNode = false;

                var nextNodeTime = Vessel.GetNextManeuverTime(vessel, out hasNextNode);

                // some stuff doesn't need to be updated if it's not changing
                var shouldUpdate = vessel.isActiveVessel || vessel.loaded || !this.vesselData.Id.Equals(vessel.id);

                var vesselData = new VesselData
                {
                    Id = vessel.id,
                    OrbitData = OrbitData.FromOrbit(vessel.orbit),
                    Resources = shouldUpdate ? updateVesselResourceData(vessel) : this.vesselData.Resources,
                    CrewData = shouldUpdate ? this.updateVesselCrewData(vessel) : this.vesselData.CrewData,
                    MET = Converters.Duration(Math.Abs(vessel.missionTime)),
                    HasNextNode = hasNextNode,
                    NextNodeIn = "T " + KSPUtil.PrintTime(Planetarium.GetUniversalTime() - nextNodeTime, 3, true),
                    NextNodeTime = KSPUtil.PrintDateCompact(nextNodeTime, true, true),
                    Situation = Vessel.GetSituationString(vessel),
                };

                this.vesselData = vesselData;
            }

            private CrewData updateVesselCrewData(Vessel vessel)
            {
                var crewData = new CrewData();

                if (vessel.isEVA)
                {
                    return crewData;
                }

                if (vessel.loaded)
                {
                    crewData.TotalCrew = vessel.GetCrewCount();
                    crewData.MaxCrew = vessel.GetCrewCapacity();

                    crewData.Crew = (from part in vessel.parts
                                     from crew in part.protoModuleCrew
                                     orderby crew.name
                                     select formatCrewDisplay(part.partInfo.title, crew)).ToList();
                }
                else
                {
                    crewData.TotalCrew = vessel.protoVessel.GetVesselCrew().Count;

                    crewData.MaxCrew = vessel.protoVessel.protoPartSnapshots.Aggregate(0,
                        (acc, part) => acc + part.partInfo.partPrefab.CrewCapacity);

                    crewData.Crew = (from part in vessel.protoVessel.protoPartSnapshots
                                     from crew in part.protoModuleCrew
                                     orderby crew.name
                                     select this.formatCrewDisplay(part.partInfo.title, crew)).ToList();
                }

                return crewData;
            }

            private string formatCrewDisplay(string partName, ProtoCrewMember crew)
            {
                var profession = crew.experienceTrait.Title.Substring(0, 1);
                var name = crew.name;
                var experiance = crew.experienceLevel;

                return string.Format("{0} ({1}:{2}) - {3}", name, profession, experiance, partName);
            }

            private List<string> updateVesselResourceData(Vessel vessel)
            {
                if (vessel.loaded)
                {
                    var counts = from part in vessel.parts
                                 from resource in part.Resources
                                 group resource by resource.resourceName
                        into resources
                                 select new
                                 {
                                     ResourceName = resources.Key,
                                     Total = resources.Aggregate(0d, (acc, r) => acc + r.amount),
                                     Max = resources.Aggregate(0d, (acc, r) => acc + r.maxAmount),
                                 };

                    return (from resource in counts
                            orderby resource.ResourceName
                            select
                                string.Format("{0}: {1}/{2} ({3})", resource.ResourceName, resource.Total.ToString("N1"), resource.Max.ToString("N1"),
                                    (resource.Total / resource.Max).ToString("P1"))).ToList();
                }
                else
                {
                    var counts = from proto in vessel.protoVessel.protoPartSnapshots
                                 from resource in proto.resources
                                 group resource by resource.resourceName
                        into resources
                                 select new
                                 {
                                     ResourceName = resources.Key,
                                     Total =
                                         resources.Aggregate(0d, (acc, r) => acc + r.amount),
                                     Max =
                                         resources.Aggregate(0d,
                                             (acc, r) => acc + r.maxAmount)
                                 };



                    return (from resource in counts
                            orderby resource.ResourceName
                            select
                                string.Format("{0}: {1}/{2} ({3})", resource.ResourceName, resource.Total.ToString("N1"), resource.Max.ToString("N1"),
                                    (resource.Total / resource.Max).ToString("P1"))).ToList();
                }
            }

            private IEnumerator updateDataCoroutine()
            {
                yield return new WaitForEndOfFrame();
                while (this.IsExpanded && (this.IsVessel || this.IsBody))
                {
                    this.updateData();
                    yield return new WaitForSeconds(1f);
                }
            }

            public void DrawExpandButton()
            {
                GUILayout.BeginVertical();
                if (GUILayout.Button(this.IsExpanded ? Resources.btnExtendedIconClose : Resources.btnExtendedIconOpen, Resources.buttonExtendedStyle, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(false), GUILayout.Width(12f)))
                {
                    this.IsExpanded = !this.IsExpanded;
                }
                GUILayout.Space(20f);
                GUILayout.EndVertical();
            }

            public void DrawExpandedWindow()
            {
                if (!IsExpanded)
                {
                    return;
                }

                this.windowRect.x = this.haystack.WinRect.xMax;
                this.windowRect.y = this.haystack.WinRect.y;
                this.windowRect.height = this.haystack.WinRect.height;
                var rect = ClickThruBlocker.GUILayoutWindow(this.windowId, this.windowRect, this.drawExpanded, "Vessel Infomation", Resources.winStyle, new[] { GUILayout.MaxWidth(600), GUILayout.MinHeight(this.haystack.winRect.height), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false) });
                this.windowRect.width = rect.width;
            }

            private void drawExpanded(int id)
            {
                GUILayout.BeginHorizontal();
                this.scrollPosition = GUILayout.BeginScrollView(this.scrollPosition, GUILayout.Width(300f));
                GUILayout.BeginVertical();

                if (this.IsVessel)
                {
                    drawVessel();
                }
                else if (this.IsBody)
                {
                    drawBody();
                }
                else
                {
                    GUILayout.Label("Nothing is selected", Resources.textListHeaderStyle);
                }

                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndHorizontal();

                if (GUI.tooltip != "")
                {
                    var mousePosition = Event.current.mousePosition;
                    var content = new GUIContent(GUI.tooltip);
                    var size = Resources.tooltipBoxStyle.CalcSize(content);
                    GUI.Box(new Rect(mousePosition.x - 30, mousePosition.y - 30, size.x + 12, 25).ClampToPosIn(this.windowRect), content, Resources.tooltipBoxStyle);
                }
            }

            private void drawBody()
            {
                GUILayout.Space(4f);
                GUILayout.Label(this.currentBody.bodyName, Resources.textListHeaderStyle);

                if (this.currentBody.orbit != null)
                {
                    GUILayout.Space(10f);
                    this.drawOrbit(this.bodyData.OrbitData);
                }

                GUILayout.Space(10f);
                this.drawItemList("Science Information:", this.bodyData.SciData);

                GUILayout.Space(10f);
                this.drawItemList("Physical Information:", this.bodyData.PhysicalData);

                if (this.currentBody.atmosphere)
                {
                    GUILayout.Space(10f);
                    this.drawItemList("Atmospheric Information:", this.bodyData.AtmData);
                }

                if (!this.bodyData.Satellites.IsEmpty())
                {
                    GUILayout.Space(10f);
                    this.drawItemList("Satellites:", this.bodyData.Satellites);
                }
            }

            private void TerminateVessel()
            {
                //this.unlockUI();
                Vessel vessel = this.currentVessel;

                GameEvents.onVesselTerminated.Fire(vessel.protoVessel);
                KSP.UI.Screens.SpaceTracking.StopTrackingObject(vessel);
                List<ProtoCrewMember> vesselCrew = vessel.GetVesselCrew();
                int count = vesselCrew.Count;
                for (int i = 0; i < count; i++)
                {
                    ProtoCrewMember protoCrewMember = vesselCrew[i];
                    //UnityEngine.Debug.Log("Crewmember " + protoCrewMember.name + " is lost.");
                    protoCrewMember.StartRespawnPeriod(-1.0);
                }
                UnityEngine.Object.DestroyImmediate(vessel.gameObject);
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                this.OnDialogDismiss();
            }

            private void OnDialogDismiss()
            {
                popupDialog = false;
                //this.unlockUI();
            }
            internal bool popupDialog = false;

            private void drawVessel()
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                string vname;
                if (!Localizer.TryGetStringByTag(this.currentVessel.vesselName, out vname))
                    vname = this.currentVessel.vesselName;

                GUILayout.Label(vname, Resources.textExpandedVesselNameStyle, GUILayout.ExpandWidth(false));

                GUILayout.Space(8f);
                if (GUILayout.Button(renameContent, Resources.buttonRenameStyle, GUILayout.Width(16f), GUILayout.Height(16f)))
                {
                    this.currentVessel.RenameVessel();
                }
                GUILayout.FlexibleSpace();
                if (HighLogic.CurrentGame.Mode != Game.Modes.MISSION_BUILDER && this.currentVessel != FlightGlobals.ActiveVessel)
                {
                    if (GUILayout.Button(terminateContent, Resources.buttonTerminateStyle, GUILayout.Width(16f), GUILayout.Height(16f)))
                    {
                        //TerminateVessel(this.currentVessel);
                        popupDialog = true;

                        PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog("TerminateMission", Localizer.Format("#autoLOC_481625"), Localizer.Format("#autoLOC_5050048"), HighLogic.UISkin, new DialogGUIBase[]
                               {
                                        new DialogGUIButton("Terminate", new Callback(this.TerminateVessel)),
                                        new DialogGUIButton("Cancel", new Callback(this.OnDialogDismiss))
                               }), false, HighLogic.UISkin, true, string.Empty);

                    }
                }

                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                int cnt = 0;
                if (currentVessel.loaded)
                    cnt = currentVessel.Parts.Count;
                else
                    cnt = currentVessel.protoVessel.protoPartSnapshots.Count;
                GUILayout.Label("Parts: " + cnt, Resources.textVesselExpandedInfoItem);
                GUILayout.Space(10f);

                GUILayout.EndHorizontal();

                drawResources();
                drawCrew();
                drawOrbit(vesselData.OrbitData);

                GUILayout.Space(10f);
                GUILayout.Label("Status:", Resources.textListHeaderStyle);
                GUILayout.Label(this.vesselData.Situation, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("MET: " + this.vesselData.MET, Resources.textVesselExpandedInfoItem);

                if (this.vesselData.HasNextNode)
                {
                    GUILayout.Label("Maneuver Node in: " + this.vesselData.NextNodeIn, Resources.textVesselExpandedInfoItem);
                    GUILayout.Label("Maneuver Node @: " + this.vesselData.NextNodeTime, Resources.textVesselExpandedInfoItem);
                }
            }

            private void drawOrbit(OrbitData orbitData)
            {
                GUILayout.Label("Orbial information:", Resources.textListHeaderStyle);
                GUILayout.Label("SOI: " + orbitData.SOI, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Apoapsis: " + orbitData.AP, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Periapsis: " + orbitData.PE, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Time to AP: " + orbitData.timeToAP, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Time to PE: " + orbitData.timeToPE, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Orbital Period: " + orbitData.Period, Resources.textVesselExpandedInfoItem);
                GUILayout.Label("Inclination: " + orbitData.INC, Resources.textVesselExpandedInfoItem);
                if (orbitData.IsSOIChange)
                {
                    GUILayout.Label("SOI Change Time: " + orbitData.SOIChangeTime, Resources.textVesselExpandedInfoItem);
                    GUILayout.Label("SOI Change Date: " + orbitData.SOIChangeDate, Resources.textVesselExpandedInfoItem);
                }
            }

            private void drawCrew()
            {
                if (!this.vesselData.HasCrew)
                {
                    return;
                }
                var crewData = this.vesselData.CrewData;

                var crewDisplay = string.Format("Crew ({0} / {1}):", crewData.TotalCrew, crewData.MaxCrew);
                GUILayout.Label(crewDisplay, Resources.textListHeaderStyle);

                foreach (var crew in crewData.Crew)
                {
                    GUILayout.Label(crew, Resources.textVesselExpandedInfoItem);
                }

                GUILayout.Space(10f);
            }

            private void drawResources()
            {
                if (!this.vesselData.HasResources)
                {
                    return;
                }

                GUILayout.Label("Resources:", Resources.textListHeaderStyle);

                foreach (var resourceDisplay in this.vesselData.Resources)
                {
                    GUILayout.Label(resourceDisplay, Resources.textVesselExpandedInfoItem);
                }

                GUILayout.Space(10f);
            }

            private void drawItemList(string header, IEnumerable<DisplayItem> items)
            {
                GUILayout.Label(header, Resources.textListHeaderStyle);
                foreach (var item in items)
                {
                    GUILayout.Label(string.Format("{0} {1}", item.Label, item.Value), Resources.textVesselExpandedInfoItem);
                }
            }

            public void Dispose()
            {
                this.IsExpanded = false;
            }

            private class VesselData
            {
                public Guid Id;
                public OrbitData OrbitData = new OrbitData();
                public List<string> Resources = new List<string>();
                public CrewData CrewData = new CrewData();
                public string MET = string.Empty;
                public bool HasNextNode;
                public string NextNodeIn = string.Empty;
                public string NextNodeTime = string.Empty;
                public string Situation = string.Empty;

                public bool HasCrew { get { return CrewData.TotalCrew > 0; } }
                public bool HasResources { get { return this.Resources.Count > 0; } }

            }

            private class CrewData
            {
                public int TotalCrew;
                public int MaxCrew;
                public List<string> Crew = new List<string>();
            }

            private class OrbitData
            {
                public string SOI = string.Empty;
                public string AP = string.Empty;
                public string PE = string.Empty;
                public string timeToAP = string.Empty;
                public string timeToPE = string.Empty;
                public string INC = string.Empty;
                public string Period = string.Empty;
                public bool IsSOIChange;
                public string SOIChangeTime = string.Empty;
                public string SOIChangeDate = string.Empty;

                public static OrbitData FromOrbit(Orbit orbit)
                {
                    return new OrbitData
                    {
                        SOI = orbit.referenceBody.bodyName,
                        AP = Converters.Distance(Math.Max(0, orbit.ApA)),
                        PE = Converters.Distance(Math.Max(0, orbit.PeA)),
                        timeToAP = Converters.Duration(Math.Max(0, orbit.timeToAp)),
                        timeToPE = Converters.Duration(Math.Max(0, orbit.timeToPe)),
                        INC = orbit.inclination.ToString("F3") + "°",
                        Period = Converters.Duration(Math.Max(0, orbit.period), 4),
                        IsSOIChange = orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE || orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER,
                        SOIChangeTime = Converters.Duration(orbit.UTsoi - Planetarium.GetUniversalTime()),
                        SOIChangeDate = KSPUtil.PrintDateCompact(orbit.UTsoi, true, true)

                    };
                }
            }

            private class BodyData
            {
                public int Id;
                public OrbitData OrbitData = new OrbitData();
                public List<DisplayItem> PhysicalData = new List<DisplayItem>();
                public List<DisplayItem> AtmData = new List<DisplayItem>();
                public List<DisplayItem> SciData = new List<DisplayItem>();
                public List<DisplayItem> Satellites = new List<DisplayItem>();
            }

            private class DisplayItem
            {
                public string Label { get; private set; }
                public string Value { get; private set; }

                public static DisplayItem Create(string label, string value)
                {
                    return new DisplayItem
                    {
                        Label = label,
                        Value = value,
                    };
                }
            }
        }
    } // HaystackContinued
}