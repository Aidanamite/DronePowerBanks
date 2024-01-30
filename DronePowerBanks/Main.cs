using HarmonyLib;
using SRML;
using SRML.Console;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SRML.Utils.Enum;
using SRML.SR;
using MonomiPark.SlimeRancher.DataModel;
using MonomiPark.SlimeRancher.Regions;
using MonomiPark.SlimeRancher;
using MonomiPark.SlimeRancher.Persist;
using SRML.SR.Translation;

namespace DronePowerBanks
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{System.Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";

        public override void PreLoad()
        {
            HarmonyInstance.PatchAll();
            TranslationPatcher.AddUITranslation("w.limit_reached_battery", "Power Bank limit reached for this Ranch Expansion. Limit: 2");
            TranslationPatcher.AddUITranslation("w.not_on_ranch_battery", "Power Bank can only be installed on the Ranch");
        }
        public override void Load()
        {
            var DroneDefinition = SRSingleton<GameContext>.Instance.LookupDirector.GetGadgetDefinition(Gadget.Id.DRONE);
            var NewGadgetPrefab = new GameObject("").CreatePrefabCopy();
            var NewBatteryGadget = NewGadgetPrefab.AddComponent<BatteryGadget>();
            var WaterMeterPrefab = DroneDefinition.prefab.transform.Find("drone_station/techActivator/waterMeter").gameObject;
            DroneDefinition.prefab.transform.Find("drone_station/techActivator").GetComponent<Recolorizer>().CopyAllTo(NewGadgetPrefab.AddComponent<Recolorizer>());
            for (int i = 1; i <= BatteryGadget.MeterCount; i++)
            {
                var a = Mathf.PI * 2 / BatteryGadget.MeterCount * i;
                var WaterMeter = Object.Instantiate(WaterMeterPrefab, NewGadgetPrefab.transform, false);
                WaterMeter.name = $"waterMeter ({i})";
                WaterMeter.AddComponent<PowerReciever>().gadget = NewBatteryGadget;
                var Collider = WaterMeter.AddComponent<CapsuleCollider>();
                Collider.height = 1;
                Collider.radius = 0.25f;
                WaterMeter.transform.localPosition += new Vector3(Mathf.Cos(a) * 0.5f,0, Mathf.Sin(a) * 0.5f);
            }
            NewBatteryGadget.id = Id.POWER_BANK;
            GadgetRegistry.RegisterBlueprintLock(Id.POWER_BANK, (GadgetDirector x) => x.CreateBasicLock(Id.POWER_BANK, Gadget.Id.DRONE, ProgressDirector.ProgressType.UNLOCK_RUINS, 24f));
            
            var NewGadgetDefinition = ScriptableObject.CreateInstance<GadgetDefinition>();
            NewGadgetDefinition.blueprintCost = 5000;
            NewGadgetDefinition.buyCountLimit = -1;
            NewGadgetDefinition.buyInPairs = false;
            NewGadgetDefinition.countLimit = 2;
            NewGadgetDefinition.countOtherIds = new Gadget.Id[0];
            NewGadgetDefinition.craftCosts = new GadgetDefinition.CraftCost[] {
                new GadgetDefinition.CraftCost() { id = Identifiable.Id.HONEY_PLORT, amount = 20 },
                new GadgetDefinition.CraftCost() { id = Identifiable.Id.QUANTUM_PLORT, amount = 10 },
                new GadgetDefinition.CraftCost() { id = Identifiable.Id.WILD_HONEY_CRAFT, amount = 6 },
                new GadgetDefinition.CraftCost() { id = Identifiable.Id.JELLYSTONE_CRAFT, amount = 18 },
                new GadgetDefinition.CraftCost() { id = Identifiable.Id.HEXACOMB_CRAFT, amount = 6 }
            }; ;
            NewGadgetDefinition.destroyOnRemoval = false;
            NewGadgetDefinition.icon = DroneDefinition.icon;
            NewGadgetDefinition.id = Id.POWER_BANK;
            NewGadgetDefinition.pediaLink = PediaDirector.Id.UTILITIES;
            NewGadgetDefinition.prefab = NewGadgetPrefab;
            LookupRegistry.RegisterGadget(NewGadgetDefinition);
            GadgetRegistry.ClassifyGadget(Id.POWER_BANK, GadgetRegistry.GadgetClassification.MISC);
            Id.POWER_BANK.GetTranslation().SetNameTranslation("Drone Power Bank").SetDescriptionTranslation("A set of water containers that can get drones running for a while longer");
            if (SRModLoader.IsModPresent("dimensionwarpslime"))
                DWS.Setup();
        }
        public static void Log(string message) => Console.Log($"[{modName}]: " + message);
        public static void LogError(string message) => Console.LogError($"[{modName}]: " + message);
        public static void LogWarning(string message) => Console.LogWarning($"[{modName}]: " + message);
        public static void LogSuccess(string message) => Console.LogSuccess($"[{modName}]: " + message);
    }

    [EnumHolder]
    public static class Id
    {
        public static readonly Gadget.Id POWER_BANK;
    }

    public static class ExtentionMethods
    {
        public static T Find<T>(this T[] t, System.Predicate<T> predicate)
        {
            foreach (var i in t)
                if (predicate(i))
                    return i;
            return default(T);
        }
    }

    class BatteryGadget : Gadget, LiquidConsumer, GadgetModel.Participant
    {
        public const int MeterCount = 3;
        public const double MeterMax = 100800;
        public const double MaxBattery = MeterMax * MeterCount;
        public static List<BatteryGadget> Batteries = new List<BatteryGadget>();
        public DroneNetwork Network { get; private set; }
        Transform[] meters;
        internal BatteryModel model;
        int dronesInArea;
        public double BatteryPerDrone => dronesInArea == 0 ? 0 : model.battery / dronesInArea;
        public float percentage
        {
            get => (float)(model.battery / MaxBattery);
            private set
            {
                var v = value * MeterCount;
                for (int i = 0; i < MeterCount; i++)
                    if (i + 1 <= v)
                        meters[i].localScale = Vector3.one;
                    else if (i + 1 > v && i < v)
                        meters[i].localScale = new Vector3(1, v % 1, 1);
                    else
                        meters[i].localScale = new Vector3(1, 0, 1);
            }
        }
        new void Awake()
        {
            base.Awake();
            Network = DroneNetwork.Find(gameObject);
            meters = new Transform[MeterCount];
            for (int i = 1; i <= MeterCount; i++)
                meters[i - 1] = transform.Find($"waterMeter ({i})").Find("water");
            Batteries.Add(this);
        }
        void OnDestroy() => Batteries.Remove(this);
        void Update()
        {
            var worldTime = SceneContext.Instance.TimeDirector.WorldTime();
            dronesInArea = 0;
            foreach (var d in Network.GetComponentsInChildren<DroneStationBattery>())
                if (model.battery > 0 && d.droneModel.batteryDepleteTime - worldTime < MeterMax)
                {
                    var missing = System.Math.Min(System.Math.Max(MeterMax - (d.droneModel.batteryDepleteTime - worldTime), 0), model.battery);
                    d.droneModel.batteryDepleteTime += missing;
                    model.battery -= missing;
                    dronesInArea++;
                }
            percentage = Mathf.Clamp01(percentage);
        }
        public void InitModel(GadgetModel model) => ((BatteryModel)model).battery = MaxBattery;
        public void SetModel(GadgetModel model) => this.model = (BatteryModel)model;
        public void AddLiquid(Identifiable.Id id, float units) => model.battery = System.Math.Min(model.battery + MeterMax * units / 3, MaxBattery);
    }
    class PowerReciever : MonoBehaviour
    {
        public BatteryGadget gadget;
    }
    public class BatteryModel : GadgetModel
    {
        public double battery = 0;
        public BatteryModel(Gadget.Id gadgetId, string siteId, Transform transform) : base(gadgetId, siteId, transform)
        {
        }
        public void Push(double batteryLife) => battery = batteryLife;
        public void Pull(out double batteryLife) => batteryLife = battery;
    }

    [HarmonyPatch(typeof(GadgetDirector), "GetPlacementError")]
    class Patch_GadgetDirector_GetPlacementError
    {
        static bool Prefix(GadgetDirector __instance, GadgetSite site, Gadget.Id gadget, ref GadgetDirector.PlacementError __result)
        {
            if (gadget != Id.POWER_BANK)
                return true;
            var def = SRSingleton<GameContext>.Instance.LookupDirector.GetGadgetDefinition(gadget);
            var net = DroneNetwork.Find(site.gameObject);
            if (!net)
            {
                __result = new GadgetDirector.PlacementError()
                {
                    button = "b.place",
                    message = "w.not_on_ranch_battery"
                };
            }
            else if (BatteryGadget.Batteries.FindAll((x) => x.Network == net).Count >= def.countLimit)
            {
                __result = new GadgetDirector.PlacementError()
                {
                    button = "b.limit_reached",
                    message = "w.limit_reached_battery"
                };
            }
            return false;

        }
    }

    [HarmonyPatch(typeof(GameModel), "CreateGadgetModel")]
    class Patch_GameModel_CreateGadgetModel
    {
        static bool Prefix(GadgetModel __instance, GadgetSiteModel site, GameObject gameObj, ref GadgetModel __result)
        {
            var id = gameObj.GetComponent<Gadget>().id;
            if (id == Id.POWER_BANK)
            {
                __result = new BatteryModel(id, site.id, gameObj.transform);
                return false;
            }
            return true;

        }
    }

    [HarmonyPatch(typeof(SavedGame), "Push", typeof(GameModel), typeof(PlacedGadgetV08), typeof(GadgetSiteModel))]
    class Patch_SavedGame_Push_Gadget
    {
        public static PlacedGadgetV08 pushing = null;
        static void Prefix(PlacedGadgetV08 gadget) => pushing = gadget;
        static void Postfix() => pushing = null;
    }

    [HarmonyPatch(typeof(GadgetModel), "NotifyParticipants")]
    class Patch_GadgetModel_NotifyParticipants
    {
        static void Prefix(GadgetModel __instance)
        {
            if (Patch_SavedGame_Push_Gadget.pushing != null && __instance is BatteryModel)
                ((BatteryModel)__instance).Push(Patch_SavedGame_Push_Gadget.pushing.lastSpawnTime);
        }
    }

    [HarmonyPatch(typeof(SavedGame), "Pull", typeof(GameModel), typeof(PlacedGadgetV08), typeof(GadgetModel))]
    class Patch_SavedGame_Pull_Gadget
    {
        static void Prefix(PlacedGadgetV08 gadget, GadgetModel model)
        {
            if (model is BatteryModel)
                ((BatteryModel)model).Pull(out gadget.lastSpawnTime);
        }
    }

    [HarmonyPatch(typeof(DroneStationBattery), "Time", MethodType.Getter)]
    class Patch_DroneStationBattery_GetTime
    {
        static void Postfix(DroneStationBattery __instance, double __result)
        {
            var net = DroneNetwork.Find(__instance.gameObject);
            foreach (var b in BatteryGadget.Batteries)
                if (net == b.Network)
                    __result += b.BatteryPerDrone;
        }
    }

    class DWS
    {
        public static void Setup()
        {
            DimensionWarpSlime.EnergyRegistry.RegisterReceiver<PowerReciever>(
                (x) => (1 - x.gadget.percentage) * 200 * BatteryGadget.MeterCount,
                (x,y) => x.gadget.model.battery = x.gadget.model.battery + y / 200 * BatteryGadget.MeterMax
                );
        }
    }
}