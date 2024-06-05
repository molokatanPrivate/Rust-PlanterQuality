using Facepunch;
using HarmonyLib;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using UnityEngine;


/**
 * possible updates:
 * - make value configurable
 * - support more than 1 priv -> might be usefull to have multiple buff levels
 * - bonus yield modifier for Y genes -> increases positive impact
 * - bonus growth modifier for G genes -> increases positive impact
 * 
 * Note:
 * I have no clue if changes for CARBON are working. Still requires a test. At least its compiling^^
 **/
namespace Oxide.Plugins
{
    [Info("PlanterQuality", "molokatan", "1.0.0"), Description("Adds a quality multiplier for plants in boxes")]
    public class PlanterQuality : RustPlugin
    {
        private static PlanterQuality Instance { get; set; }

        const string perm_use = "planterquality.use";
        const float multiplier = 1.5f;
        const GrowableEntity.Flags UseMultiplierFlag = (GrowableEntity.Flags)0x4000000;

#if CARBON
		private Harmony _harmony;
#endif

        private void Loaded()
        {
            permission.RegisterPermission(perm_use, this);
            Instance = this;

#if CARBON
		    _harmony = new Harmony(Name + "Patch");
            _harmony.PatchAll();
#endif
        }


#if CARBON   
        private void Unload()
        {
            _harmony.UnpatchAll(Name + "Patch");
        }
#endif

        private static void CalculateOverallQuality(GrowableEntity __instance)
        {
            // Note: this value is the reason, why we have to override this method. Its capped at 1.0 and therefore its impossible to get over 100%.
            float a = multiplier;
            if (ConVar.Server.useMinimumPlantCondition)
            {
                a = Mathf.Min(a, __instance.LightQuality);
                a = Mathf.Min(a, __instance.WaterQuality);
                a = Mathf.Min(a, __instance.GroundQuality);
                a = Mathf.Min(a, __instance.TemperatureQuality);
            }
            else
            {
                a = __instance.LightQuality * __instance.WaterQuality * __instance.GroundQuality * __instance.TemperatureQuality;
            }

            __instance.OverallQuality = a;
        }

#if CARBON
#else
        [AutoPatch]
#endif
        [HarmonyPatch(typeof(GrowableEntity), "CalculateQualities")]
        internal class GrowableEntity_CalculateQualities_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(GrowableEntity __instance, bool firstTime, bool forceArtificialLightUpdates = false, bool forceArtificialTemperatureUpdates = false)
            {
                // Note: the plant is dead and the origin code skips quality calculations, so no need to execute it
                if (__instance.IsDead()) return false;

                // Note: this flag shows us, if we run original method or our code
                if (!__instance.HasFlag(UseMultiplierFlag)) return true;

                // Note: we simply run the same logic to not mess up normal behavior
                //       even if it looks like nonesense, it prevents us from running CalculateOverallQuality twice 
                if (__instance.sunExposure == null)
                {
                    __instance.sunExposure = new TimeCachedValue<float>
                    {
                        refreshCooldown = 30f,
                        refreshRandomRange = 5f,
                        updateValue = __instance.SunRaycast
                    };
                }

                if (__instance.artificialLightExposure == null)
                {
                    __instance.artificialLightExposure = new TimeCachedValue<float>
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = __instance.CalculateArtificialLightExposure
                    };
                }

                if (__instance.artificialTemperatureExposure == null)
                {
                    __instance.artificialTemperatureExposure = new TimeCachedValue<float>
                    {
                        refreshCooldown = 60f,
                        refreshRandomRange = 5f,
                        updateValue = __instance.CalculateArtificialTemperature
                    };
                }

                if (forceArtificialTemperatureUpdates)
                {
                    __instance.artificialTemperatureExposure.ForceNextRun();
                }

                // Note: we reduce the risk that future updates brick that plugin -> simply run original methods
                __instance.CalculateLightQuality(forceArtificialLightUpdates || firstTime);
                __instance.CalculateWaterQuality();
                __instance.CalculateWaterConsumption();
                __instance.CalculateGroundQuality(firstTime);
                __instance.CalculateTemperatureQuality();
                
                // this is different: we apply the multiplier
                __instance.LightQuality = __instance.LightQuality * multiplier;
                __instance.WaterQuality = __instance.WaterQuality * multiplier;
                __instance.GroundQuality = __instance.GroundQuality * multiplier;
                __instance.TemperatureQuality = __instance.TemperatureQuality * multiplier;

                // run our "fixed" method
                CalculateOverallQuality(__instance);

                return false;
            }
        }

#if CARBON
#else
        [AutoPatch]
#endif
        [HarmonyPatch(typeof(GrowableEntity), "CalculateQualities_Water")]
        internal class GrowableEntity_CalculateQualities_Water_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(GrowableEntity __instance)
            {
                if (!__instance.HasFlag(UseMultiplierFlag)) return true;
                
                // Note: we reduce the risk that future updates brick that plugin -> simply run original methods
                __instance.CalculateWaterQuality();
                __instance.CalculateWaterConsumption();

                __instance.WaterQuality = __instance.WaterQuality * multiplier;

                CalculateOverallQuality(__instance);
                return false;
            }
        }

#if CARBON
#else
        [AutoPatch]
#endif
        [HarmonyPatch(typeof(GrowableEntity), "UpdateState")]
        internal class GrowableEntity_UpdateState_Patch
        {
            // Note: each time the plant state can change, we check permissions
            //       its not done each tick to reduce impact on server
            [HarmonyPrefix]
            private static bool Prefix(GrowableEntity __instance)
            {
                if (__instance.stageAge <= __instance.currentStage.lifeLengthSeconds) return true;

                PlanterBox box = __instance.GetPlanter();

                if (box == null) return true;

                BuildingPrivlidge priv = box.GetBuildingPrivilege();

                if (priv == null) {
                    // there is no TC, might be destoyed? -> disable effect to be sure
                    __instance.SetFlag(UseMultiplierFlag, false);
                    return true;
                }

                bool authed = false;
                foreach (var playerId in priv.authorizedPlayers)
                {
                    if(!Instance.permission.UserHasPermission(playerId.userid.ToString(), perm_use)) continue;
                    else
                    {
                        authed = true;
                        break;
                    }
                }

                __instance.SetFlag(UseMultiplierFlag, authed);
                // run original method to apply state changes
                return true;
            }
        }

#if CARBON
#else
        [AutoPatch]
#endif
        [HarmonyPatch(typeof(GrowableEntity), "OnDeployed")]
        internal class GrowableEntity_OnDeployed_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(GrowableEntity __instance, BaseEntity parent, BasePlayer deployedBy, Item fromItem)
            {
                // plant is not in a box
                if (parent == null) return true;

                // the box might not be inside priv range -> this is needed to indentify owner later
                BuildingPrivlidge priv = parent.GetBuildingPrivilege();
                if (priv == null || !priv.IsAuthed(deployedBy)) return true;
                
                // FIXME: check permissions of all players in priv -> others can plant
                if (!Instance.permission.UserHasPermission(deployedBy.UserIDString, perm_use)) return true;

                __instance.SetFlag(UseMultiplierFlag, true);
                // Note: the effect will not be active now. Ramps up after next quality calculation
                //       thats fine!
                return true;
            }
        }
    }
}